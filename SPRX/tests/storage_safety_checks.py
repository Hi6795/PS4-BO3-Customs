#!/usr/bin/env python3
"""Fault-inject production config/share I/O and exercise the catalog lock on a host.

These fixtures check file preservation, parser bounds and lock exclusion, not BO3
gameplay or the PS4 kernel implementation.
"""
from pathlib import Path
import os
import re
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def run(name, code):
    with tempfile.TemporaryDirectory(prefix=f"bo3-{name}-") as directory:
        source = Path(directory) / "fixture.cpp"
        binary = Path(directory) / "fixture"
        source.write_text(code, encoding="utf-8")
        subprocess.run([os.environ.get("CXX", "g++"), "-std=c++20", "-Wall", "-Wextra", "-Werror",
                        "-pthread", "-fsanitize=address,undefined", "-I", str(ROOT),
                        str(source), "-o", str(binary)], check=True)
        subprocess.run([str(binary)], check=True, timeout=30)


filesystem = r'''
#include <algorithm>
#include <atomic>
#include <cassert>
#include <chrono>
#include <cstdarg>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <map>
#include <string>
#include <thread>
#include <vector>
constexpr int SCE_KERNEL_O_RDONLY=0, SCE_KERNEL_O_WRONLY=1, SCE_KERNEL_O_CREAT=512;
constexpr int SCE_KERNEL_O_TRUNC=1024, SCE_KERNEL_O_APPEND=8;
static std::map<std::string, std::string> files;
struct Open { std::string path; size_t at; };
static std::map<int, Open> handles;
static bool failWrite, failSync, failClose, failRename, failRead, failStat, failOpen;
static size_t readChunk=32, writeChunk=3;
static int renames;
struct SceKernelStat { int64_t st_size; };
[[maybe_unused]] static int sceKernelStat(const char* p,SceKernelStat* st) {
    if(failStat) return -9;
    if(!files.count(p)) return (int32_t)0x80020002;
    st->st_size=(int64_t)files[p].size(); return 0;
}
static int sceKernelOpen(const char* p,int flags,int) {
    if(failOpen) return -9;
    if(!files.count(p) && !(flags&SCE_KERNEL_O_CREAT)) return (int32_t)0x80020002;
    if(flags&SCE_KERNEL_O_TRUNC) files[p].clear();
    int fd=handles.empty()?1:handles.rbegin()->first+1;
    handles[fd]={p,(flags&SCE_KERNEL_O_APPEND)?files[p].size():0}; return fd;
}
[[maybe_unused]] static int64_t sceKernelRead(int fd,void* out,size_t size) {
    Open& f=handles.at(fd);
    if(failRead && f.at>=3) return -9;
    size_t n=std::min({size,files[f.path].size()-f.at,readChunk});
    memcpy(out,files[f.path].data()+f.at,n); f.at+=n; return (int64_t)n;
}
static int64_t sceKernelWrite(int fd,const void* data,size_t size) {
    if(failWrite) return -9;
    Open& f=handles.at(fd); size_t n=std::min(size,writeChunk);
    files[f.path].replace(f.at,n,(const char*)data,n); f.at+=n; return (int64_t)n;
}
static int sceKernelClose(int fd) { handles.erase(fd); return failClose?-9:0; }
static int sceKernelFsync(int) { return failSync?-9:0; }
static int sceKernelUnlink(const char* p) { files.erase(p); return 0; }
static int sceKernelRename(const char* a,const char* b) {
    ++renames; if(failRename) return -9;
    files[b]=files.at(a); files.erase(a); return 0;
}
[[maybe_unused]] static int sceKernelMkdir(const char*,int) { return 0; }
[[maybe_unused]] static void sceKernelUsleep(int) { std::this_thread::yield(); }
[[maybe_unused]] static uint64_t sceKernelGetProcessTime() { return 1; }
[[maybe_unused]] static void T7Log_Write(const char*,...) {}
static void ResetFiles() {
    files.clear(); handles.clear(); renames=0;
    failWrite=failSync=failClose=failRename=failRead=failStat=failOpen=false;
    readChunk=32; writeChunk=3;
}
'''

