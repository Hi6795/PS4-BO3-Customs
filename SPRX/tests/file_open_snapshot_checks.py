#!/usr/bin/env python3
"""Compile the production FileOpen_h against a deterministic refresh race fixture.

No PS4 SDK or retail PC game files are required. Requires a C++20 host compiler.
"""
import os
from pathlib import Path
import subprocess
import tempfile

SOURCE = Path(__file__).resolve().parents[1] / "t7_maps.cpp"
source = SOURCE.read_text(encoding="utf-8")
start = source.index("static uint64_t FileOpen_h(")
end = source.index("\nstatic int MapPackage(", start)
function = source[start:end]

harness = r'''
#include <cstdio>
#include <cstdint>
#include <cstring>
#include <cstdlib>
#include <string>

struct File { char rel[128]; char path[224]; int package; };
using Hook_t = uint64_t (*)(uint64_t, uint64_t, uint64_t, uint64_t, uint64_t, uint64_t,
                            double, double, double, double, double, double, double, double);
struct ListReader { ListReader() {} ~ListReader() {} };
static File g_files[2];
static File g_stackFile;
static int g_fileCount = 2;
static uint64_t g_openOriginal = 0;
static const char* g_expected = nullptr;
static bool g_mutate = false;
static const char* Relative(const char* path) {
    const char prefix[] = "/app0/zone/";
    return std::strncmp(path, prefix, sizeof(prefix) - 1) == 0 ? path + sizeof(prefix) - 1 : nullptr;
}
static const File* StackFile(const char* rel) {
    return std::strcmp(rel, "mod.ff") == 0 ? &g_stackFile : nullptr;
}
static int LanguageCompanion(const char* rel) {
    return std::strcmp(rel, "fr_zm_map.ff") == 0 ? 1 : -1;
}
static uint64_t FakeOpen(uint64_t a1, uint64_t, uint64_t, uint64_t, uint64_t, uint64_t,
                         double, double, double, double, double, double, double, double) {
    // Simulates a refresh immediately after FileOpen_h's reader guard exits.
    if (g_mutate) {
        std::strcpy(g_files[0].path, "/data/CHANGED-REGISTRY.ff");
        std::strcpy(g_files[1].path, "/data/CHANGED-LANGUAGE.ff");
        std::strcpy(g_stackFile.path, "/data/CHANGED-STACK.ff");
    }
    return std::strcmp(reinterpret_cast<const char*>(a1), g_expected) == 0 ? 1 : 0;
}
'''

main = r'''
static void Reset() {
    std::strcpy(g_files[0].rel, "zm_test.ff");
    std::strcpy(g_files[0].path, "/data/maps/zm_test.ff");
    std::strcpy(g_files[1].rel, "en_zm_map.ff");
    std::strcpy(g_files[1].path, "/data/maps/en_zm_map.ff");
    std::strcpy(g_stackFile.path, "/data/mods/mod.ff");
    g_openOriginal = reinterpret_cast<uint64_t>(&FakeOpen);
    g_mutate = true;
}
static void Check(const char* name, const char* input, const char* expected, bool mutate) {
    Reset();
    g_expected = expected;
    g_mutate = mutate;
    if (FileOpen_h(reinterpret_cast<uint64_t>(input), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0) != 1) {
        std::fprintf(stderr, "FAIL: %s (source path mutated during original call)\n", name);
        std::exit(1);
    }
    std::printf("PASS: %s\n", name);
}
int main() {
    Check("registered map survives refresh", "/app0/zone/zm_test.ff", "/data/maps/zm_test.ff", true);
    Check("active mod survives refresh", "/app0/zone/mod.ff", "/data/mods/mod.ff", true);
    Check("language companion survives refresh", "/app0/zone/fr_zm_map.ff", "/data/maps/en_zm_map.ff", true);
    Check("unmatched file preserves original", "/app0/zone/other.ff", "/app0/zone/other.ff", true);
    Check("outside-zone path preserves original", "/app0/other.ff", "/app0/other.ff", true);
    Check("registered map without refresh", "/app0/zone/zm_test.ff", "/data/maps/zm_test.ff", false);
    return 0;
}
'''

with tempfile.TemporaryDirectory(prefix="bo3-file-open-") as tmp:
    cpp = Path(tmp) / "file_open.cpp"
    exe = Path(tmp) / "file_open"
    cpp.write_text(harness + "\n" + function + "\n" + main, encoding="utf-8")
    compiler = os.environ.get("CXX", "clang++")
    command = [compiler, "-std=c++20", "-O1", "-g", "-Wall", "-Wextra", "-Werror",
               "-fsanitize=address,undefined", "-fno-omit-frame-pointer", str(cpp), "-o", str(exe)]
    subprocess.run(command, check=True)
    subprocess.run([str(exe)], check=True)
