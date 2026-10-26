#!/usr/bin/env python3
"""Exercise ReloadFrontend decision/state paths from the production source (#4)."""
from pathlib import Path
import os
import subprocess
import tempfile

source = (Path(__file__).resolve().parents[1] / "t7_maps.cpp").read_text(encoding="utf-8")
start = source.index("static void ReloadFrontend(const char* why)")
end = source.index("static uint64_t PlayMovie_h(", start)
function = source[start:end]

fixture = r'''
#include <cstdint>
#include <cstdio>
#include <cstdlib>

static bool g_inLevel = false;
static bool g_frontendReloadReady = false;
static bool g_quietReload = false;
static int g_unshownCount = 0;
static int g_loadedCount = 2;
static int g_scriptClaimCount = 4;
static void* g_playMovieOriginal = nullptr;
static int calls = 0;
static bool observedQuiet = false;
static bool reenterOnce = false;
static bool outerRemainsQuiet = false;
static void ReloadFrontend(const char* why);
static void MockKillServer(uint64_t mode) {
    if (mode != 0) std::abort();
    ++calls;
    observedQuiet = g_quietReload;
    if (reenterOnce) {
        reenterOnce = false;
        ReloadFrontend("nested KillServer callback");
        outerRemainsQuiet = g_quietReload;
    }
}
static uintptr_t g_base = reinterpret_cast<uintptr_t>(&MockKillServer);
constexpr uintptr_t kKillServer = 0;
static void T7Log_Write(const char*, ...) {}

PRODUCTION

int main() {
    g_unshownCount = 3;
    ReloadFrontend("not ready");
    if (calls || g_unshownCount != 3) return 1;
    g_frontendReloadReady = true;
    g_inLevel = true;
    ReloadFrontend("inside level");
    if (calls || g_unshownCount != 3) return 2;
    g_inLevel = false;

    // With no movie hook installed, reloading remains functional and quiet
    // mode is never accidentally enabled.
    ReloadFrontend("normal");
    if (calls != 1 || observedQuiet || g_quietReload || g_unshownCount) return 3;

    g_playMovieOriginal = reinterpret_cast<void*>(1);
    for (int i = 0; i < 100; ++i) {
        g_unshownCount = 3;
        ReloadFrontend("repeated menu selection");
        if (calls != i + 2 || !observedQuiet || g_quietReload || g_unshownCount)
            return 4;
    }
    if (g_loadedCount != 2 || g_scriptClaimCount != 4) return 5;
    // A synchronous KillServer callback can cause a nested reload. The inner
    // reload must not disable the outer one's movie suppression prematurely.
    reenterOnce = true;
    ReloadFrontend("outer callback");
    if (!outerRemainsQuiet || g_quietReload || calls != 103) return 6;
    std::puts("PASS: guarded, repeated frontend reloads clear transient UI state and retain mod metadata");
    return 0;
}
'''.replace("PRODUCTION", function)

with tempfile.TemporaryDirectory(prefix="bo3-frontend-reload-") as directory:
    path = Path(directory) / "reload.cpp"
    exe = Path(directory) / "reload"
    path.write_text(fixture, encoding="utf-8")
    subprocess.run([os.environ.get("CXX", "g++"), "-std=c++20", "-Wall", "-Wextra", "-Werror",
                    "-fsanitize=address,undefined", str(path), "-o", str(exe)], check=True)
    subprocess.run([str(exe)], check=True)
