#!/usr/bin/env python3
"""Exercise real diagnostic readers/formatters with mapped host memory, not a GPU emulator."""
from pathlib import Path
import os
import subprocess
import tempfile

root = Path(__file__).resolve().parents[1]
source = (root / "t7_maps.cpp").read_text(encoding="utf-8")
context = source[source.index("struct GpuLevelContext"):source.index("#endif", source.index("struct GpuLevelContext"))]
readers = source[source.index("struct GpuProgressSample"):source.index("static void RaiseThread()")]
count_blobs = source[source.index("static void CountBlobs("):source.index("static void LogBlobs(")]
watch = source[source.index("static void* StallWatch("):source.index("static void* StallMirror(")]
fixture = r'''
#include "diagnostic_ring.hpp"
#include <cassert>
#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <string>
constexpr size_t kNameMax=56,kMaxStack=24;
constexpr uintptr_t kGpuFrameLabel=0,kFrontendData=8,kFrameDataA=128,kFrameDataB=256,kFrameLabelWanted=16;
constexpr uintptr_t kStreamBlobs=512,kPartsRequired=640,kPartsInMemory=648,kMeshRequired=656,kMeshInMemory=660;
constexpr uintptr_t kStreamPending=664,kStreamStarted=668,kStreamDone=669,kSoundLoaded=672;
constexpr int kPartWords=2,kMeshWords=1,kStreamBlobCount=2;
alignas(uintptr_t) static unsigned char memory[2048];
static uintptr_t g_base=(uintptr_t)memory, denied=0;
static std::atomic<uint64_t> g_lastFrameAt{1000000};
static std::atomic<uint32_t> g_comFrames{10},g_linkCount{30};
static bool RangeReadable(uintptr_t address,size_t bytes) {
    return address!=denied && address>=g_base && address-g_base<sizeof(memory) && bytes<=sizeof(memory)-(address-g_base);
}
static std::string output;
static int checkpoints=0,loadStalls=0,engineIdle=0,gameplayStalls=0;
static void ReportLine(const char* format,...) {
    char text[2048]; va_list args; va_start(args,format); vsnprintf(text,sizeof(text),format,args); va_end(args);
    if(std::string(text).find("[GPU] checkpoint:")==0) ++checkpoints;
    if(std::string(text).find("[GPU] load-stall:")==0) ++loadStalls;
    if(std::string(text).find("[GPU] engine-idle:")==0) ++engineIdle;
    output+=text; output+='\n';
}
template<class T> static void Set(size_t offset,T value) { memcpy(memory+offset,&value,sizeof(value)); }
''' + context + count_blobs + readers + r'''
static std::atomic<uint64_t> g_levelStart{0};
static std::atomic<uint32_t> g_levelFrames{0};
static std::atomic<bool> g_usermapLevel{true},g_poolsWanted{false};
static bool g_inLevel=true;
constexpr uint32_t kStallRounds=3,kStallArmFrames=120;
constexpr uint64_t kLoadStallUs=20000000,kStallUs=4000000,kStallGapUs=5000000;
static uint64_t mockNow=0;
static int ticks=0,tickLimit=0,mode=0;
struct Stop {};
static uint64_t sceKernelGetProcessTime() { return mockNow; }
static void sceKernelUsleep(int us) {
    if(++ticks>tickLimit) throw Stop{};
    mockNow+=us;
    if(mode==0 || mode==3 || mode==4) { ++g_comFrames; g_lastFrameAt=mockNow+(mode==4?10:0); }
    if(mode==3 && ticks==140) g_levelStart=mockNow;
}
static void RaiseThread() {}
static void SampleThreads() {}
static void ReportDispatches() {}
static void SampleStalledThreads(uint32_t,uint64_t) {
    ++gameplayStalls; ReportGpuProgress("gameplay-stall",g_levelStart,mockNow);
}
''' + watch + r'''
static void RunWatch(int scenario,int limit) {
    static uint64_t start=10000000;
    start+=100000000;
    mode=scenario; ticks=0; tickLimit=limit; mockNow=start;
    g_levelStart=start; g_comFrames=0; g_linkCount=0; g_lastFrameAt=0;
    g_levelFrames=scenario==1?0:200;
    checkpoints=loadStalls=engineIdle=gameplayStalls=0; output.clear();
    try { StallWatch(nullptr); } catch(const Stop&) {}
}
int main() {
    Set(kGpuFrameLabel,g_base+1024); Set(kFrontendData,g_base+kFrameDataA);
    Set<uint32_t>(1024,7); Set<uint32_t>(kFrameDataB+kFrameLabelWanted,8); Set<uint32_t>(kFrameDataA+kFrameLabelWanted,9);
    Set<uint32_t>(kPartsRequired,11); Set<uint32_t>(kPartsRequired+4,8); Set<uint32_t>(kPartsInMemory,1);
    Set<uint32_t>(kMeshRequired,7); Set<uint32_t>(kMeshInMemory,5);
    Set<int32_t>(kStreamPending,3); Set<uint8_t>(kStreamStarted,1); Set<float>(kSoundLoaded,0.75f);
    Set<uint32_t>(kStreamBlobs+32,1); Set<uint8_t>(kStreamBlobs+37,0);
    Set<uint32_t>(kStreamBlobs+56+32,2); Set<uint8_t>(kStreamBlobs+56+37,4);
    auto sample=ReadGpuProgress();
    assert(sample.frameValid && sample.completed==7 && sample.wanted==8 && sample.building==9);
    assert(sample.resourcesValid && sample.images==4 && sample.missingImages==3 && sample.meshes==3 && sample.missingMeshes==1);
    assert(sample.blobs==2 && sample.missingBlobs==1 && sample.pending==3 && sample.sound==0.75f);
    GpuLevelContext level{}; level.started=1000000; strcpy(level.map,"zm_test"); level.mods=1; strcpy(level.names[0],"test_mod");
    assert(g_gpuLevelContext.TryPush(level));
    unsigned char before[sizeof(memory)]; memcpy(before,memory,sizeof(memory));
    ReportGpuProgress("checkpoint",1000000,2000000);
    assert(output.find("level=zm_test")!=std::string::npos && output.find("test_mod")!=std::string::npos);
    assert(output.find("missing required image parts=3/4")!=std::string::npos && output.find("completion-changed=unknown")!=std::string::npos);
    output.clear(); ReportGpuProgress("gameplay-stall",1000000,6000000);
    assert(output.find("completion-changed=no unchanged=4000 ms")!=std::string::npos);
    assert(memcmp(before,memory,sizeof(memory))==0); // Observation must not alter native values.
    Set<uint32_t>(1024,9); output.clear(); ReportGpuProgress("gameplay-stall",1000000,7000000);
    assert(output.find("completion-changed=yes unchanged=0 ms")!=std::string::npos);
    Set(kFrontendData,uintptr_t(0xDEADBEEF)); assert(!ReadGpuProgress().frameValid);
    Set(kFrontendData,g_base+kFrameDataB); sample=ReadGpuProgress();
    assert(sample.frameValid && sample.wanted==9 && sample.building==8);
    Set(kGpuFrameLabel,uintptr_t(0xDEADBEEF)); assert(!ReadGpuProgress().frameValid);
    Set(kGpuFrameLabel,g_base+1024); denied=g_base+kPartsRequired;
    sample=ReadGpuProgress(); assert(sample.frameValid && !sample.resourcesValid);
    output.clear(); ReportGpuProgress("load-stall",2000000,8000000);
    assert(output.find("resources-readable=0")!=std::string::npos && output.find("context unavailable")!=std::string::npos);
    assert(output.find("completion-changed=unknown")!=std::string::npos);
    RunWatch(0,160); assert(checkpoints>0 && checkpoints<=7 && loadStalls==0 && engineIdle==0 && gameplayStalls==0);
    RunWatch(1,270); assert(checkpoints<=7 && loadStalls==3 && gameplayStalls==0);
    RunWatch(2,270); assert(checkpoints<=7 && engineIdle==3 && loadStalls==0);
    assert(output.find("while a level loads")==std::string::npos);
    RunWatch(3,280); assert(checkpoints>7 && checkpoints<=14 && loadStalls==0 && gameplayStalls==0);
    RunWatch(4,160); assert(gameplayStalls==0 && engineIdle==0); // Frame timestamp published after poll time.
    std::puts("PASS: safe GPU/frame/resource reads, progress/context logs, read-only observation and bounded healthy/loading/idle/new-level watchdog traces");
}
'''
with tempfile.TemporaryDirectory(prefix="bo3-gpu-progress-") as directory:
    cpp = Path(directory) / "fixture.cpp"
    binary = Path(directory) / "fixture"
    cpp.write_text(fixture, encoding="utf-8")
    subprocess.run([os.environ.get("CXX", "g++"), "-std=c++20", "-Wall", "-Wextra", "-Werror",
                    "-fsanitize=address,undefined", "-pthread", "-I", str(root), str(cpp), "-o", str(binary)], check=True)
    subprocess.run([str(binary)], check=True, timeout=30)