config = (ROOT / "t7_config.cpp").read_text(encoding="utf-8")
config = config[config.index("namespace T7Config"):]
config_main = r'''
static bool Owns(const char* line) { return strncmp(line,"set graphics ",13)==0; }
static void Reset() { ResetFiles(); T7Config::g_migrated=false; }
int main() {
    using namespace T7Config;
    Reset(); files[k_path]="set keep 1\nset graphics old\n";
    assert(T7Config_Save("graphics",Owns,"set graphics new\n"));
    assert(files[k_path].find("set keep 1\n")!=std::string::npos);
    assert(files[k_path].find("set graphics new\n")!=std::string::npos);
    assert(files[k_path].find("old")==std::string::npos && handles.empty());
    // Migration failure must retain both old files and retry before a save.
    Reset(); files[k_oldBinds]="bind F +use\n"; files[k_oldGraphics]="r_vsync 0\n";
    failWrite=true; char out[256];
    assert(!T7Config_Read(out,sizeof(out)) && !g_migrated && files.count(k_oldBinds));
    failWrite=false;
    assert(T7Config_Save("graphics",Owns,"set graphics new\n"));
    assert(g_migrated && !files.count(k_oldBinds) && !files.count(k_oldGraphics));
    assert(files[k_path].find("bind F +use")!=std::string::npos);
    // An unreadable or oversized legacy file cannot be silently discarded.
    Reset(); files[k_oldBinds]=std::string(kMaxText,'x');
    assert(!T7Config_Save("graphics",Owns,"x") && files.count(k_oldBinds) && !files.count(k_path));
    Reset(); failStat=true;
    assert(!T7Config_Save("graphics",Owns,"x") && !g_migrated && !files.count(k_path));
    // Failed reads and oversized input/output preserve the exact old config.
    for(int fault=0;fault<5;++fault) {
        Reset(); files[k_path]="set keep 1\n"; const auto old=files[k_path];
        if(fault==0) { failRead=true; readChunk=3; }
        if(fault==1) failSync=true;
        if(fault==2) failClose=true;
        if(fault==3) failRename=true;
        if(fault==4) failOpen=true;
        assert(!T7Config_Save("graphics",Owns,"set graphics new\n"));
        assert(files[k_path]==old && handles.empty());
    }
    Reset(); files[k_path]=std::string(kMaxText,'x'); auto old=files[k_path];
    assert(!T7Config_Save("graphics",Owns,"x") && files[k_path]==old && renames==0);
    Reset(); files[k_path]="set keep 1\n"; old=files[k_path];
    const std::string tooLarge(kMaxText,'x');
    assert(!T7Config_Save("graphics",Owns,tooLarge.c_str()) && files[k_path]==old && renames==0);
    Reset(); files[k_path]=std::string(kMaxText-1,'x');
    assert(ReadAll(k_path,g_current,sizeof(g_current))==(int64_t)kMaxText-1);
    assert(!T7Config_Read(nullptr,0));
    std::puts("PASS: config partial writes, migration retry, read/flush/close/rename failures and size limits");
}
'''
run("config-storage", filesystem + config + config_main)

