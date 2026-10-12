#include "headers.hpp"
#include "t7_builtins.hpp"
#include "t7_config.hpp"

#include <atomic>

namespace T7Builtins
{
constexpr uintptr_t kGscFunction = 0xAA79C0;
constexpr uintptr_t kGscMethod   = 0xAA7BF0;
constexpr uintptr_t kCscFunction = 0x6C0E20;

constexpr uintptr_t kScrGetConstString = 0x78BD10;
constexpr uintptr_t kScrFreeValue      = 0x786650;
constexpr uintptr_t kScrOverflow       = 0x78CA10;
constexpr uintptr_t kScrError          = 0x78BE10;
constexpr uintptr_t kDvarFind          = 0xFB6ED0;
constexpr uintptr_t kDvarSet           = 0xFBC3D0;
constexpr uintptr_t kDvarSetByName     = 0xFBC5A0;

constexpr uintptr_t kScriptStrings   = 0x33C3848;
constexpr uintptr_t kScrVmState      = 0x3453C90;
constexpr uintptr_t kScrErrorText    = 0x3448A80;
constexpr uintptr_t kScrErrorBuffer  = 0x35852B0;
constexpr uintptr_t kEntities        = 0x66EAFE0;
constexpr uintptr_t kCharacterTables = 0x1F276F0;

constexpr size_t kVmStride   = 0x8A38;
constexpr size_t kVmStackEnd = 8;
constexpr size_t kVmTop      = 32;
constexpr size_t kVmFatal    = 42;
constexpr size_t kVmOutCount = 48;
constexpr size_t kVmInCount  = 52;

constexpr size_t kEntityStride = 1264;
constexpr size_t kEntityClient = 592;
constexpr size_t kEntityBot    = 632;
constexpr size_t kEntityFlags  = 680;

constexpr uint32_t kFlagGod     = 1;
constexpr uint32_t kFlagDemiGod = 2;

constexpr size_t kDvarFlags      = 0x10;
constexpr uint32_t kDvarSaved    = 1;
constexpr uint32_t kDvarModFlags = 0x20001;

constexpr int    kCharacterModes  = 4;
constexpr size_t kCharacterStride = 24;
constexpr uint32_t kNoMode        = 3;

constexpr uint32_t kTypeInt = 7;

constexpr int kMaxVars    = 64;
constexpr int kSaveFrames = 120;

const uint8_t k_gscFunction[] = { 0x31, 0xC0, 0xC7, 0x06, 0x00, 0x00, 0x00, 0x00, 0x81, 0xFF, 0xE3, 0x46,
                                  0xF4, 0x43, 0x74, 0x5B, 0xB8, 0x01, 0x00, 0x00, 0x00, 0x81, 0xFF, 0x23 };
const uint8_t k_gscMethod[]   = { 0xC7, 0x06, 0x00, 0x00, 0x00, 0x00, 0x45, 0x31, 0xD2, 0x4C, 0x8D, 0x0D,
                                  0xC0, 0x89, 0x89, 0x05, 0x45, 0x89, 0xD0, 0x49, 0xC1, 0xE0, 0x05, 0x43 };
const uint8_t k_cscFunction[] = { 0x45, 0x31, 0xC9, 0x4C, 0x8D, 0x05, 0x46, 0xFA, 0x92, 0x02, 0x66, 0x0F,
                                  0x1F, 0x44, 0x00, 0x00, 0x44, 0x89, 0xC8, 0x48, 0xC1, 0xE0, 0x05, 0x42 };
const uint8_t k_scrError[]    = { 0x55, 0x48, 0x89, 0xE5, 0x89, 0xF9, 0x48, 0x8D, 0x05, 0x03, 0xFE, 0xCB,
                                  0x02, 0x48, 0x8D, 0x35 };
const uint8_t k_dvarFind[]    = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x56, 0x53, 0x48, 0x83, 0xEC, 0x10, 0x4C,
                                  0x8B, 0x35, 0xCE, 0x99 };

using Lookup_t = void* (*)(int hash, int* type, int* minArgs, int* maxArgs);

struct Builtin
{
    uint32_t hash;
    void*    handler;
    int      minArgs;
    int      maxArgs;
};

struct ModVar
{
    char name[64];
    char value[160];
};

static uintptr_t g_base = 0;

static Detour g_gscFunctionDetour{};
static Detour g_gscMethodDetour{};
static Detour g_cscFunctionDetour{};
static void*  g_gscFunctionOriginal = nullptr;
static void*  g_gscMethodOriginal = nullptr;
static void*  g_cscFunctionOriginal = nullptr;

