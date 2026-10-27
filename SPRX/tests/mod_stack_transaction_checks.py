#!/usr/bin/env python3
"""Exercise the production AddModZones rejection/commit paths on a host.

This isolates the zone planner boundary; it does not simulate the BO3 engine.
"""
from pathlib import Path
import os
import subprocess
import tempfile
import sys

source = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[1] / "t7_maps.cpp"
text = source.read_text(encoding="utf-8")
begin = text.index("static int AddModZones(")
end = text.index("static bool FreesModZones(", begin)
function = text[begin:end]
append_begin = text.index("static void AppendMods(")
append_end = text.index("static void ApplyModsBeforeLevel(", append_begin)
append_function = text[append_begin:append_end]

fixture = r'''
#include <atomic>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>

constexpr int kNameMax = 56;
constexpr int kMaxStack = 24;
constexpr int kModZoneKinds = 4;
constexpr int kExpandedZones = 32;
constexpr int kMaxZoneCall = 64;
constexpr int kModeCommonFlags = 0x20;
static const char* const k_modeCommons[kModZoneKinds] = {"", "zm_common", "mp_common", "cp_common"};
struct ZoneInfo {
    const char* name;
    int32_t allocFlags, freeFlags, allocSlot, freeSlot;
    uint64_t buffer, bufferSize;
};
struct StackMod { char name[kNameMax]; int fingerprint; };
static StackMod g_stack[kMaxStack];
static int g_stackCount = 0;
static char g_loaded[kMaxStack][kNameMax];
static int g_loadedCount = 0;
static int g_chosenCount = 1;
static std::atomic<bool> g_applyWanted{false};
static std::atomic<bool> g_modsLuaDirty{false};
static int recordsFree = 8;
static int plannedZones = 0;
static int lastPlannedFrom = 0;
static int claimResets = 0;
static int writes = 0;
static bool failSnapshot = false;
[[maybe_unused]] static void* TestMalloc(size_t bytes) { return failSnapshot ? nullptr : std::malloc(bytes); }

static bool EqualsNoCase(const char* a, const char* b) { return std::strcmp(a, b) == 0; }
static bool IsModZoneName(const char*) { return false; }
static bool ModHooksReady() { return true; }
static int ZoneRecordsFree() { return recordsFree; }
static void T7Log_Write(const char*, ...) {}
static int ChosenForLoad(char (*names)[kNameMax]) {
    std::snprintf(names[0], kNameMax, "newmod");
    return 1;
}
static void BeginListWrite() { ++writes; }
static void EndListWrite() { --writes; }
static void SetStack(const char (*names)[kNameMax], int count) {
    g_stackCount = count;
    for (int i = 0; i < count; ++i) {
        std::memcpy(g_stack[i].name, names[i], kNameMax);
        g_stack[i].fingerprint = 99;
    }
}
static int StackEntries(int from, int, ZoneInfo* out, int, int*, int* through) {
    if (plannedZones > 0 && from <= lastPlannedFrom) {
        out[0] = {"core_mod", 0, 0, 0, 0, 0, 0};
        *through = from + 1;
        return plannedZones;
    }
    return 0;
}
static void ResetScriptClaims() { ++claimResets; }
[[maybe_unused]] static void PruneUnloadedModUiFiles() {}
static void SetLoaded(int count) {
    g_loadedCount = count;
    for (int i = 0; i < count; ++i)
        std::snprintf(g_loaded[i], kNameMax, "%s", g_stack[i].name);
}

#define malloc TestMalloc
PRODUCTION
static void WaitForModZones(const ZoneInfo*, int, int) {}
static uint64_t LoadXAssets_h(uint64_t, uint64_t, uint64_t, uint64_t, uint64_t, uint64_t,
                              double, double, double, double, double, double, double, double) { return 0; }
APPEND
#undef malloc

static void ResetFixture(bool previouslyLoaded) {
    std::memset(g_stack, 0, sizeof(g_stack));
    std::memset(g_loaded, 0, sizeof(g_loaded));
    g_stackCount = previouslyLoaded ? 1 : 0;
    g_loadedCount = previouslyLoaded ? 1 : 0;
    if (previouslyLoaded) {
        std::snprintf(g_stack[0].name, kNameMax, "oldmod");
        g_stack[0].fingerprint = 12345;
        std::snprintf(g_loaded[0], kNameMax, "oldmod");
    }
    g_applyWanted.store(false);
    recordsFree = 8;
    plannedZones = 0;
    lastPlannedFrom = 0;
    claimResets = 0;
    writes = 0;
    failSnapshot = false;
}

static int Rejection(const char* scenario, bool active, int records, bool outOfMemory = false) {
    ResetFixture(active);
    recordsFree = records;
    failSnapshot = outOfMemory;
    const StackMod before[kMaxStack] = {g_stack[0]};
    const int count = g_stackCount;
    ZoneInfo common = {"zm_common", kModeCommonFlags, 0, 0, 0, 0, 0};
    ZoneInfo output[kMaxZoneCall]{};
    const int added = AddModZones(&common, 1, output);
    if (added != 0 || g_stackCount != count ||
        (active && (std::strcmp(g_stack[0].name, before[0].name) ||
                    g_stack[0].fingerprint != before[0].fingerprint)) ||
        g_loadedCount != (active ? 1 : 0) ||
        (active && std::strcmp(g_loaded[0], "oldmod")) || claimResets || writes || g_applyWanted.load()) {
        std::fprintf(stderr, "FAIL: rejected %s changed live mod stack or claims\n", scenario);
        return 1;
    }
    std::printf("PASS: %s leaves active stack unchanged\n", scenario);
    return 0;
}

int main() {
    // Valid zone-table capacity, but the selected mod has no zone in this mode.
    if (Rejection("zero-zone request", true, 8)) return 1;
    // One free record cannot fit a required two-record companion zone.
    if (Rejection("insufficient companion slots", true, 3)) return 2;
    if (Rejection("no available zone records", true, 2)) return 3;
    if (Rejection("first load with zero zones", false, 8)) return 4;
    if (Rejection("backup allocation failure", true, 8, true)) return 6;

    // The append path must not advertise an unloaded second mod's zone files.
    ResetFixture(true);
    char appendNames[2][kNameMax] = {"oldmod", "newmod"};
    AppendMods(appendNames, 2, 1);
    if (g_stackCount != 1 || std::strcmp(g_stack[0].name, "oldmod") ||
        g_stack[0].fingerprint != 12345 || g_loadedCount != 1 || claimResets)
        return 7;
    std::puts("PASS: rejected append cannot expose unloaded mod files");

    ResetFixture(true);
    plannedZones = 1;
    lastPlannedFrom = 1;
    char three[3][kNameMax] = {"oldmod", "secondmod", "thirdmod"};
    AppendMods(three, 3, 1);
    if (g_stackCount != 2 || g_loadedCount != 2 ||
        std::strcmp(g_loaded[1], "secondmod")) return 8;
    std::puts("PASS: partial append hides the unplanned tail");

    ResetFixture(true);
    plannedZones = 1;
    ZoneInfo common = {"zm_common", kModeCommonFlags, 0, 0, 0, 0, 0};
    ZoneInfo output[kMaxZoneCall]{};
    if (AddModZones(&common, 1, output) != 1 || g_stackCount != 1 ||
        std::strcmp(g_stack[0].name, "newmod") || g_stack[0].fingerprint != 99 ||
        std::strcmp(g_loaded[0], "newmod") || claimResets != 1 || writes)
        return 5;
    std::puts("PASS: successful plan commits the new stack and resets claims");
}
'''.replace("PRODUCTION", function).replace("APPEND", append_function)

with tempfile.TemporaryDirectory(prefix="bo3-mod-stack-") as directory:
    cpp = Path(directory) / "transaction.cpp"
    exe = Path(directory) / "transaction"
    cpp.write_text(fixture, encoding="utf-8")
    compiler = os.environ.get("CXX", "g++")
    subprocess.run([compiler, "-std=c++20", "-Wall", "-Wextra", "-Werror", "-O1",
                    "-fsanitize=address,undefined", "-fno-omit-frame-pointer",
                    str(cpp), "-o", str(exe)], check=True)
    subprocess.run([str(exe)], check=True)