share = (ROOT / "t7_share.cpp").read_text(encoding="utf-8")
share_prefix = r'''
#include "t7_share.hpp"
PRODUCTION_LIMITS
PRODUCTION_ENTRY
struct Stream {};
struct Slot {};
struct Link {
    Stream* stream; Slot* slots=nullptr; void* list=nullptr; size_t listSize=0,listAt=0;
    std::vector<std::string> lines; size_t next=0;
};
struct ListSink { void* list; size_t used; };
struct FileSink { int fd; char* buffer; size_t used; uint64_t a,b; Slot* slots; int spill; const char* path; };
static std::atomic<bool> g_stop{false};
static std::atomic<uint64_t> g_done{0},g_received{0},g_hostRead{0},g_hostUpload{0},g_writeUs{0},g_writeBytes{0};
static std::string body;
static bool Fail(const char*,...) { return false; }
static void MakeParents(const char*) {}
static bool SendText(Stream&,const char*,...) { return true; }
static bool ReadLine(Stream&,char* out,size_t size) {
    snprintf(out,size,"DATA %zu 0 0",body.size()); return true;
}
static int ReadBody(Stream&,char* out,int want) {
    const size_t n=std::min(body.size(),(size_t)want);
    memcpy(out,body.data(),n); body.erase(0,n); return (int)n;
}
static bool Flush(FileSink&) { return true; }
static bool ToFile(void*,uint64_t,const uint8_t*,size_t) { return false; }
static bool ToList(void*,uint64_t,const uint8_t*,size_t) { return false; }
template<class S,class C> static const char* UdpPull(Link&,int,uint64_t,uint64_t*,S,C) { return "unused UDP fixture"; }
static bool NextLine(Link& link,char* out,size_t size) {
    if(link.next==link.lines.size()) return false;
    snprintf(out,size,"%s",link.lines[link.next++].c_str()); return true;
}
'''
limit_names = ("kMaxEntries", "kWalkDepth", "kRelMax", "kPathMax", "kChunk", "kUdpRing",
               "kMaxSharedFileBytes", "kMaxSharedTotalBytes")
limits = "\n".join(re.search(r"^constexpr[^\n]*\b" + name + r"\s*=[^;]*;", share, re.M).group()
                   for name in limit_names)
entry = share[share.index("struct Entry\n"):share.index("struct Session\n")]
share_prefix = share_prefix.replace("PRODUCTION_LIMITS", limits).replace("PRODUCTION_ENTRY", entry)
safe = share[share.index("static bool SafeName("):share.index("static bool ReadMapName(")]
parser = share[share.index("static bool ReadDecimal("):share.index("static bool Exchange(")]
fetch = share[share.index("static bool Fetch("):share.index("static bool ReadDecimal(")]
write = share[share.index("static bool WriteAll(int fd"):share.index("struct FileSink")]
share_main = r'''
static bool Manifest(std::vector<std::string> lines) {
    Stream stream; Link link{&stream,nullptr,nullptr,0,0,std::move(lines),0};
    Entry entries[kMaxEntries]; int count=0,mods=0; char map[kT7ShareName]={}; bool custom=false;
    return ReadList(link,entries,&count,map,&custom,mapMods,&mods);
}
int main() {
    Entry e{};
    assert(ParseFileRecord("0 -1 0 zone/workshop.json",0,0,&e) && e.kind==-1 && e.size==0);
    assert(ParseFileRecord("0 0 68719476736 zm_mod.ff",0,1,&e));
    for(const char* bad:{"0 -1 -1 x.ff","0 -1 68719476737 x.ff","0 -1 18446744073709551616 x.ff",
                        "0 -2 4 x.ff","0 1 4 x.ff","1 -1 4 x.ff","0x -1 4 x.ff",
                        "0 -1 4 ../x.ff","0 -1 4x x.ff","0 -1 4","0 0 4 x.ff"})
        assert(!ParseFileRecord(bad,0,0,&e));
    assert(Manifest({"BO3C 1 OK","map zm_map 1","mod bots","file 0 0 12 zm_mod.ff","end"}));
    assert(!Manifest({"BO3C 1 OK","file 0 -1 -1 workshop.ff","end"}));
    std::vector<std::string> large={"BO3C 1 OK"};
    for(int i=0;i<5;++i) large.push_back("file "+std::to_string(i)+" -1 68719476736 map.ff");
    large.push_back("end"); assert(!Manifest(large));
    // Exercise actual TCP Fetch with partial writes and existing destination.
    Stream stream; Link link{&stream,nullptr,nullptr,0,0,{},0}; char buffer[64];
    for(int fault=0;fault<5;++fault) {
        ResetFiles(); files["/target"]="previous"; body="new";
        Entry entry{3,-1,true,"workshop.ff"};
        failRename=fault==1; failSync=fault==2; failClose=fault==3; failWrite=fault==4;
        bool ok=Fetch(link,0,entry,"/target",buffer);
        assert(ok==(fault==0) && files["/target"]==(fault==0?"new":"previous") && handles.empty());
    }
    ResetFiles(); files["/target"]="previous"; files["/target.part"]="new";
    Entry entry{3,-1,true,"workshop.ff"}; body.clear();
    assert(Fetch(link,0,entry,"/target",buffer) && files["/target"]=="new");
    std::puts("PASS: share numeric/path/aggregate bounds, partial writes and failed replacement preservation");
}
'''
# The manifest output buffer is separate from the map-name buffer.
share_main = 'static char mapMods[kT7ShareMods][kT7ShareName];\n' + share_main
run("share-storage", filesystem + share_prefix + safe + write + fetch + parser + share_main)