static std::atomic<bool> g_varsBusy{false};
static std::atomic<bool> g_varsDirty{false};
static std::atomic<bool> g_varsRestored{false};
static bool              g_varsRead = false;
static ModVar            g_vars[kMaxVars];
static int               g_varCount = 0;
static int               g_saveWait = 0;

struct VarLock
{
    VarLock()
    {
        while (g_varsBusy.exchange(true, std::memory_order_acquire))
            sceKernelUsleep(50);
    }

    ~VarLock()
    {
        g_varsBusy.store(false, std::memory_order_release);
    }
};

static bool Matches(uintptr_t base, uintptr_t offset, const uint8_t* bytes, size_t size)
{
    const uintptr_t at = base + offset;
    return RangeReadable(at, size) && memcmp((const void*)at, bytes, size) == 0;
}

static uintptr_t Vm(uint32_t inst)
{
    return g_base + kScrVmState + kVmStride * (uintptr_t)inst;
}

static void ScriptError(uint32_t inst, const char* text)
{
    uint64_t* const pending = (uint64_t*)(g_base + kScrErrorText) + 14 * (uintptr_t)inst + 2;

    if (!*pending)
    {
        char* const buffer = (char*)(g_base + kScrErrorBuffer);
        snprintf(buffer, 1024, "%s", text);
        *pending = (uint64_t)buffer;
    }

    *(uint8_t*)(Vm(inst) + kVmFatal) = 0;
    ((void (*)(uint32_t))(g_base + kScrError))(inst);
}

static const char* GetString(uint32_t inst, uint32_t index)
{
    const uint32_t id = ((uint32_t (*)(uint32_t, uint32_t))(g_base + kScrGetConstString))(inst, index);
    const uintptr_t strings = *(const uintptr_t*)(g_base + kScriptStrings);
    return id && strings ? (const char*)(strings + 28 * (uintptr_t)id + 4) : nullptr;
}

static void AddInt(uint32_t inst, int value)
{
    const uintptr_t vm = Vm(inst);
    uint64_t* const top = (uint64_t*)(vm + kVmTop);
    uint32_t* const incoming = (uint32_t*)(vm + kVmInCount);

    if (*incoming)
    {
        uint64_t at = *top;
        uint32_t left = *incoming;

        *top -= 16 * (uint64_t)left;
        *incoming = 0;

        for (; left; --left, at -= 16)
            ((void (*)(uint64_t, uint64_t))(g_base + kScrFreeValue))(inst, at);
    }

    if (*top == *(const uint64_t*)(vm + kVmStackEnd))
        ((void (*)())(g_base + kScrOverflow))();

    *top += 16;
    ++*(uint32_t*)(vm + kVmOutCount);
    *(uint32_t*)(*top + 8) = kTypeInt;
    *(int32_t*)(*top) = value;
}

static bool ValidName(const char* name)
{
    if (!name || !name[0])
        return false;

    for (const char* c = name; *c; ++c)
    {
        const bool letter = (*c >= 'a' && *c <= 'z') || (*c >= 'A' && *c <= 'Z');

        if (!letter && !(*c >= '0' && *c <= '9') && *c != '_')
            return false;
    }

    return true;
}

static bool SameName(const char* a, const char* b)
{
    for (;; ++a, ++b)
    {
        const char x = (*a >= 'A' && *a <= 'Z') ? (char)(*a + 32) : *a;
        const char y = (*b >= 'A' && *b <= 'Z') ? (char)(*b + 32) : *b;

        if (x != y)
            return false;

        if (!x)
            return true;
    }
}

static void Remember(const char* name, const char* value, bool changed)
{
    if (strlen(name) >= sizeof(g_vars[0].name))
        return;

    ModVar* slot = nullptr;
    bool added = false;

    for (int i = 0; i < g_varCount && !slot; ++i)
    {
        if (SameName(g_vars[i].name, name))
            slot = &g_vars[i];
    }

    if (!slot)
    {
        if (g_varCount >= kMaxVars)
            return;

        slot = &g_vars[g_varCount++];
        snprintf(slot->name, sizeof(slot->name), "%s", name);
        slot->value[0] = 0;
        added = true;
    }

    char clean[sizeof(slot->value)];
    size_t used = 0;

    for (const char* c = value; *c && used + 1 < sizeof(clean); ++c)
        clean[used++] = (*c == '"') ? '\'' : ((*c == '\n' || *c == '\r') ? ' ' : *c);

    clean[used] = 0;

    if (!added && strcmp(slot->value, clean) == 0)
        return;

    memcpy(slot->value, clean, used + 1);

    if (changed)
        g_varsDirty.store(true, std::memory_order_relaxed);
}

