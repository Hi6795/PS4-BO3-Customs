#!/usr/bin/env python3
"""Exercise actual linker/priority hook bodies with a controlled caller address.

This checks host concurrency and passthrough behavior, not PS4 native layouts.
"""
from pathlib import Path
import subprocess
import tempfile
import sys
import re

ROOT = Path(__file__).resolve().parents[1]


def function(source, name):
    start = source.index("static uint64_t " + name + "(")
    brace = source.index("{", start)
    depth = 1
    end = brace + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


def main():
    source_path = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "t7_maps.cpp"
    source = source_path.read_text()
    fixed = "g_scriptLinkers.Current" in source
    hooks = function(source, "LinkXAsset_h") + "\n" + function(source, "ZonePriority_h")
    address = "(uintptr_t)__builtin_return_address(0) - g_base"
    assert address in hooks
    # The host cannot call from PS4 text addresses; replace only this probe.
    hooks = hooks.replace(address, "g_testCaller")
    state = "static ModLoadContexts<> g_scriptLinkers;" if fixed else "static std::atomic<uint64_t> g_scriptLinker{0};"
    prelude = r'''
#include "mod_load_context.hpp"
#include <atomic>
#include <cassert>
#include <condition_variable>
#include <cstdint>
#include <mutex>
#include <thread>
#include <iostream>
using Hook_t = uint64_t (*)(uint64_t,uint64_t,uint64_t,uint64_t,uint64_t,uint64_t,
                            double,double,double,double,double,double,double,double);
static thread_local uint64_t threadId = 0;
static thread_local uintptr_t g_testCaller = 0x857A11;
static uint64_t scePthreadSelf() { return threadId; }
static uintptr_t g_base = 0;
static bool g_buildMatched = false;
static int g_stackCount = 0;
static std::atomic<bool> g_usermapLevel{true};
static void GrowPool(int) {}
static void T7Log_PoolOut(int) {}
static void RenameStackAsset(uint64_t) {}
constexpr int32_t kScriptParseTree = 54;
constexpr uint64_t kModMarker = 0x80000000;
constexpr uint64_t kModRankShift = 29;
constexpr uint64_t kModRankMax = 3;
constexpr uint64_t kLocalizedZone = 0x8000000;
constexpr uint64_t kModScriptPriority = 1210;
constexpr uint64_t kModPriority = 1150;
constexpr uint64_t kUsermapScriptPriority = 1200;
constexpr uint32_t kUsermapZoneFlags = 0x100;
constexpr uintptr_t kLinkXAsset = 0x857A10;
constexpr uintptr_t kLinkXAssetEnd = 0x858234;
static void* g_linkOriginal = nullptr;
static void* g_priorityOriginal = nullptr;
'''
    cases = r'''
static uint64_t priority(uint64_t flags) {
    return ZonePriority_h(flags,2,3,4,5,6,1,2,3,4,5,6,7,8);
}
static uint64_t nativePriority(uint64_t,uint64_t a2,uint64_t a3,uint64_t a4,uint64_t a5,uint64_t a6,
                               double x0,double x1,double x2,double x3,double x4,double x5,double x6,double x7) {
    assert(a2==2 && a3==3 && a4==4 && a5==5 && a6==6);
    assert(x0==1 && x1==2 && x2==3 && x3==4 && x4==5 && x5==6 && x6==7 && x7==8);
    return 99;
}
static std::mutex gateMutex;
static std::condition_variable gate;
static int entered = 0;
static uint64_t results[3]{};
static bool concurrent = false;
static thread_local int nesting = 0;
static uint64_t nativeLink(uint64_t asset,uint64_t a2,uint64_t a3,uint64_t a4,uint64_t a5,uint64_t a6,
                           double x0,double x1,double x2,double x3,double x4,double x5,double x6,double x7) {
    assert(asset && a2==2 && a3==3 && a4==4 && a5==5 && a6==6);
    assert(x0==1 && x1==2 && x2==3 && x3==4 && x4==5 && x5==6 && x6==7 && x7==8);
    if(concurrent) {
        std::unique_lock<std::mutex> lock(gateMutex);
        ++entered;
        gate.notify_all();
        gate.wait(lock, []{return entered==2;});
        results[threadId] = priority(kModMarker);
        // Keep both contexts alive until both priority decisions are made.
        ++nesting;
        gate.notify_all();
        static int measured = 0;
        ++measured;
        gate.wait(lock, []{return measured==2;});
    } else if(nesting==0) {
        ++nesting;
        int32_t script = kScriptParseTree;
        assert(LinkXAsset_h((uint64_t)&script,2,3,4,5,6,1,2,3,4,5,6,7,8)==0x12345678);
        --nesting;
        assert(priority(kModMarker)==1217);
        assert(priority(kModMarker | (2u<<29))==1213);
        assert(priority(kModMarker | kLocalizedZone)==1216);
        assert(priority(kUsermapZoneFlags)==1200);
        assert(priority(0)==99);
        g_testCaller = kLinkXAssetEnd + 1;
        assert(priority(kModMarker)==1157);
        g_testCaller = kLinkXAsset + 1;
    } else {
        assert(priority(kModMarker)==1217);
    }
    return 0x12345678;
}
int main() {
    g_linkOriginal = (void*)nativeLink;
    g_priorityOriginal = (void*)nativePriority;
    threadId = 1;
    assert(priority(kModMarker)==1157);
    assert(priority(kUsermapZoneFlags)==99);
    int32_t script = kScriptParseTree;
    assert(LinkXAsset_h((uint64_t)&script,2,3,4,5,6,1,2,3,4,5,6,7,8)==0x12345678);
    assert(priority(kModMarker)==1157);
    concurrent = true;
    auto run = [&](uint64_t id) {
        threadId=id;
        LinkXAsset_h((uint64_t)&script,2,3,4,5,6,1,2,3,4,5,6,7,8);
        assert(priority(kModMarker)==1157);
    };
    std::thread first(run,1), second(run,2);
    first.join(); second.join();
    if(EXPECT_FIXED) {
        assert(results[1]==1217 && results[2]==1217);
        std::cout << "PASS: concurrent/nested script priorities, cleanup, rank/localization and native passthrough\n";
    } else {
        assert((results[1]==1157 && results[2]==1217) || (results[2]==1157 && results[1]==1217));
        std::cout << "REPRODUCED: one concurrent script receives ordinary-asset priority1157 instead of1217\n";
    }
}
'''
    for name in ("kScriptParseTree", "kModMarker", "kModRankShift", "kModRankMax", "kLocalizedZone",
                 "kModScriptPriority", "kModPriority", "kUsermapScriptPriority", "kUsermapZoneFlags",
                 "kLinkXAsset", "kLinkXAssetEnd"):
        declaration = re.search(r"constexpr\s+\w+\s+" + name + r"\s*=.*?;", source)
        assert declaration, name
        prelude, count = re.subn(r"constexpr\s+\w+\s+" + name + r"\s*=.*?;", declaration[0], prelude)
        assert count == 1, name
    with tempfile.TemporaryDirectory(prefix="priority-check-", dir=ROOT) as directory:
        path = Path(directory)
        test = path / "check.cpp"
        test.write_text(prelude + state + "\n" + hooks + cases)
        binary = path / "check"
        subprocess.run(["g++", "-std=c++11", "-pthread", "-O0", "-I", str(ROOT),
                        "-DEXPECT_FIXED=" + str(int(fixed)), str(test), "-o", str(binary)], check=True)
        subprocess.run([str(binary)], check=True, timeout=15)


if __name__ == "__main__":
    main()
