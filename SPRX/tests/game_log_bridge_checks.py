"""Exercise actual dvar drain and tick bodies with host stubs, without BO3."""
from pathlib import Path
import os
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
source = (ROOT / "t7_lua.cpp").read_text()
begin = source.index("static void FlushDvarLog(")
drain = source[begin:source.index("\n#endif", begin)]
begin = source.index("void T7Lua_Tick()")
tick = source[begin:source.index("bool T7Lua_MouseReady()", begin)]
prefix = r'''
#define _DEBUG
#include <cassert>
#include <cstdint>
#include <cstdio>
#include <cstdarg>
#include <cstring>
#include <map>
#include <string>
#include <vector>
static std::vector<std::string> lines;
namespace T7Lua {
static uintptr_t g_base=1;
static unsigned g_ticks=0;
static bool menus=false, luaSuccess=true;
static std::map<std::string,std::string> dvars;
static const char* const k_logFlushLua="UI log drain";
static const char* const k_lobbyTickLua="lobby tick";
static uintptr_t FindDvar(const char* name) { return (uintptr_t)&dvars[name]; }
static const char* DvarText(uintptr_t p) { return ((std::string*)p)->c_str(); }
static void SetDvar(const char* name,const char* value) { dvars[name]=value; }
static bool RunLua(const char* script,const char*)
{
    if(!luaSuccess) return false;
    if(script==k_logFlushLua) dvars["bo3customs_log"]="queued UI message";
    return true;
}
static bool InMenus() { return menus; }
static bool UiTryLock() { return false; }
static bool UiUp() { return false; }
static void UiUnlock() { assert(false); }
static void AddPcUtil() {}
static void ShowShare() {}
static void PollGraphics() {}
static void InjectScripts() {}
static void T7Log_Write(const char* format,...)
{
    char text[2048]; va_list args; va_start(args,format);
    vsnprintf(text,sizeof(text),format,args); va_end(args); lines.emplace_back(text);
}
'''
main = r'''
int main()
{
    using namespace T7Lua;
    dvars["bo3customs_log"]="pending GSC message";
    FlushLuaLog();
    assert(lines.size()==2 && lines[0]=="[MapSelect] pending GSC message");
    assert(lines[1]=="[MapSelect] queued UI message");
    lines.clear(); luaSuccess=false;
    dvars["bo3customs_log"]="message survives Lua error";
    FlushLuaLog();
    assert(lines.size()==1 && lines[0].find("survives Lua error")!=std::string::npos);
    lines.clear();
    dvars["bo3customs_gsc_log"]="[ZM8P-LIFE] in-match event\nsecond line";
    g_ticks=5; menus=false;
    T7Lua_Tick(); // No UI lock or ready state, and not the menu injection cadence.
    assert(lines.size()==2 && lines[0].find("in-match event")!=std::string::npos);
    assert(lines[1]=="[ScriptLog] second line");
    assert(dvars["bo3customs_gsc_log"].empty());
    lines.clear();
    dvars["bo3customs_log"]="legacy in-match event";
    g_ticks=11; T7Lua_Tick();
    assert(lines.size()==1 && lines[0]=="[ScriptLog] legacy in-match event");
    lines.clear(); menus=true;
    dvars["bo3customs_gsc_log"]="dedicated channel in menu";
    dvars["bo3customs_log"]="pending menu event";
    g_ticks=17; T7Lua_Tick();
    assert(lines.size()==1 && lines[0].find("dedicated channel")!=std::string::npos);
    assert(dvars["bo3customs_log"]=="pending menu event");
    puts("PASS: pending message ordering, Lua failure, in-match/no-UI capture and menu routing");
}
'''


def run():
    with tempfile.TemporaryDirectory(prefix="bo3-game-log-") as directory:
        cpp, binary = Path(directory) / "check.cpp", Path(directory) / "check"
        cpp.write_text(prefix + drain + "\n}\n" + tick + main)
        subprocess.run([os.environ.get("CXX", "g++"), "-std=c++20", "-O1", "-Wall", "-Wextra", "-Werror",
                        str(cpp), "-o", str(binary)], check=True)
        subprocess.run([str(binary)], check=True)


if __name__ == "__main__":
    run()