static void ReadVars()
{
    if (g_varsRead)
        return;

    g_varsRead = true;

    static char text[32768];

    if (!T7Config_Read(text, sizeof(text)))
        return;

    for (char* line = text; line && *line;)
    {
        char* const next = strchr(line, '\n');

        if (next)
            *next = 0;

        if (strncmp(line, "modvar ", 7) == 0)
        {
            char* const name = line + 7;
            char* const open = strchr(name, '"');
            char* const close = open ? strrchr(open + 1, '"') : nullptr;

            if (open && close && open > name && open[-1] == ' ')
            {
                open[-1] = 0;
                *close = 0;

                if (ValidName(name))
                    Remember(name, open + 1, false);
            }
        }

        line = next ? next + 1 : nullptr;
    }
}

static uintptr_t FindDvar(const char* name)
{
    return ((uintptr_t (*)(const char*))(g_base + kDvarFind))(name);
}

static void RegisterDvar(const char* name, const char* value)
{
    ((void (*)(const char*, const char*, uint32_t, uint32_t))(g_base + kDvarSetByName))(name, value, 0, kDvarModFlags);
}

static void RestoreVars()
{
    if (g_varsRestored.load(std::memory_order_relaxed) || !T7Maps_UsingMods())
        return;

    VarLock lock;

    if (g_varsRestored.exchange(true))
        return;

    ReadVars();

    int made = 0;

    for (int i = 0; i < g_varCount; ++i)
    {
        if (FindDvar(g_vars[i].name))
            continue;

        RegisterDvar(g_vars[i].name, g_vars[i].value);
        ++made;
    }

    if (g_varCount)
        T7Log_Write("[Scripts] %d saved mod variable(s), %d restored", g_varCount, made);
}

static bool OwnsLine(const char* line)
{
    return strncmp(line, "modvar ", 7) == 0;
}

static void IsUsingMods(uint32_t inst)
{
    AddInt(inst, T7Maps_UsingMods() ? 1 : 0);
}

static void ModVarCall(uint32_t inst)
{
    const char* const name = GetString(inst, 0);

    if (!name || !name[0])
        return;

    if (!ValidName(name))
    {
        char text[256];
        snprintf(text, sizeof(text), "Dvar %s has an invalid dvar name", name);
        ScriptError(inst, text);
        return;
    }

    const char* const value = GetString(inst, 1);

    if (!value || !T7Maps_UsingMods())
        return;

    const uintptr_t dvar = FindDvar(name);

    if (dvar)
    {
        if (!(*(const uint32_t*)(dvar + kDvarFlags) & kDvarSaved))
            return;

        ((void (*)(uintptr_t, const char*, uint32_t))(g_base + kDvarSet))(dvar, value, 0);
    }
    else
    {
        RegisterDvar(name, value);
    }

    VarLock lock;
    ReadVars();
    Remember(name, value, true);
}

static void ClearAllCharacterTables(uint32_t)
{
    for (int i = 0; i < kCharacterModes; ++i)
    {
        const uintptr_t entry = g_base + kCharacterTables + kCharacterStride * (uintptr_t)i;

        *(uint32_t*)(entry) = kNoMode;
        *(uint32_t*)(entry + 4) = kNoMode;
        *(uint64_t*)(entry + 8) = 0;
        *(uint64_t*)(entry + 16) = 0;
    }
}

static void Nothing(uint32_t)
{
}

static void NothingOn(uint32_t, uint32_t, uint32_t)
{
}

static void BotToggle(uint32_t entity, uint32_t classnum, uint32_t own, uint32_t other)
{
    if ((uint16_t)classnum)
    {
        ScriptError(0, "not an entity");
        return;
    }

    const uintptr_t ent = g_base + kEntities + kEntityStride * (uintptr_t)entity;

    if (!*(const uint64_t*)(ent + kEntityClient) || !*(const uint64_t*)(ent + kEntityBot))
    {
        char text[64];
        snprintf(text, sizeof(text), "Entity %i is not a bot", (int)entity);
        ScriptError(0, text);
        return;
    }

    uint32_t* const flags = (uint32_t*)(ent + kEntityFlags);

    *flags = (*flags ^ own) & ~other;
    AddInt(0, (*flags & own) ? 1 : 0);
}

static void BotToggleGodMode(uint32_t, uint32_t entity, uint32_t classnum)
{
    BotToggle(entity, classnum, kFlagGod, kFlagDemiGod);
}

static void BotToggleDemiGodMode(uint32_t, uint32_t entity, uint32_t classnum)
{
    BotToggle(entity, classnum, kFlagDemiGod, kFlagGod);
}

const Builtin k_commonFunctions[] =
{
    { 0x77110410, (void*)IsUsingMods, 0, 0 },
    { 0x8B57C052, (void*)ModVarCall, 2, 2 },
    { 0x4C0DC03D, (void*)ClearAllCharacterTables, 0, 0 },
};

