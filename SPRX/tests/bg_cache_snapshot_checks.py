#!/usr/bin/env python3
"""Run the actual read-only checksum snapshot with bounded native memory stubs."""
from pathlib import Path
import os
import subprocess
import tempfile

text = (Path(__file__).resolve().parents[1] / "t7_maps.cpp").read_text()
begin = text.index("static void TraceBgCacheState(")
end = text.index("static void TraceModTransition(", begin)
body = text[begin:end]
fixture = r'''
#include <cassert>
#include <cstdarg>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>
#define _DEBUG 1
static uintptr_t g_base=0;
static std::vector<uint8_t> image(0x1F276F0);
static bool wordsReadable=true;
static unsigned logs=0;
static std::string last;
static bool RangeReadable(uintptr_t address,size_t size) {
    if(!g_base || address<g_base || size>image.size()) return false;
    if(!wordsReadable && address==g_base+0x1F276DC) return false;
    return address-g_base<=image.size()-size;
}
static void T7Log_Write(const char* format,...) {
    char line[256]; va_list args; va_start(args,format);
    vsnprintf(line,sizeof(line),format,args); va_end(args); last=line; ++logs;
}
BODY
int main() {
    TraceBgCacheState("no base"); assert(logs==0);
    g_base=(uintptr_t)image.data();
    TraceBgCacheState("wrong signature"); assert(logs==0);
    const uint8_t signature[]={0x4c,0x89,0xf2,0x48,0xc1,0xea,0x20,0x44,0x39,0xf2};
    std::memcpy(image.data()+0x7C928E,signature,sizeof(signature));
    const uint32_t mismatch[]={0x1234,0x5678,0x9abc};
    std::memcpy(image.data()+0x1F276DC,mismatch,sizeof(mismatch));
    auto before=image;
    wordsReadable=false; TraceBgCacheState("unreadable"); assert(logs==0);
    wordsReadable=true; TraceBgCacheState("BG cache index mismatch");
    assert(logs==1 && last.find("cached=0x00001234 received=0x00005678 computed=0x00009ABC match=0")!=std::string::npos);
    assert(image==before);
    const uint32_t match[]={0x5678,0x5678,0x5678};
    std::memcpy(image.data()+0x1F276DC,match,sizeof(match));
    before=image; TraceBgCacheState(nullptr);
    assert(logs==2 && last.find("snapshot:")!=std::string::npos && last.find("match=1")!=std::string::npos);
    assert(image==before);
    std::puts("PASS: guarded BG cache snapshots, match/mismatch values, null context and no native writes");
}
'''.replace("BODY", body)
with tempfile.TemporaryDirectory(prefix="bo3-bg-cache-") as directory:
    cpp = Path(directory) / "snapshot.cpp"
    exe = Path(directory) / "snapshot"
    cpp.write_text(fixture)
    subprocess.run([os.environ.get("CXX", "g++"), "-std=c++20", "-Wall", "-Wextra", "-Werror",
                    "-fsanitize=address,undefined", str(cpp), "-o", str(exe)], check=True)
    subprocess.run([str(exe)], check=True)
