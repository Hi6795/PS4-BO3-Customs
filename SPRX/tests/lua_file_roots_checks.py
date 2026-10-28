#!/usr/bin/env python3
"""Fault-inject the production companion-file reader; this is not a PS4 emulator."""
from pathlib import Path
import os
import subprocess
import tempfile

root = Path(__file__).resolve().parents[1]
fixture = r'''
#include <algorithm>
#include <cassert>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <map>
#include <string>
#include <vector>
constexpr int SCE_KERNEL_O_RDONLY=0;
struct Handle { std::string path; size_t at; };
static std::map<std::string,std::string> files;
static std::map<int,Handle> handles;
static std::string badRead,badClose,badSeek,growing;
static int opens=0;
static std::vector<std::string> opened;
static int sceKernelOpen(const char* path,int flags,int) {
    assert(flags==SCE_KERNEL_O_RDONLY); ++opens; opened.emplace_back(path);
    if(!files.count(path)) return -1;
    int fd=handles.empty()?1:handles.rbegin()->first+1;
    handles[fd]={path,0}; return fd;
}
static int64_t sceKernelLseek(int fd,int64_t offset,int mode) {
    auto& h=handles.at(fd);
    if(h.path==badSeek) return -1;
    h.at=mode==SEEK_END?files[h.path].size():size_t(offset);
    if(mode==SEEK_END && h.path==growing) --h.at;
    return h.at;
}
static int64_t sceKernelRead(int fd,void* data,size_t size) {
    auto& h=handles.at(fd);
    if(h.path==badRead && h.at>=3) return -1;
    size_t n=std::min({size,size_t(3),files[h.path].size()-h.at});
    memcpy(data,files[h.path].data()+h.at,n); h.at+=n; return n;
}
static int sceKernelClose(int fd) {
    bool fail=handles.at(fd).path==badClose;
    handles.erase(fd); return fail?-1:0;
}
void T7Log_Write(const char*,...) {}
#include "t7_lua_files.hpp"
static const char* internal="/data/BO3-Customs/ui_scripts/test.lua";
static const char* external="/ext0/BO3-Customs/ui_scripts/test.lua";
static const char* fallback="/usb3/BO3-Customs/ui_scripts/test.lua";
static void Reset() {
    files.clear(); handles.clear(); badRead.clear(); badClose.clear(); badSeek.clear(); growing.clear();
    opens=0; opened.clear();
}
static void Expect(const char* expected,size_t maximum=64) {
    size_t length=99;
    char* body=T7LuaFiles::Read("ui_scripts/test.lua",maximum,&length);
    if(expected) assert(body && std::string(body,length)==expected && body[length]==0);
    else assert(!body && length==0);
    free(body); assert(handles.empty());
}
int main() {
    Reset(); files[internal]="internal"; files[external]="external"; files[fallback]="usb3";
    files["/usb7/BO3-Customs/ui_scripts/test.lua"]="usb7";
    files["/data/BO3-Customs/lua_roots.txt"]="/usb7/BO3-Customs\n"; // Obsolete overrides must have no effect.
    Expect("internal"); assert(opens==1 && opened[0]==internal);
    files.erase(internal); int before=opens;
    Expect("external"); assert(opens-before==2); // Internal then first external; no directory scans.
    files[internal]="internal again"; before=opens;
    Expect("internal again"); assert(opens-before==1); // An earlier external hit never outranks internal.
    files.erase(internal);
    for(int fault=0;fault<6;++fault) {
        badRead.clear(); badClose.clear(); badSeek.clear(); growing.clear(); files[external]="external";
        if(fault==0) files.erase(external); // Removed/unplugged drive.
        if(fault==1) badRead=external;
        if(fault==2) badClose=external;
        if(fault==3) badSeek=external;
        if(fault==4) growing=external;
        if(fault==5) files[external]=std::string(65,'x');
        Expect("usb3");
    }
    badRead.clear(); badClose.clear(); badSeek.clear(); growing.clear(); files[external]=""; Expect("usb3");
    files[external]=std::string("a\0b",3); Expect("usb3");
    size_t length=0; char* binary=T7LuaFiles::Read("ui_scripts/test.lua",64,&length,false);
    assert(binary && length==3 && binary[1]==0); free(binary);
    before=opens;
    assert(!T7LuaFiles::Read("../outside",64)); assert(!T7LuaFiles::Read("/absolute",64));
    assert(!T7LuaFiles::Read("lui/a/../../outside",64)); assert(opens==before);
    assert(!T7LuaFiles::Read(std::string(600,'x').c_str(),64)); assert(opens==before);
    for(const auto& path:opened) assert(path.find("lua_roots.txt")==std::string::npos);
    Reset(); files[internal]=""; files[external]="external"; Expect("external");
    files[internal]="working internal"; Expect("working internal");
    // Last mounted drive is reachable and all-missing searches have a strict bound.
    Reset(); files["/usb7/BO3-Customs/ui_scripts/test.lua"]="usb7"; Expect("usb7"); assert(opens==17);
    Reset(); Expect(nullptr); assert(opens==17);
    std::puts("PASS: unconditional internal duplicate priority, automatic external roots, one-open internal reads, bounded fallback, failed I/O, binary data and path bounds");
}
'''
with tempfile.TemporaryDirectory(prefix="bo3-lua-roots-") as directory:
    source = Path(directory) / "fixture.cpp"
    binary = Path(directory) / "fixture"
    source.write_text(fixture, encoding="utf-8")
    subprocess.run([os.environ.get("CXX", "g++"), "-std=c++20", "-Wall", "-Wextra", "-Werror",
                    "-fsanitize=address,undefined", "-pthread", "-I", str(root), str(source), "-o", str(binary)], check=True)
    subprocess.run([str(binary)], check=True, timeout=30)
