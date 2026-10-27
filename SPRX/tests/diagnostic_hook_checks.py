"""Execute the actual error-hook body with host stubs; never claim engine validation."""
import os
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
LOG = (ROOT / "t7_log.cpp").read_text()
HOOK_END = LOG.index("static bool CopyImportName(") if "static bool CopyImportName(" in LOG else LOG.index("static uint64_t ScriptImport(")
HOOK = LOG[LOG.index("static const char* ScriptVmName("):HOOK_END]
PREFIX = r'''
#include "script_error_budget.hpp"
#include <cassert>
#include <atomic>
#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>
using Passthrough_t = uint64_t (*)(uint64_t,uint64_t,uint64_t,uint64_t,uint64_t,uint64_t,
                                   double,double,double,double,double,double,double,double);
static unsigned nativeCalls = 0, resolutions = 0;
static uint64_t expected[6];
static uint64_t Native(uint64_t a,uint64_t b,uint64_t c,uint64_t d,uint64_t e,uint64_t f,
                      double x0,double x1,double x2,double x3,double x4,double x5,double x6,double x7)
{
    const uint64_t got[] = {a,b,c,d,e,f};
    for (unsigned i=0;i<6;++i) assert(got[i] == expected[i]);
    const double xs[] = {x0,x1,x2,x3,x4,x5,x6,x7};
    for (unsigned i=0;i<8;++i) assert(xs[i] == 0.25+i);
    ++nativeCalls;
    return 0xDEADC0DE;
}
static void* g_scriptErrorOriginal = (void*)Native;
static uint32_t g_scriptErrors = 0;
static T7Diagnostics::ScriptErrorBudget g_scriptBudget;
static int T7Maps_ScriptOwner(const char*) { return 1; }
void T7Maps_TraceDiagnostics(const char*) {}
alignas(uintptr_t) static unsigned char vmMemory[512] = {};
static uintptr_t g_base = (uintptr_t)vmMemory;
constexpr uintptr_t kScrVmPub = 0;
static std::vector<std::string> lines;
static bool RangeReadable(uintptr_t p, size_t) { return p != 0; }
static const char* ScriptAt(uintptr_t p, uint32_t* offset)
{ ++resolutions; *offset = (uint32_t)p; return "scripts/zm/test.gsc"; }
static void T7Log_Write(const char* format, ...)
{
    char text[2048]; va_list args; va_start(args,format);
    vsnprintf(text,sizeof(text),format,args); va_end(args); lines.emplace_back(text);
}
'''
SUFFIX = r'''
static void Invoke(uint64_t vm, uintptr_t pos, const char* dialog)
{
    expected[0]=vm; expected[1]=pos; expected[2]=0x3333;
    expected[3]=(uintptr_t)dialog; expected[4]=0x5555; expected[5]=0x6666;
    const auto value=ScriptError(expected[0],expected[1],expected[2],expected[3],expected[4],expected[5],
                                0.25,1.25,2.25,3.25,4.25,5.25,6.25,7.25);
    assert(value == 0xDEADC0DE);
}
int main()
{
    static char message[192]="undefined is not a field object";
    static char dialog[192]="test dialog";
    *(uintptr_t*)(vmMemory+0x10)=(uintptr_t)message;
    *(uintptr_t*)(vmMemory+0x80)=(uintptr_t)message;
    for (unsigned n=0;n<10000;++n) Invoke(0,0x1234,dialog);
    assert(nativeCalls==10000 && resolutions==14 && lines.size()==14);
    Invoke(0,0x5678,dialog);
    assert(lines.back().find("+0x5678")!=std::string::npos);
    assert(lines.back().find("owner=mod-slot-2")!=std::string::npos);
    Invoke(1,0x1234,dialog);
    assert(lines.back().find("client (csc)")!=std::string::npos);
    for (unsigned n=0;n<100;++n) Invoke(0,0x10000+n,dialog);
    Invoke(1,0x7777,dialog);
    assert(lines.back().find("+0x7777")!=std::string::npos);
    Invoke(99,0x9999,nullptr);
    assert(lines.back().find("unknown VM")!=std::string::npos);
    ScriptErrorSummary("host check");
    assert(lines.back().find("summary (host check)")!=std::string::npos);
    assert(nativeCalls==10104 && g_scriptErrors==10104);
    g_scriptBudget.NewLevel();
    Invoke(0,0x1234,dialog);
    assert(lines.back().find("occurrence 1")!=std::string::npos);
    puts("PASS: actual ScriptError forwards every argument and result once, including flood/overflow/unknown VM");
}
'''

TRANSITION_PREFIX = r'''
#include <atomic>
#include <cassert>
#include <cstdarg>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>
#define _DEBUG
static std::vector<std::string> lines;
static void T7Log_Write(const char* format, ...)
{
    char text[2048]; va_list args; va_start(args,format);
    vsnprintf(text,sizeof(text),format,args); va_end(args); lines.emplace_back(text);
}
namespace T7Lua {
alignas(uintptr_t) static unsigned char memory[128];
static uintptr_t g_base=(uintptr_t)memory, g_pcUtilState=0, g_mouseState=0;
static bool g_forceInject=false, allowLock=true;
static unsigned locks=0, unlocks=0;
constexpr uintptr_t kLuaState=0;
static bool UiTryLock() { if (!allowLock) return false; ++locks; return true; }
static void UiUnlock() { ++unlocks; }
static bool UiUp() { return *(uintptr_t*)g_base != 0; }
}
'''

