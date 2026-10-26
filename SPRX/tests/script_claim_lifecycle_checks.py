#!/usr/bin/env python3
"""Exercise the real SPRX claim/register/prune bodies for two active mods."""
from pathlib import Path
import os
import subprocess
import tempfile


source = (Path(__file__).resolve().parents[1] / "t7_maps.cpp").read_text(encoding="utf-8")


def fragment(start: str, end: str) -> str:
    first = source.index(start)
    return source[first:source.index(end, first)]


functions = "\n".join((
    fragment("static const char* InternName(", "static void PruneUnloadedModUiFiles()"),
    fragment("static bool ScriptBase(", "static bool BufferHas("),
    fragment("static void RemoveClaimsFrom(", "static bool FreeModsFrom("),
))

fixture = r'''
#include <cctype>
#include <cstdint>
#include <cstdio>
#include <cstring>

constexpr int kInternedMax = 128;
constexpr int kMaxInterned = 1024;
constexpr int kMaxScriptClaims = 1024;
struct ScriptClaim { char base[kInternedMax]; int owner; };
struct StackMod { struct { char name[56]; } mod; };
static ScriptClaim g_scriptClaims[kMaxScriptClaims]{};
static int g_scriptClaimCount = 0;
static char g_interned[kMaxInterned][kInternedMax]{};
static int g_internedCount = 0;
static StackMod g_stack[2]{};

struct ClaimLock { ClaimLock() {} ~ClaimLock() {} };
static void T7Log_Write(const char*, ...) {}
static bool EqualsNoCase(const char* a, const char* b) {
    while (*a && *b) {
        if (std::tolower((unsigned char)*a) != std::tolower((unsigned char)*b)) return false;
        ++a; ++b;
    }
    return *a == *b;
}
static bool SafeReadableCString(uintptr_t ptr, size_t capacity, size_t* used) {
    if (!ptr) return false;
    const char* text = (const char*)ptr;
    for (size_t i = 0; i < capacity; ++i) {
        if (!text[i]) { if (used) *used = i; return true; }
    }
    return false;
}

PRODUCTION

static int Failure(const char* scenario) {
    std::fprintf(stderr, "FAIL: %s\n", scenario);
    return 1;
}
int main() {
    std::snprintf(g_stack[0].mod.name, sizeof(g_stack[0].mod.name), "bots");
    std::snprintf(g_stack[1].mod.name, sizeof(g_stack[1].mod.name), "eight-player");

    struct Header { const char* name; } first{"scripts/bots.gsc"};
    Header second{"scripts/bots.csc"};
    Header sameSecond{"scripts/bots.gsc"};
    Header uniqueSecond{"scripts/eightplayer.gsc"};

    ClaimStackScript((uintptr_t)&first, 0);
    if (g_scriptClaimCount != 1 || g_scriptClaims[0].owner != 0 ||
        std::strcmp(first.name, "scripts/bots.gsc")) return Failure("first mod ownership");
    ClaimStackScript((uintptr_t)&second, 1);
    if (g_scriptClaimCount != 1 || std::strcmp(second.name, "m2_scripts/bots.csc"))
        return Failure("second mod duplicate was not hidden");
    ClaimStackScript((uintptr_t)&sameSecond, 1);
    if (std::strcmp(sameSecond.name, "m2_scripts/bots.gsc") || g_scriptClaimCount != 1)
        return Failure("cross-mod GSC collision");
    ClaimStackScript((uintptr_t)&uniqueSecond, 1);
    if (g_scriptClaimCount != 2 || g_scriptClaims[1].owner != 1)
        return Failure("unique second-mod claim");

    RemoveClaimsFrom(1);
    if (g_scriptClaimCount != 1 || g_scriptClaims[0].owner != 0)
        return Failure("partial unload dropped surviving mod or retained removed mod");
    Header reloadedSecond{"scripts/bots.csc"};
    ClaimStackScript((uintptr_t)&reloadedSecond, 1);
    if (std::strcmp(reloadedSecond.name, "m2_scripts/bots.csc") ||
        reloadedSecond.name != second.name)
        return Failure("second mod reload duplicate or leaked interning");

    ResetScriptClaims();
    if (g_scriptClaimCount) return Failure("full unload left a claim");
    Header freshSecond{"scripts/bots.csc"};
    ClaimStackScript((uintptr_t)&freshSecond, 1);
    if (g_scriptClaimCount != 1 || g_scriptClaims[0].owner != 1 ||
        std::strcmp(freshSecond.name, "scripts/bots.csc"))
        return Failure("fresh session failed to release old ownership");
    std::puts("PASS: two-mod claims, GSC/CSC collision, partial unload, and reload recovery");
    return 0;
}
'''.replace("PRODUCTION", functions)

with tempfile.TemporaryDirectory(prefix="bo3-claims-") as directory:
    cpp = Path(directory) / "claims.cpp"
    executable = Path(directory) / "claims"
    cpp.write_text(fixture, encoding="utf-8")
    subprocess.run([os.environ.get("CXX", "g++"), "-std=c++20", "-Wall", "-Wextra", "-Werror", "-O1",
                    "-fsanitize=address,undefined", "-fno-omit-frame-pointer", str(cpp), "-o", str(executable)],
                   check=True)
    subprocess.run([str(executable)], check=True)
