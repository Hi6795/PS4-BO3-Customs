#include <kernel.h>
#include "Detour.hpp"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/mman.h>
#include <sys/wait.h>
#include <unistd.h>

static int mapped = 0, unmapped = 0, protectedCalls = 0, delayed = 0;
static int failProtectionCall = 0;
static const void* deniedAddress = nullptr;

#define CHECK(condition) do { if (!(condition)) { \
    fprintf(stderr, "line %d: %s\n", __LINE__, #condition); exit(1); \
} } while (0)

int sceKernelMmap(void* hint, size_t bytes, int protection, int, int, int64_t, void** out)
{
    void* page = mmap(hint, bytes, protection, MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
    *out = page == MAP_FAILED ? nullptr : page;
    if (page == MAP_FAILED)
        return -1;
    ++mapped;
    return 0;
}

int sceKernelMunmap(void* page, size_t bytes)
{
    const int result = munmap(page, bytes);
    if (result == 0)
        ++unmapped;
    return result;
}

int sceKernelMprotect(const void* address, size_t bytes, int protection)
{
    ++protectedCalls;
    if (address == deniedAddress || protectedCalls == failProtectionCall)
        return -1;
    const uintptr_t mask = static_cast<uintptr_t>(sysconf(_SC_PAGESIZE)) - 1;
    const uintptr_t begin = reinterpret_cast<uintptr_t>(address) & ~mask;
    const uintptr_t end = (reinterpret_cast<uintptr_t>(address) + bytes + mask) & ~mask;
    return mprotect(reinterpret_cast<void*>(begin), end - begin, protection);
}

int sceKernelUsleep(unsigned int)
{
    ++delayed;
    return 0;
}

using Function = int (*)();
static int Replacement() { return 42; }

struct Target
{
    size_t bytes = static_cast<size_t>(sysconf(_SC_PAGESIZE));
    void* page = mmap(nullptr, bytes, PROT_READ | PROT_WRITE, MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
    unsigned char original[32];

    Target()
    {
        CHECK(page != MAP_FAILED);
        // mov eax,7; nine NOPs; ret. The first fourteen bytes form the patch.
        memset(page, 0x90, bytes);
        const unsigned char instructions[] = {0xB8, 7, 0, 0, 0};
        memcpy(page, instructions, sizeof(instructions));
        static_cast<unsigned char*>(page)[14] = 0xC3;
        memcpy(original, page, sizeof(original));
        ReadOnly();
        CHECK(Call() == 7);
    }

    ~Target() { CHECK(munmap(page, bytes) == 0); }
    void ReadOnly() { CHECK(mprotect(page, bytes, PROT_READ | PROT_EXEC) == 0); }
    int Call() const { return reinterpret_cast<Function>(page)(); }
    bool Unchanged() const { return memcmp(original, page, sizeof(original)) == 0; }
    uint64_t Address() const { return reinterpret_cast<uint64_t>(page); }
};

static void SuccessfulAttachAndDetach()
{
    Target target;
    Detour detour{};
    void* stub = nullptr;
    CHECK(Detour_Attach(&detour, target.Address(), reinterpret_cast<void*>(Replacement), &stub) == stub);
    CHECK(stub != nullptr && detour.Installed);
    CHECK(target.Call() == 42 && reinterpret_cast<Function>(stub)() == 7);
    const int allocations = mapped;
    void* sameStub = nullptr;
    CHECK(Detour_Attach(&detour, target.Address(), reinterpret_cast<void*>(Replacement), &sameStub) == stub);
    CHECK(sameStub == stub && mapped == allocations);
    Detour_Detach(&detour);
    CHECK(target.Unchanged() && target.Call() == 7);
    CHECK(!detour.Installed && detour.StubPtr == nullptr && mapped == unmapped);
}

static void FailedTargetWriteCanRetry()
{
    Target target;
    Detour detour{};
    void* stub = reinterpret_cast<void*>(1);
    deniedAddress = target.page;
    CHECK(Detour_Attach(&detour, target.Address(), reinterpret_cast<void*>(Replacement), &stub) == nullptr);
    CHECK(stub == nullptr && !detour.Installed && target.Unchanged());
    CHECK(mapped > 0 && mapped == unmapped);
    deniedAddress = nullptr;
    CHECK(Detour_Attach(&detour, target.Address(), reinterpret_cast<void*>(Replacement), &stub) != nullptr);
    CHECK(target.Call() == 42 && reinterpret_cast<Function>(stub)() == 7);
    Detour_Detach(&detour);
    CHECK(target.Unchanged() && mapped == unmapped);
}

static void FailedTrampolineWrite(int call)
{
    Target target;
    Detour detour{};
    void* stub = reinterpret_cast<void*>(1);
    failProtectionCall = call;
    CHECK(Detour_Attach(&detour, target.Address(), reinterpret_cast<void*>(Replacement), &stub) == nullptr);
    CHECK(stub == nullptr && !detour.Installed && target.Unchanged());
    CHECK(protectedCalls == call && mapped > 0 && mapped == unmapped);
}

static void FailedJumpBackWrite() { FailedTrampolineWrite(1); }
static void FailedBridgeWrite() { FailedTrampolineWrite(2); }

static void FailedRestoreKeepsLiveTrampoline()
{
    Target target;
    Detour detour{};
    void* stub = nullptr;
    CHECK(Detour_Attach(&detour, target.Address(), reinterpret_cast<void*>(Replacement), &stub) != nullptr);
    const Detour before = detour;
    const int releases = unmapped;
    target.ReadOnly();
    deniedAddress = target.page;
    Detour_Detach(&detour);
    CHECK(memcmp(&before, &detour, sizeof(detour)) == 0);
    CHECK(unmapped == releases && delayed == 0);
    CHECK(target.Call() == 42 && reinterpret_cast<Function>(stub)() == 7);

    Detour duplicate{};
    void* duplicateStub = reinterpret_cast<void*>(1);
    CHECK(Detour_Attach(&duplicate, target.Address(), reinterpret_cast<void*>(Replacement), &duplicateStub) == nullptr);
    CHECK(duplicateStub == nullptr);

    deniedAddress = nullptr;
    Detour_Detach(&detour);
    CHECK(target.Unchanged() && !detour.Installed && mapped == unmapped);
    CHECK(Detour_Attach(&duplicate, target.Address(), reinterpret_cast<void*>(Replacement), &duplicateStub) != nullptr);
    Detour_Detach(&duplicate);
    CHECK(target.Unchanged() && target.Call() == 7 && mapped == unmapped);
}

static void InvalidInputClearsOutput()
{
    void* stub = reinterpret_cast<void*>(1);
    CHECK(Detour_Attach(nullptr, 0, nullptr, &stub) == nullptr);
    CHECK(stub == nullptr && mapped == 0);
}

int main()
{
    setvbuf(stdout, nullptr, _IONBF, 0);
    const struct { const char* name; void (*run)(); } checks[] = {
        {"successful attach, original call, idempotence and detach", SuccessfulAttachAndDetach},
        {"denied target write cleans up and permits retry", FailedTargetWriteCanRetry},
        {"denied jump-back write cleans up", FailedJumpBackWrite},
        {"denied bridge write cleans up", FailedBridgeWrite},
        {"denied restore preserves hook, trampoline and retry", FailedRestoreKeepsLiveTrampoline},
        {"invalid input clears output without allocating", InvalidInputClearsOutput},
    };
    int failures = 0;
    for (const auto& check : checks)
    {
        const pid_t pid = fork();
        CHECK(pid >= 0);
        if (pid == 0) { check.run(); _exit(0); }
        int status = 0;
        CHECK(waitpid(pid, &status, 0) == pid);
        const bool passed = WIFEXITED(status) && WEXITSTATUS(status) == 0;
        printf("%s: %s", passed ? "PASS" : "FAIL", check.name);
        if (WIFSIGNALED(status)) printf(" (signal %d)", WTERMSIG(status));
        printf("\n");
        if (!passed) ++failures;
    }
    printf("%zu checks, %d failures\n", sizeof(checks) / sizeof(checks[0]), failures);
    return failures ? 1 : 0;
}