maps = (ROOT / "t7_maps.cpp").read_text(encoding="utf-8")
choice = maps[maps.index("static bool WriteChosenMods("):maps.index("static void AppendModFiles(")]
run("chosen-mod-storage", filesystem + r'''
constexpr int kNameMax=56;
static const char k_chosenModFile[]="/data/BO3-Customs/active_mod.txt";
''' + choice + r'''
int main() {
    const char names[2][kNameMax]={"bots","lobby"};
    for(int fault=0;fault<5;++fault) {
        ResetFiles(); files[k_chosenModFile]="previous\n";
        failWrite=fault==1; failSync=fault==2; failClose=fault==3; failRename=fault==4;
        assert(WriteChosenMods(names,2)==(fault==0));
        assert(files[k_chosenModFile]==(fault==0?"bots\nlobby\n":"previous\n"));
        assert(handles.empty() && !files.count("/data/BO3-Customs/active_mod.txt.tmp"));
    }
    ResetFiles(); files[k_chosenModFile]="previous\n";
    assert(WriteChosenMods(names,0) && files[k_chosenModFile].empty());
    std::puts("PASS: chosen-mod selection survives write/flush/close/rename failures and short writes");
}
''')
locks = maps[maps.index("static std::atomic<int32_t> g_listReaders"):maps.index("static Detour g_openDetour")]
builder = maps[maps.index("static void BuildModsLua()"):maps.index("static uint64_t MapsTable_h(")]
assert "ListReader reading;" in builder
catalog_main = r'''
static int catalog=0;
static std::atomic<int> writers{0}, violations{0}, reads{0};
int main() {
    // Hold a real reader and confirm neither of two writers can enter yet.
    std::atomic<int> entered{0};
    ListReader* hold=new ListReader;
    auto writer=[&] {
        for(int i=0;i<2000;++i) {
            BeginListWrite();
            if(writers.fetch_add(1)!=0) ++violations;
            ++entered; ++catalog; std::this_thread::yield();
            writers.fetch_sub(1); EndListWrite();
        }
    };
    std::thread a(writer), b(writer);
    std::this_thread::sleep_for(std::chrono::milliseconds(20));
    assert(entered==0); delete hold;
    auto reader=[] {
        for(int i=0;i<3000;++i) {
            ListReader reading;
            if(writers.load()!=0) ++violations;
            const int before=catalog; std::this_thread::yield();
            if(before!=catalog) ++violations;
            ++reads;
        }
    };
    std::thread c(reader), d(reader);
    a.join(); b.join(); c.join(); d.join();
    assert(violations==0 && catalog==4000 && reads==6000);
    assert(g_listReaders==0 && !g_listWriting);
    std::puts("PASS: catalog writers serialize, readers block writers and observe stable data");
}
'''
run("catalog-lock", r'''
#include <atomic>
#include <cassert>
#include <chrono>
#include <cstdint>
#include <cstdio>
#include <thread>
static void sceKernelUsleep(int) { std::this_thread::yield(); }
''' + locks + catalog_main)
