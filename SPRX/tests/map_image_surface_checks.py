"""Exercise the real GPU-handle resolver and pixel writer with mapped output fixtures."""
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
source = (ROOT / "t7_mapimages.cpp").read_text()
resolver = source[source.index("static uint8_t* PixelMemory("):source.index("static void ReleaseImage(")]
assert "RangeReadable((uintptr_t)pixels" not in resolver
make_picture = source[source.index("static uint64_t MakePicture("):source.index("static bool IsPictureName(")]
assert "RangeReadable((uintptr_t)pixels" not in make_picture
assert "surfaceBytes > bytes" in make_picture

prefix = r'''
#include <cassert>
#include <cstdint>
#include <cstring>
#include <vector>
#include <fstream>
#include <iterator>
#include "png.hpp"
static constexpr size_t kImagePixels=256, kGpuHeap=0x705D9C0, kGpuHeapTable=0x705D9B8;
static uintptr_t g_base;
static uintptr_t imageAddress;
static size_t queryCount;
static bool RangeReadable(uintptr_t address,size_t size) {
    ++queryCount;
    // Simulate readable descriptors but GPU-owned handle/output regions that
    // the generic CPU-read probe cannot certify.
    return (address == imageAddress+kImagePixels && size==8) ||
           (address == g_base+kGpuHeap && size==8) ||
           (address == g_base+kGpuHeapTable && size==8);
}
'''
main = r'''
int main(int argc,char** argv) {
    std::vector<uint8_t> globals(kGpuHeap+16), image(400), handle(16), table(16);
    g_base=(uintptr_t)globals.data(); imageAddress=(uintptr_t)image.data();
    std::vector<uint8_t> pixels(960*544*4+32,0xCD);
    auto store=[](uint8_t* p,uint64_t v){memcpy(p,&v,8);};
    store(image.data()+kImagePixels,(uintptr_t)pixels.data());
    assert(PixelMemory(imageAddress)==pixels.data());
    // Engine heap handles encode the pixel address through an indexed table.
    store(globals.data()+kGpuHeap,(uintptr_t)handle.data());
    store(globals.data()+kGpuHeapTable,(uintptr_t)table.data());
    store(handle.data()+8,2ULL<<2);
    store(table.data()+8,(uintptr_t)pixels.data()-16);
    store(image.data()+kImagePixels,(uintptr_t)handle.data());
    assert(PixelMemory(imageAddress)==pixels.data());
    store(handle.data()+8,0);
    assert(PixelMemory(imageAddress)==nullptr);
    store(handle.data()+8,2ULL<<2);
    store(table.data()+8,UINT64_MAX-1);
    assert(PixelMemory(imageAddress)==nullptr);
    store(image.data()+kImagePixels,UINT64_MAX);
    assert(PixelMemory(imageAddress)==nullptr);
    store(image.data()+kImagePixels,(uintptr_t)pixels.data());
    store(globals.data()+kGpuHeap,0);
    for(int i=1;i<argc;++i) {
        std::ifstream file(argv[i],std::ios::binary);
        std::vector<uint8_t> png((std::istreambuf_iterator<char>(file)),{});
        PngImage decoded{}; const char* error=nullptr;
        assert(Png_Open(png.data(),png.size(),&decoded,&error));
        for(auto size : {std::pair<int,int>{640,352},{960,544}}) {
            std::fill(pixels.begin(),pixels.end(),0xCD);
            assert(WritePicture(decoded,PixelMemory(imageAddress),size.first,size.second,size.first));
            for(size_t j=(size_t)size.first*size.second*4;j<pixels.size();++j) assert(pixels[j]==0xCD);
        }
    }
    printf("GPU direct/handle/null/overflow cases passed; %d PNGs rendered as previews and loading images with output canaries intact.\n",argc-1);
}
'''

if __name__ == "__main__":
    import sys
    with tempfile.TemporaryDirectory(prefix="bo3-map-surface-") as tmp:
        cpp=Path(tmp)/"check.cpp"
        exe=Path(tmp)/"check"
        cpp.write_text(prefix+resolver+main)
        subprocess.run(["clang++-18","-std=c++20","-fsanitize=address,undefined",
                        "-I",str(ROOT),str(cpp),str(ROOT/"png.cpp"),"-o",str(exe)],check=True)
        subprocess.run([str(exe),*sys.argv[1:]],check=True)
