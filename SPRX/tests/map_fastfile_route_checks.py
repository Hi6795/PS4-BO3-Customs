"""Run the map asset file-open hook with a full registry and mocked PS4 I/O."""
from pathlib import Path
import os
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
SOURCE = (ROOT / "t7_maps.cpp").read_text()


def extract(signature):
    start = SOURCE.index(signature)
    opening = SOURCE.index("{", start)
    depth = 0
    for index in range(opening, len(SOURCE)):
        if SOURCE[index] == "{":
            depth += 1
        elif SOURCE[index] == "}":
            depth -= 1
            if depth == 0:
                return SOURCE[start:index + 1]
    raise AssertionError(f"Unterminated source function: {signature}")


PREFIX = r'''
#include <cassert>
#include <cstdarg>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#define _DEBUG
constexpr int kNameMax=56, kMaxFiles=1536;
struct Package { char name[kNameMax]; char folder[224]; char alternate[224]; char preview[288]; bool map; };
struct File { char rel[128]; char path[224]; int package; };
static Package g_packages[1]={{"zm_ds_house_zw", "/ext0/BO3-Customs/usermaps/zm_ds_house_zw", "", "preview.png", true}};
static int g_packageCount=1;
static File g_files[kMaxFiles] = {};
static int g_fileCount=kMaxFiles;
static int readers=0;
struct ListReader { ListReader() { ++readers; } ~ListReader() { --readers; } };
namespace T7Maps {}
static uintptr_t g_base=1;
static uint64_t sceKernelGetProcessTime() { return 5000000; }
static bool overrideEnabled=false, exists=true;
static File overrideFile={"zm_ds_house_zw.ff", "/data/mod-overrides/zm_ds_house_zw.ff", 0};
static std::string actualPath;
static std::string loggedPackage;
static bool refreshDuringOpen=false;
static bool EqualsNoCase(const char* a,const char* b) { return std::strcmp(a,b)==0; }
static bool EndsWith(const char* a,const char* b)
{
    const size_t an=std::strlen(a), bn=std::strlen(b);
    return an>=bn && std::strcmp(a+an-bn,b)==0;
}
static const char* Relative(const char* p)
{
    constexpr char prefix[]="/app0/zone/";
    return std::strncmp(p,prefix,sizeof(prefix)-1)==0 ? p+sizeof(prefix)-1 : nullptr;
}
static const File* StackFile(const char* rel)
{
    return overrideEnabled && std::strcmp(rel,"zm_ds_house_zw.ff")==0 ? &overrideFile : nullptr;
}
static bool FindPackageFile(const Package& p,const char* rel,char* out,size_t size)
{
    if (!exists) return false;
    std::snprintf(out,size,"%s/%s",p.folder,rel);
    return true;
}
static int LanguageCompanion(const char*) { return -1; }
static void T7Log_Write(const char* format,...) {
    if(std::strstr(format,"[MapLoad] open")) {
        va_list args; va_start(args,format);
        (void)va_arg(args,const char*); (void)va_arg(args,const char*);
        loggedPackage=va_arg(args,const char*); va_end(args);
    }
}
using Hook_t=uint64_t (*)(uint64_t,uint64_t,uint64_t,uint64_t,uint64_t,uint64_t,
                          double,double,double,double,double,double,double,double);
static uint64_t NativeOpen(uint64_t a1,uint64_t,uint64_t,uint64_t,uint64_t,uint64_t,
                           double,double,double,double,double,double,double,double)
{
    assert(readers==0); // Native code can reenter catalog refresh; do not hold its read lock here.
    if(refreshDuringOpen) {
        std::strcpy(overrideFile.path,"/data/refreshed-mod.ff");
        std::strcpy(g_files[2].path,"/data/refreshed-core-mod.ff");
        std::strcpy(g_packages[0].name,"catalog_refreshed");
    }
    actualPath=(const char*)a1; return 17;
}
static void* g_openOriginal=(void*)NativeOpen;
static bool LanguagePrefixed(const char*);
'''