TRANSITION_SUFFIX = r'''
constexpr int kMaxStack=24, kNameMax=192, kModZoneKinds=4;
static const char* const k_modZones[]={"core_mod","zm_mod","mp_mod","cp_mod"};
static const char* const k_modeCommons[]={"","zm_common","mp_common","cp_common"};
static int g_loadedCount=1, g_waitZoneCount=0;
static bool g_reloadCheck=false;
alignas(uintptr_t) static unsigned char mapsMemory[64] = {};
static uintptr_t g_base=(uintptr_t)mapsMemory;
constexpr uintptr_t kSessionModes=0;
static std::atomic<bool> g_applyWanted{false};
static char g_loaded[kMaxStack][kNameMax]={"zm8p"};
static int LoadedModeCommon() { return 1; }
static int SessionModeCommon() { return 2; }
static bool ModeCommonOnTop() { return false; }
static bool ZoneLoaded(const char* z) { return std::strcmp(z,"zm_mod")==0; }
static int ChosenForLoad(char (*names)[kNameMax]) { std::strcpy(names[0],"zm8p"); return 1; }
'''

TRANSITION_MAIN = r'''
using Hook_t=uint64_t(*)(uint64_t,uint64_t,uint64_t,uint64_t,uint64_t,uint64_t,
                       double,double,double,double,double,double,double,double);
static unsigned originals=0, applies=0;
static uint64_t Original(uint64_t a1,uint64_t a2,uint64_t a3,uint64_t a4,uint64_t a5,uint64_t a6,
                         double x0,double x1,double x2,double x3,double x4,double x5,double x6,double x7)
{
    assert(a1 && a2==2 && a3==3 && a4==4 && a5==5 && a6==6);
    assert(x0==0 && x1==1 && x2==2 && x3==3 && x4==4 && x5==5 && x6==6 && x7==7);
    ++originals; return 999;
}
static void* g_levelZonesOriginal=(void*)Original;
static bool RangeReadable(uintptr_t p,size_t) { return p!=0; }
static bool EqualsNoCase(const char* a,const char* b) { return std::strcmp(a,b)==0; }
static void ApplyModsBeforeLevel(const char*) { ++applies; }
'''

TRANSITION_END = r'''
int main()
{
    T7Lua::allowLock=false;
    T7Lua_TraceState("busy test");
    assert(T7Lua::locks==0 && T7Lua::unlocks==0);
    T7Lua::allowLock=true;
    *(uintptr_t*)T7Lua::g_base=0x1234;
    T7Lua_TraceState("ready test");
    assert(lines.back().find("observed-generation=1")!=std::string::npos);
    T7Lua_TraceState("same state");
    assert(lines.back().find("observed-generation=1")!=std::string::npos);
    *(uintptr_t*)T7Lua::g_base=0;
    T7Lua_TraceState("destroyed state");
    assert(lines.back().find("observed-generation=2")!=std::string::npos);
    char level[192]="zm_prototype";
    assert(LoadLevelZones_h((uintptr_t)level,2,3,4,5,6,0,1,2,3,4,5,6,7)==999);
    assert(originals==1 && applies==1);
    std::strcpy(level,"core_frontend");
    assert(LoadLevelZones_h((uintptr_t)level,2,3,4,5,6,0,1,2,3,4,5,6,7)==999);
    assert(originals==2 && applies==1);
    assert(T7Lua::locks==T7Lua::unlocks);
    bool found=false;
    for (const auto& line:lines)
    {
        if (line.find("session=mp_common bits=0x0 loaded-common=zm_common")!=std::string::npos &&
            line.find("core-mod=0 zm-mod=1 mp-mod=0 cp-mod=0")!=std::string::npos)
            found=true;
    }
    assert(found);
    found=false;
    for (const auto& line:lines)
        if (line.find("native level-zone result for zm_prototype: 0x3E7")!=std::string::npos)
            found=true;
    assert(found);
    puts("PASS: actual transition snapshots, UI lock balance, unchanged frontend/map dispatch and native result");
}
'''