const Builtin k_gscFunctions[] =
{
    { 0x594D2BDF, (void*)Nothing, 1, 1 },
    { 0x64F7CBC3, (void*)Nothing, 0, 0 },
};

const Builtin k_gscMethods[] =
{
    { 0xAF89AC8F, (void*)BotToggleGodMode, 0, 0 },
    { 0xA71FA5A2, (void*)BotToggleDemiGodMode, 0, 0 },
    { 0xFA5D216B, (void*)NothingOn, 2, 2 },
    { 0xE1F3D6D7, (void*)NothingOn, 0, 0 },
};

template <size_t N>
static void* Find(const Builtin (&table)[N], int hash, int* type, int* minArgs, int* maxArgs)
{
    for (const Builtin& builtin : table)
    {
        if (builtin.hash != (uint32_t)hash)
            continue;

        if (type)
            *type = 0;

        if (minArgs)
            *minArgs = builtin.minArgs;

        if (maxArgs)
            *maxArgs = builtin.maxArgs;

        return builtin.handler;
    }

    return nullptr;
}

static void* GscFunction_h(int hash, int* type, int* minArgs, int* maxArgs)
{
    RestoreVars();

    if (void* const found = ((Lookup_t)g_gscFunctionOriginal)(hash, type, minArgs, maxArgs))
        return found;

    if (void* const common = Find(k_commonFunctions, hash, type, minArgs, maxArgs))
        return common;

    return Find(k_gscFunctions, hash, type, minArgs, maxArgs);
}

static void* GscMethod_h(int hash, int* type, int* minArgs, int* maxArgs)
{
    if (void* const found = ((Lookup_t)g_gscMethodOriginal)(hash, type, minArgs, maxArgs))
        return found;

    return Find(k_gscMethods, hash, type, minArgs, maxArgs);
}

static void* CscFunction_h(int hash, int* type, int* minArgs, int* maxArgs)
{
    if (void* const found = ((Lookup_t)g_cscFunctionOriginal)(hash, type, minArgs, maxArgs))
        return found;

    return Find(k_commonFunctions, hash, type, minArgs, maxArgs);
}
}

void T7Builtins_Install(uintptr_t base)
{
    using namespace T7Builtins;

    static bool installed = false;

    if (installed || !base)
        return;

    installed = true;

    if (!Matches(base, kGscFunction, k_gscFunction, sizeof(k_gscFunction)) ||
        !Matches(base, kGscMethod, k_gscMethod, sizeof(k_gscMethod)) ||
        !Matches(base, kCscFunction, k_cscFunction, sizeof(k_cscFunction)) ||
        !Matches(base, kScrError, k_scrError, sizeof(k_scrError)) ||
        !Matches(base, kDvarFind, k_dvarFind, sizeof(k_dvarFind)))
        return;

    g_base = base;

    Detour_Attach(&g_gscFunctionDetour, (uint64_t)(base + kGscFunction), (void*)GscFunction_h, &g_gscFunctionOriginal);
    Detour_Attach(&g_gscMethodDetour, (uint64_t)(base + kGscMethod), (void*)GscMethod_h, &g_gscMethodOriginal);
    Detour_Attach(&g_cscFunctionDetour, (uint64_t)(base + kCscFunction), (void*)CscFunction_h, &g_cscFunctionOriginal);

    T7Log_Write("[Scripts] PC builtins: functions %s, methods %s, client functions %s",
                g_gscFunctionOriginal ? "on" : "off", g_gscMethodOriginal ? "on" : "off",
                g_cscFunctionOriginal ? "on" : "off");
}

void T7Builtins_Tick()
{
    using namespace T7Builtins;

    if (!g_base || !g_varsDirty.load(std::memory_order_relaxed))
        return;

    if (++g_saveWait < kSaveFrames)
        return;

    g_saveWait = 0;

    static char lines[kMaxVars * (sizeof(ModVar) + 16)];
    size_t used = 0;

    {
        VarLock lock;
        g_varsDirty.store(false, std::memory_order_relaxed);
        lines[0] = 0;

        for (int i = 0; i < g_varCount; ++i)
        {
            const int wrote = snprintf(lines + used, sizeof(lines) - used, "modvar %s \"%s\"\n", g_vars[i].name,
                                       g_vars[i].value);

            if (wrote <= 0 || used + (size_t)wrote >= sizeof(lines))
                break;

            used += (size_t)wrote;
        }
    }

    if (!T7Config_Save("mod variables", OwnsLine, lines))
        T7Log_Write("[Scripts] mod variables could not be saved to config.cfg");
}
