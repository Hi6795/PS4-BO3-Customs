#!/usr/bin/env python3
"""Guard against appending a mod before earlier tracked zones are ready.

The engine's global loading flag can be false between asynchronous zone
transitions, even when a prior mod's planned zones are not ready yet.
"""
from pathlib import Path
import os
import subprocess
import tempfile


source = (Path(__file__).resolve().parents[1] / "t7_maps.cpp").read_text(encoding="utf-8")
begin = source.index("static bool TrackedModZonesPending()")
end = source.index("static void TraceModWait(", begin)
production = source[begin:end]

for name, flag in (
    ("PrefetchMods", "g_prefetchWanted"),
    ("ApplyModsNow", "g_applyWanted"),
):
    start = source.index(f"static void {name}()")
    stop = source.index(f"static ", start + 1)
    body = source[start:stop]
    expected = f"if (engineBusy || trackedPending)\n    {{\n        {flag}.store(true);"
    assert expected in body, f"{name} must defer and retry when a tracked zone is pending"
    assert "const bool engineBusy = AnyZoneLoading();" in body
    assert "const bool trackedPending = TrackedModZonesPending();" in body


fixture = r'''
#include <cstdio>
#include <cstring>

constexpr int kNameMax = 56;
static bool g_reloadCheck = false;
static int g_waitZoneCount = 0;
static char g_waitZones[64][kNameMax]{};
static int first = 0;
static int second = 0;

static int ZoneStateOf(const char* name) {
    if (std::strcmp(name, "core_mod") == 0) return first;
    if (std::strcmp(name, "zm_mod") == 0) return second;
    return 0;
}

PRODUCTION

static int Check(bool expected, const char* scenario) {
    if (TrackedModZonesPending() != expected) {
        std::fprintf(stderr, "FAIL: %s\n", scenario);
        return 1;
    }
    std::printf("PASS: %s\n", scenario);
    return 0;
}

int main() {
    std::snprintf(g_waitZones[0], kNameMax, "core_mod");
    std::snprintf(g_waitZones[1], kNameMax, "zm_mod");
    if (Check(false, "no tracked zones")) return 1;
    g_waitZoneCount = 2;
    if (Check(false, "not waiting for zones")) return 2;
    g_reloadCheck = true;
    if (Check(true, "planned zones not registered yet")) return 3;
    first = 1;
    if (Check(true, "zone registered and loading")) return 4;
    first = 2;
    if (Check(true, "first complete, second not registered")) return 5;
    second = 1;
    if (Check(true, "first complete, second loading")) return 6;
    second = 2;
    if (Check(false, "all tracked zones ready")) return 7;
    first = 3;
    second = 3;
    if (Check(false, "higher ready states")) return 8;
    g_reloadCheck = false;
    first = 0;
    if (Check(false, "expired tracking does not block new loads")) return 9;
    return 0;
}
'''.replace("PRODUCTION", production)

with tempfile.TemporaryDirectory(prefix="bo3-mod-readiness-") as directory:
    cpp = Path(directory) / "readiness.cpp"
    binary = Path(directory) / "readiness"
    cpp.write_text(fixture, encoding="utf-8")
    subprocess.run([os.environ.get("CXX", "g++"), "-std=c++20", "-Wall", "-Wextra", "-Werror",
                    "-fsanitize=address,undefined", str(cpp), "-o", str(binary)], check=True)
    subprocess.run([str(binary)], check=True)