OWNER_PREFIX = r"""
#include <atomic>
#include <cassert>
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstring>
constexpr int kMaxStack=24, kNameMax=192, kInternedMax=128, kMaxScriptClaims=32;
struct ScriptClaim { char base[kInternedMax]; int owner; };
static std::atomic<bool> g_claimLock{false};
static ScriptClaim g_scriptClaims[kMaxScriptClaims];
static int g_scriptClaimCount=0;
static bool RangeReadable(uintptr_t p, size_t) { return p != 0; }
static bool EqualsNoCase(const char* a, const char* b) { return std::strcmp(a, b) == 0; }
"""
OWNER_MAIN = r"""
int main()
{
    std::snprintf(g_scriptClaims[0].base, sizeof(g_scriptClaims[0].base), "%s", "scripts/zm/test");
    g_scriptClaims[0].owner=5;
    g_scriptClaimCount=1;
    assert(ScriptOwner("scripts/zm/test.gsc")==5);
    assert(ScriptOwner("m6_scripts/zm/test.gsc")==5);
    assert(ScriptOwner("scripts/zm/other.gsc")==-1);
    g_claimLock.store(true);
    assert(ScriptOwner("scripts/zm/test.gsc")==-2);
    assert(g_claimLock.load());
    std::puts("PASS: script owner resolves plain/stack-prefixed names and returns without waiting on busy claims");
}
"""

def main():
    assert "kScriptErrorsLogged" not in LOG
    assert 'ScriptErrorSummary("Com_Error")' in LOG
    assert 'ScriptErrorSummary("end of previous level")' in LOG
    assert "g_scriptBudget.NewLevel();" in LOG
    maps = (ROOT / "t7_maps.cpp").read_text()
    assert 'TraceModTransition("zone request", name)' in maps
    assert 'TraceModTransition("before level zones", diagnosticLevel)' in maps
    assert 'TraceModTransition("after level zones", diagnosticLevel)' in maps
    assert 'TraceModTransition("prepared level zones", diagnosticLevel)' in maps
    assert '"core-mod=%d zm-mod=%d mp-mod=%d cp-mod=%d' in maps
    assert 'native level-zone result for %s: 0x%llX' in maps
    assert "static int ScriptOwner(const char* script)" in maps
    assert "void T7Maps_TraceDiagnostics(const char* reason)" in maps
    assert 'T7Maps_TraceDiagnostics("clientfield mismatch")' in LOG
    assert "owner=%s" in LOG
    assert 'TraceModTransition("prepared level zones", diagnosticLevel)' in maps
    assert '"core-mod=%d zm-mod=%d mp-mod=%d cp-mod=%d' in maps
    assert "int T7Maps_ScriptOwner(const char* script)" in maps
    assert "void T7Maps_TraceDiagnostics(const char* reason)" in maps
    assert 'T7Maps_TraceDiagnostics("clientfield mismatch")' in LOG
    assert "owner=%s" in LOG
    lua = (ROOT / "t7_lua.cpp").read_text()
    trace = lua[lua.index("void T7Lua_TraceState("):lua.index("bool T7Lua_IsUiState(")]
    assert "UiTryLock()" in trace and "UiUnlock();" in trace
    assert "utility-matches-current=%d" in trace
    assert "mouse-matches-current=%d" in trace
    assert "utility-matches-current=%d" in trace
    assert "mouse-matches-current=%d" in trace
    assert "RunLua" not in trace and "RunOnTop" not in trace
    with tempfile.TemporaryDirectory(prefix="bo3-log-check-") as temp:
        temp = Path(temp)
        source, binary = temp / "hook.cpp", temp / "hook"
        source.write_text(PREFIX + HOOK + SUFFIX)
        subprocess.run([os.environ.get("CXX", "g++"), "-std=c++20", "-Wall", "-Wextra", "-Werror",
                        "-O1", "-pthread", "-I", str(ROOT), str(source), "-o", str(binary)], check=True)
        subprocess.run([str(binary)], check=True)
        start = maps.index("static void TraceModTransition(const char* when, const char* level)\n{")
        transition = maps[start:maps.index("static void ClearAssetsInUse()", start)]
        start = maps.index("static uint64_t LoadLevelZones_h(")
        level_hook = maps[start:maps.index("static uint64_t BlockJob_h(", start)]
        source.write_text(TRANSITION_PREFIX + trace + TRANSITION_SUFFIX + transition + TRANSITION_MAIN
                          + level_hook + TRANSITION_END)
        subprocess.run([os.environ.get("CXX", "g++"), "-std=c++20", "-Wall", "-Wextra", "-Werror",
                        "-O1", "-pthread", str(source), "-o", str(binary)], check=True)
        subprocess.run([str(binary)], check=True)
        maps = (ROOT / "t7_maps.cpp").read_text()
        stack_start = maps.index("static int StackPrefix(")
        stack_end = maps.index("static bool IsModZoneName(", stack_start)
        stack_prefix = maps[stack_start:stack_end]
        base_start = maps.index("static bool ScriptBase(")
        base_end = maps.index("static void ClaimStackScript(", base_start)
        script_base = maps[base_start:base_end]
        owner_start = maps.index("static int ScriptOwner(")
        owner_end = maps.index("static void TraceModTransition(const char* when, const char* level)\n{", owner_start)
        owner = maps[owner_start:owner_end]
        source.write_text(OWNER_PREFIX + stack_prefix + script_base + owner + OWNER_MAIN)
        subprocess.run([os.environ.get("CXX", "g++"), "-std=c++20", "-Wall", "-Wextra", "-Werror",
                        "-O1", "-pthread", str(source), "-o", str(binary)], check=True)
        subprocess.run([str(binary)], check=True)


if __name__ == "__main__":
    main()
