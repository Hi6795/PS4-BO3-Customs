#!/usr/bin/env python3
"""Exercise real GSC and Lua name handlers at protected memory boundaries."""
from pathlib import Path
import os
import subprocess
import tempfile

root = Path(__file__).resolve().parents[1]
maps = (root / "t7_maps.cpp").read_text(encoding="utf-8")
platform = (root / "platform.cpp").read_text(encoding="utf-8")


def extract(source: str, start: str, following: str) -> str:
    begin = source.index(start)
    return source[begin:source.index(following, begin)]


script_base = extract(maps, "static bool ScriptBase(", "\nstatic void ClaimStackScript(")
note_ui = extract(maps, "static void NoteUiFile(", "\nstatic void RenameStackAsset(")
readable = extract(platform, "bool SafeReadableCString(", "\nbool SafeStrStr(")
rename = extract(maps, "static void RenameStackAsset(", "\nstatic PoolGrowth* GrowthFor(")
assert "SafeReadableCString((uintptr_t)zone, kNameMax" in rename
assert "SafeReadableCString((uintptr_t)name, kNameMax" in rename

fixture = r'''
#include <atomic>
#include <cassert>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <sys/mman.h>
#include <unistd.h>

constexpr int kMaxMods = 8;
constexpr int kMaxModUiFiles = 8;
constexpr size_t kNameMax = 56;
constexpr size_t kInternedMax = 128;

static uintptr_t g_readStart;
static uintptr_t g_readEnd;
static uintptr_t ReadableEnd(uintptr_t address, size_t) {
    return address >= g_readStart && address < g_readEnd ? g_readEnd : 0;
}
[[maybe_unused]] static bool RangeReadable(uintptr_t address, size_t size) {
    return address >= g_readStart && address + size >= address && address + size <= g_readEnd;
}

SAFE_CSTRING

static bool EqualsNoCase(const char* left, const char* right) {
    while (*left && *right) {
        const char l = (*left >= 'A' && *left <= 'Z') ? char(*left + 32) : *left;
        const char r = (*right >= 'A' && *right <= 'Z') ? char(*right + 32) : *right;
        if (l != r) return false;
        ++left;
        ++right;
    }
    return *left == *right;
}
static bool ContainsNoCase(const char* haystack, const char* needle) {
    for (; *haystack; ++haystack) {
        const char* p = haystack;
        const char* n = needle;
        while (*p && *n) {
            const char left = (*p >= 'A' && *p <= 'Z') ? char(*p + 32) : *p;
            const char right = (*n >= 'A' && *n <= 'Z') ? char(*n + 32) : *n;
            if (left != right) break;
            ++p;
            ++n;
        }
        if (!*n) return true;
    }
    return false;
}
static bool LoadsOvercharged(uintptr_t) { return false; }
static void T7Log_Write(const char*, ...) {}
struct ClaimLock { ClaimLock() = default; ~ClaimLock() {} };
struct Mod { char name[kNameMax]; };
struct StackMod { Mod mod; };
static StackMod g_stack[1] = {{{"testmod"}}};
static char g_modUiFiles[kMaxModUiFiles][128]{};
static char g_modUiOwners[kMaxModUiFiles][kNameMax]{};
static char g_uiMods[kMaxMods][kNameMax]{};
static char g_dllMods[kMaxMods][kNameMax]{};
static int g_modUiFileCount = 0;
static int g_uiModCount = 0;
static int g_dllModCount = 0;
static std::atomic<bool> g_modsLuaDirty{false};

SCRIPT_BASE
NOTE_UI

int main() {
    const long page = sysconf(_SC_PAGESIZE);
    assert(page > 256);
    void* mapping = mmap(nullptr, size_t(page) * 2, PROT_READ | PROT_WRITE,
                         MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
    assert(mapping != MAP_FAILED);
    char* const memory = static_cast<char*>(mapping);
    g_readStart = reinterpret_cast<uintptr_t>(memory);
    g_readEnd = g_readStart + uintptr_t(page);
    assert(mprotect(memory + page, size_t(page), PROT_NONE) == 0);

    char lower[kInternedMax]{};
    assert(!ScriptBase(nullptr, lower, sizeof(lower)));
    std::memcpy(memory, "scripts/zm/zmbot.gsc", 21);
    assert(ScriptBase(memory, lower, sizeof(lower)));
    assert(std::strcmp(lower, "scripts/zm/zmbot") == 0);
    std::memcpy(memory, "SCRIPTS/EXAMPLE.CSC", 20);
    assert(ScriptBase(memory, lower, sizeof(lower)));
    assert(std::strcmp(lower, "scripts/example") == 0);

    char* const tail = memory + page - 4;
    std::memcpy(tail, "gscx", 4); // No terminator before the unreadable page.
    assert(!ScriptBase(tail, lower, sizeof(lower)));

    std::memcpy(memory, "ui/example.lua", 15);
    uintptr_t header = reinterpret_cast<uintptr_t>(&memory);
    NoteUiFile(header, 0);
    assert(g_modUiFileCount == 1);
    assert(std::strcmp(g_modUiFiles[0], "ui/example.lua") == 0);
    assert(std::strcmp(g_modUiOwners[0], "testmod") == 0);
    NoteUiFile(header, 0);
    assert(g_modUiFileCount == 1); // same asset doesn't duplicate UI ownership

    char* unsafeName = tail;
    std::memcpy(unsafeName, "ui/z", 4);
    header = reinterpret_cast<uintptr_t>(&unsafeName);
    NoteUiFile(header, 0);
    assert(g_modUiFileCount == 1); // Reject unterminated name, never scan into PROT_NONE.

    std::memset(memory, 'a', 128);
    header = reinterpret_cast<uintptr_t>(&memory);
    NoteUiFile(header, 0);
    assert(g_modUiFileCount == 1);

    assert(munmap(mapping, size_t(page) * 2) == 0);
    std::puts("PASS: script and Lua names reject inaccessible/unterminated memory without losing valid assets");
}
'''.replace("SAFE_CSTRING", readable).replace("SCRIPT_BASE", script_base).replace("NOTE_UI", note_ui)

with tempfile.TemporaryDirectory(prefix="bo3-script-asset-name-") as directory:
    cpp = Path(directory) / "script.cpp"
    binary = Path(directory) / "script"
    cpp.write_text(fixture, encoding="utf-8")
    subprocess.run([os.environ.get("CXX", "g++"), "-std=c++20", "-Wall", "-Wextra", "-Werror",
                    "-fsanitize=address,undefined", str(cpp), "-o", str(binary)], check=True)
    subprocess.run([str(binary)], check=True)
