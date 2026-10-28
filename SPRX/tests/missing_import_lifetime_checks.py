"""Compile the real import/dialog hooks and release the script page between calls."""
import os
from pathlib import Path
import resource
import signal
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
LOG = (ROOT / "t7_log.cpp").read_text()
STATE = LOG[LOG.index("struct MissingImport"):LOG.index("static Detour g_defaultDetour")]
START = LOG.index("static bool CopyImportName(") if "static bool CopyImportName(" in LOG else LOG.index("static uint64_t ScriptImport(")
HOOKS = LOG[START:LOG.index("static void LogScriptCall(")]
PREFIX = r'''
#include <atomic>
#include <cassert>
#include <cstdarg>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>
#include <sys/mman.h>
constexpr size_t kNameMax=192;
using Passthrough_t=uint64_t(*)(uint64_t,uint64_t,uint64_t,uint64_t,uint64_t,uint64_t,
                                double,double,double,double,double,double,double,double);
static unsigned importCalls=0, dialogCalls=0;
static uint64_t importResult=0;
static bool scriptReadable=true, resetInsideImport=false;
static void* page=nullptr;
static std::vector<std::string> lines;
static void ResetMissingImports();
static uint64_t ImportNative(uint64_t a,uint64_t b,uint64_t c,uint64_t d,uint64_t e,uint64_t f,
                              double x0,double x1,double x2,double x3,double x4,double x5,double x6,double x7)
{
    assert((a==0 || a==1) && b==(uintptr_t)page && c && d==4 && e==5 && f==6);
    const double xs[]={x0,x1,x2,x3,x4,x5,x6,x7};
    for(unsigned i=0;i<8;++i) assert(xs[i]==i+0.25);
    ++importCalls;
    if(resetInsideImport) ResetMissingImports();
    return importResult;
}
static uint64_t DialogNative(uint64_t a,uint64_t b,uint64_t c,uint64_t d,uint64_t e,uint64_t f,
                              double x0,double x1,double x2,double x3,double x4,double x5,double x6,double x7)
{
    assert(a==8 && b && c==3 && d==4 && e==5 && f==6);
    const double xs[]={x0,x1,x2,x3,x4,x5,x6,x7};
    for(unsigned i=0;i<8;++i) assert(xs[i]==i+0.25);
    ++dialogCalls; return 0xC0FFEE;
}
static void* g_scriptImportOriginal=(void*)ImportNative;
static void* g_errorDialogOriginal=(void*)DialogNative;
static uint32_t g_scriptErrors=0;
[[maybe_unused]] static void ScriptErrorSummary(const char*) {}
static unsigned cacheDiagnostics=0;
void T7Maps_TraceDiagnostics(const char* why) {
    if(std::strcmp(why,"BG cache index mismatch")==0) ++cacheDiagnostics;
}
static bool RangeReadable(uintptr_t address,size_t size)
{
    if(!address || address+size<address) return false;
    const uintptr_t begin=(uintptr_t)page;
    if(address>=begin && address<begin+uint64_t(UINT32_MAX)+8192)
        return scriptReadable && address<begin+4096 && size<=begin+4096-address;
    return address>=4096;
}
static const char* ReadableText(uintptr_t p)
{ return RangeReadable(p,2) ? (const char*)p : nullptr; }
static void T7Log_Write(const char* format,...)
{
    char text[2048]; va_list args; va_start(args,format);
    vsnprintf(text,sizeof(text),format,args); va_end(args); lines.emplace_back(text);
}
'''
MAIN = r'''
static void Capture(unsigned vm)
{
    scriptReadable=true;
    assert(mprotect(page,4096,PROT_READ|PROT_WRITE)==0);
    std::memset(page,0,4096);
    *(uint32_t*)((char*)page+52)=64;
    std::strcpy((char*)page+64,"scripts/zm/test_import.gsc");
    alignas(uint32_t) unsigned char imports[12]={};
    *(uint32_t*)imports=0xDEADBEEF;
    *(uint32_t*)(imports+4)=0x12345678;
    imports[10]=4; imports[11]=0x21;
    assert(ScriptImport(vm,(uintptr_t)page,(uintptr_t)imports,4,5,6,
                        0.25,1.25,2.25,3.25,4.25,5.25,6.25,7.25)==importResult);
}
static unsigned Details()
{
    unsigned count=0;
    for(const auto& line:lines) if(line.find("linker could not resolve")!=std::string::npos) ++count;
    return count;
}
static void Dialog()
{
    static const char error[]="2 script error(s): unresolved external";
    assert(ErrorDialog(8,(uintptr_t)error,3,4,5,6,0.25,1.25,2.25,3.25,4.25,5.25,6.25,7.25)==0xC0FFEE);
}
int main()
{
    page=mmap(nullptr,4096,PROT_READ|PROT_WRITE,MAP_PRIVATE|MAP_ANONYMOUS,-1,0);
    assert(page!=MAP_FAILED);
    Capture(0);
    scriptReadable=false;
    assert(mprotect(page,4096,PROT_NONE)==0);
    Dialog();
    assert(dialogCalls==1 && Details()==1);
    assert(lines.back().find("scripts/zm/test_import.gsc")!=std::string::npos);
    Dialog();
    assert(Details()==1); // Consumed once; never attribute a later error to this import.
    Capture(0); Capture(1);
    scriptReadable=false;
    assert(mprotect(page,4096,PROT_NONE)==0);
    Dialog();
    assert(Details()==3); // Independent server/client snapshots.
    Capture(0);
    ResetMissingImports();
    scriptReadable=false;
    assert(mprotect(page,4096,PROT_NONE)==0);
    Dialog();
    assert(Details()==3);
    resetInsideImport=true;
    Capture(1); // A level reset during the native resolver must discard this old result.
    resetInsideImport=false;
    scriptReadable=false;
    assert(mprotect(page,4096,PROT_NONE)==0);
    Dialog();
    assert(Details()==3 && importCalls==5 && dialogCalls==5);
    assert(!g_importLock.test_and_set(std::memory_order_acquire));
    Capture(0); // Optional diagnostics may be busy; native resolver must still run.
    g_importLock.clear(std::memory_order_release);
    Dialog();
    assert(Details()==3 && importCalls==6 && dialogCalls==6);
    importResult=0x100; // Preserve the full native result, including upper bits.
    Capture(0); Dialog();
    assert(Details()==4);
    importResult=1; // Successful resolution must not manufacture a missing import.
    Capture(0); Dialog();
    assert(Details()==4 && importCalls==8 && dialogCalls==8);
    char copied[kNameMax];
    *(uint32_t*)((char*)page+52)=4095;
    ((char*)page)[4095]='x';
    assert(!CopyImportName((uintptr_t)page,copied));
    *(uint32_t*)((char*)page+52)=UINT32_MAX;
    assert(!CopyImportName((uintptr_t)page,copied));
    scriptReadable=false;
    assert(mprotect(page,4096,PROT_NONE)==0);
    assert(!CopyImportName((uintptr_t)page,copied));
    munmap(page,4096);
    static const char cacheError[]="EXE_ERR_BG_CACHE";
    assert(ErrorDialog(8,(uintptr_t)cacheError,3,4,5,6,0.25,1.25,2.25,3.25,4.25,5.25,6.25,7.25)==0xC0FFEE);
    assert(cacheDiagnostics==1 && dialogCalls==9);
    puts("PASS: released-script lifetime, per-VM snapshots, consume/reset and native hook forwarding");
}
'''


def main():
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    with tempfile.TemporaryDirectory(prefix="bo3-import-lifetime-") as directory:
        source, binary = Path(directory) / "test.cpp", Path(directory) / "test"
        fallback = "static void ResetMissingImports() {}\n" if "static void ResetMissingImports()" not in LOG else ""
        test_main = MAIN
        if "--expect-unfixed" in sys.argv:
            test_main = MAIN.split("    Dialog();", 1)[0] + "    Dialog();\n    return 0;\n}\n"
        source.write_text(PREFIX + STATE + fallback + HOOKS + test_main)
        subprocess.run([os.environ.get("CXX", "g++"), "-std=c++20", "-Wall", "-Wextra", "-Werror",
                        "-O1", "-pthread", str(source), "-o", str(binary)], check=True)
        result = subprocess.run([str(binary)])
        if "--expect-unfixed" in sys.argv:
            assert result.returncode == -signal.SIGSEGV, result.returncode
            print("CONFIRMED: unfixed native dialog hook dereferences released script memory (SIGSEGV)")
        else:
            result.check_returncode()
            level = LOG[LOG.index("void T7Log_NewLevel()"):LOG.index("void T7Log_Pools(")]
            assert "ResetMissingImports();" in level


if __name__ == "__main__":
    main()
