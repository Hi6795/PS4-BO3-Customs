#!/usr/bin/env python3
"""Source-extracted host test for the SPRX UI-file claim lifecycle (not a PS4 test)."""
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile

source = Path(sys.argv[1]).read_text(encoding="utf-8")
start = source.index("static void PruneUnloadedModUiFiles()")
brace = source.index("{", start)
depth = 0
for i in range(brace, len(source)):
    depth += (source[i] == "{") - (source[i] == "}")
    if depth == 0:
        function = source[start:i + 1]
        break
else:
    raise AssertionError("unterminated prune helper")
assert "ClaimLock locked;" in function
assert re.search(r"static void SetLoaded\(int count\).*?PruneUnloadedModUiFiles\(\);", source, re.S)
assert re.search(r"if \(wanted == 0\).*?ResetScriptClaims\(\);\s*PruneUnloadedModUiFiles\(\);", source, re.S)
assert re.search(r"g_loadedCount\s*=\s*0;\s*ResetScriptClaims\(\);\s*PruneUnloadedModUiFiles\(\);", source)

program = r'''
#include <atomic>
#include <cctype>
#include <cstdio>
#include <cstring>
constexpr int kMaxModUiFiles = 1024, kMaxStack = 24;
constexpr size_t kNameMax = 56;
static char g_modUiFiles[kMaxModUiFiles][96], g_modUiOwners[kMaxModUiFiles][kNameMax];
static int g_modUiFileCount = 0, g_loadedCount = 0;
static char g_loaded[kMaxStack][kNameMax];
static std::atomic<bool> g_modsLuaDirty{false};
struct ClaimLock { ClaimLock() {} ~ClaimLock() {} };
static bool EqualsNoCase(const char* a, const char* b) {
    while (*a && *b) {
        if (std::tolower((unsigned char)*a) != std::tolower((unsigned char)*b)) return false;
        ++a; ++b;
    }
    return *a == *b;
}
FUNCTION
static void add(const char* owner, const char* file) {
    if (g_modUiFileCount >= kMaxModUiFiles) return;
    std::snprintf(g_modUiFiles[g_modUiFileCount], sizeof(g_modUiFiles[0]), "%s", file);
    std::snprintf(g_modUiOwners[g_modUiFileCount], sizeof(g_modUiOwners[0]), "%s", owner);
    ++g_modUiFileCount;
}
int main() {
    for (int i = 0; i < kMaxModUiFiles; ++i) {
        char file[64]; std::snprintf(file, sizeof(file), "ui/old/%04d.lua", i);
        add("OldMod", file);
    }
    add("NewMod", "ui/new.lua");
    if (g_modUiFileCount != 1024) return 1;
    PruneUnloadedModUiFiles();
    if (g_modUiFileCount != 0 || !g_modsLuaDirty.load()) return 2;
    add("NewMod", "ui/new.lua");
    if (g_modUiFileCount != 1 || std::strcmp(g_modUiOwners[0], "NewMod")) return 3;
    g_modUiFileCount = 0;
    add("OldMod", "ui/old.lua");
    add("newMOD", "ui/first.lua");
    add("OldMod", "ui/old2.lua");
    add("NewMod", "ui/second.lua");
    std::snprintf(g_loaded[0], sizeof(g_loaded[0]), "NEWMOD");
    g_loadedCount = 1;
    g_modsLuaDirty.store(false);
    PruneUnloadedModUiFiles();
    if (g_modUiFileCount != 2 || !g_modsLuaDirty.load()) return 4;
    if (std::strcmp(g_modUiFiles[0], "ui/first.lua") ||
        std::strcmp(g_modUiFiles[1], "ui/second.lua")) return 5;
    g_modsLuaDirty.store(false);
    PruneUnloadedModUiFiles();
    if (g_modUiFileCount != 2 || g_modsLuaDirty.load()) return 6;
    g_loadedCount = 0;
    PruneUnloadedModUiFiles();
    if (g_modUiFileCount != 0 || !g_modsLuaDirty.load()) return 7;
    return 0;
}
'''.replace("FUNCTION", function)

compilers = [x for x in ("g++", "clang++") if shutil.which(x)]
if not compilers:
    raise SystemExit("No C++ compiler available")
with tempfile.TemporaryDirectory() as d:
    cpp = Path(d) / "check.cpp"
    cpp.write_text(program, encoding="utf-8")
    for compiler in compilers:
        exe = Path(d) / compiler
        subprocess.run([
            compiler, "-std=c++20", "-O1", "-g", "-Wall", "-Wextra", "-Werror",
            "-fsanitize=address,undefined", "-fno-omit-frame-pointer",
            str(cpp), "-o", str(exe),
        ], check=True)
        subprocess.run([str(exe)], check=True)
        print(f"{compiler}: PASS (1024 stale, recovery, mixed active, case, idempotence)")
print("PASS: all three unload/update call sites present")