MAIN = r'''
int main()
{
    std::strcpy(g_files[0].rel, "core_frontend.ff");
    std::strcpy(g_files[0].path, "/ext0/BO3-Customs/zone/core_frontend.ff");
    std::strcpy(g_files[1].rel, "en_core_frontend.ff");
    std::strcpy(g_files[1].path, "/ext0/BO3-Customs/zone/en_core_frontend.ff");
    std::strcpy(g_files[2].rel, "core_mod.ff");
    std::strcpy(g_files[2].path, "/ext0/BO3-Customs/zone/core_mod.ff");
    assert(NativeBootstrapFile("core_frontend.ff"));
    assert(NativeBootstrapFile("en_core_frontend.ff"));
    assert(NativeBootstrapFile("snd/en/core_frontend.sabl"));
    assert(!NativeBootstrapFile("core_mod.ff"));
    assert(!NativeBootstrapFile("zm_ds_house_zw.ff"));
    const char* const coreRequest="/app0/zone/core_frontend.ff";
    assert(FileOpen_h((uintptr_t)coreRequest,0,0,0,0,0,0,0,0,0,0,0,0,0)==17);
    assert(actualPath==coreRequest);
    const char* const localizedCore="/app0/zone/en_core_frontend.ff";
    assert(FileOpen_h((uintptr_t)localizedCore,0,0,0,0,0,0,0,0,0,0,0,0,0)==17);
    assert(actualPath==localizedCore);
    assert(FileOpen_h((uintptr_t)"/app0/zone/core_mod.ff",0,0,0,0,0,0,0,0,0,0,0,0,0)==17);
    assert(actualPath==g_files[2].path);
    assert(MapPackageFile("zm_ds_house_zw.ff")==0);
    assert(MapPackageFile("en_zm_ds_house_zw.ff")==0);
    assert(MapPackageFile("zm_ds_house_zw.fd")==0);
    assert(MapPackageFile("zm_ds_house_zw.xpak")==0);
    assert(MapPackageFile("en_zm_ds_house_zw.sabl")==0);
    assert(MapPackageFile("zm_ds_house_zw.sabs")==0);
    assert(MapPackageFile("snd/en/zm_ds_house_zw.sabl")==0);
    assert(MapPackageFile("other.ff")==-1);
    assert(MapPackageFile("zm_ds_house_zw.txt")==-1);
    const char* const request="/app0/zone/zm_ds_house_zw.ff";
    assert(FileOpen_h((uintptr_t)request,0,0,0,0,0,0,0,0,0,0,0,0,0)==17);
    assert(actualPath=="/ext0/BO3-Customs/usermaps/zm_ds_house_zw/zm_ds_house_zw.ff");
    overrideEnabled=true;
    assert(FileOpen_h((uintptr_t)request,0,0,0,0,0,0,0,0,0,0,0,0,0)==17);
    assert(actualPath==overrideFile.path);
    overrideEnabled=false;
    assert(FileOpen_h((uintptr_t)"/app0/zone/en_zm_ds_house_zw.ff",0,0,0,0,0,0,0,0,0,0,0,0,0)==17);
    assert(actualPath=="/ext0/BO3-Customs/usermaps/zm_ds_house_zw/en_zm_ds_house_zw.ff");
    assert(FileOpen_h((uintptr_t)"/app0/zone/zm_ds_house_zw.xpak",0,0,0,0,0,0,0,0,0,0,0,0,0)==17);
    assert(actualPath=="/ext0/BO3-Customs/usermaps/zm_ds_house_zw/zm_ds_house_zw.xpak");
    assert(FileOpen_h((uintptr_t)"/app0/zone/snd/en/zm_ds_house_zw.sabl",0,0,0,0,0,0,0,0,0,0,0,0,0)==17);
    assert(actualPath=="/ext0/BO3-Customs/usermaps/zm_ds_house_zw/snd/en/zm_ds_house_zw.sabl");
    exists=false;
    // A colliding filename belonging to another package must not be used.
    std::strcpy(g_files[3].rel, "zm_ds_house_zw.ff");
    std::strcpy(g_files[3].path, "/ext0/other/zm_ds_house_zw.ff");
    g_files[3].package=12;
    assert(FileOpen_h((uintptr_t)request,0,0,0,0,0,0,0,0,0,0,0,0,0)==17);
    assert(actualPath==request);
    // A registered fallback in the owning package is still permitted.
    g_files[3].package=0;
    assert(FileOpen_h((uintptr_t)request,0,0,0,0,0,0,0,0,0,0,0,0,0)==17);
    assert(actualPath==g_files[3].path);
    g_files[3].package=12;
    assert(FileOpen_h((uintptr_t)request,0,0,0,0,0,0,0,0,0,0,0,0,0)==17);
    assert(actualPath==request);
    assert(FileOpen_h((uintptr_t)"/app0/zone/other.ff",0,0,0,0,0,0,0,0,0,0,0,0,0)==17);
    assert(actualPath=="/app0/zone/other.ff");

    char preview[288];
    assert(T7Maps_PreviewFile("zm_ds_house_zw",preview,sizeof(preview)));
    std::strcpy(g_packages[0].preview,"changed.png");
    assert(std::strcmp(preview,"preview.png")==0);
    char shortPreview[3];
    assert(!T7Maps_PreviewFile("zm_ds_house_zw",shortPreview,sizeof(shortPreview)) && shortPreview[0]==0);
    assert(!T7Maps_PreviewFile("missing",preview,sizeof(preview)) && preview[0]==0);
    assert(!T7Maps_PreviewFile("zm_ds_house_zw",nullptr,0));

    refreshDuringOpen=true; overrideEnabled=true;
    const std::string before=overrideFile.path;
    assert(FileOpen_h((uintptr_t)request,0,0,0,0,0,0,0,0,0,0,0,0,0)==17);
    assert(actualPath==before); // Fails when the engine receives the mutable catalog pointer.
    std::strcpy(g_files[2].path,"/data/original-core-mod.ff");
    assert(FileOpen_h((uintptr_t)"/app0/zone/core_mod.ff",0,0,0,0,0,0,0,0,0,0,0,0,0)==17);
    assert(actualPath=="/data/original-core-mod.ff");
    overrideEnabled=false; exists=true;
    std::strcpy(g_packages[0].name,"zm_ds_house_zw");
    assert(FileOpen_h((uintptr_t)request,0,0,0,0,0,0,0,0,0,0,0,0,0)==17);
    assert(loggedPackage=="zm_ds_house_zw");
    assert(readers==0);
    std::puts("PASS: map routing and owned mod/global/preview path snapshots survive catalog mutation during native open");
}
'''


def main():
    snippets = [extract("static bool IsZoneFile("), extract("static bool NativeBootstrapFile("),
                extract("static int MapPackage("),
                extract("static bool LanguagePrefixed(const char* name)\n{"),
                extract("static int MapPackageFile("), extract("static uint64_t FileOpen_h("),
                extract("bool T7Maps_PreviewFile(")]
    with tempfile.TemporaryDirectory(prefix="bo3-ff-route-") as temporary:
        source = Path(temporary) / "map_route.cpp"
        output = Path(temporary) / "map_route"
        source.write_text(PREFIX + "\n".join(snippets) + MAIN)
        subprocess.run([os.environ.get("CXX", "g++"), "-std=c++20", "-Wall", "-Wextra", "-Werror",
                        str(source), "-o", str(output)], check=True)
        subprocess.run([str(output)], check=True)


if __name__ == "__main__":
    main()
