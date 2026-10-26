#!/usr/bin/env python3
"""Run the production PNG file reader against mocked PS4 kernel I/O.

OpenOrbis SDK v0.5.4 has an incompatible struct stat layout, so st_size must
never determine the number of PNG bytes the SPRX reads.
"""
from pathlib import Path
import os
import subprocess
import tempfile

root = Path(__file__).resolve().parents[1]
source = (root / "t7_mapimages.cpp").read_text(encoding="utf-8")
start = source.index("static uint8_t* ReadWholeFile(")
end = source.index("\nstatic uint8_t* PixelMemory(", start)
read_whole_file = source[start:end]

fixture = r'''
#include <cassert>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <sys/types.h>
#include <vector>

#define SCE_KERNEL_O_RDONLY 0

namespace T7MapImages {
constexpr size_t kMaxFileBytes = 48u * 1024 * 1024;
struct SceKernelStat { int64_t st_size; };
static std::vector<uint8_t> g_contents;
static size_t g_position = 0;
static size_t g_chunkSize = 13;
static int64_t g_reportedSize = 16; // Simulates a bad PS4 struct stat offset.
static int64_t g_seekSize = -1;
static int g_statCalls = 0;
static int g_closeCalls = 0;
static bool g_openFails = false;
static bool g_endSeekFails = false;
static bool g_rewindFails = false;
static bool g_readFails = false;

[[maybe_unused]] static int sceKernelStat(const char*, SceKernelStat* st) {
    ++g_statCalls;
    st->st_size = g_reportedSize;
    return 0;
}
static int sceKernelOpen(const char*, int, int) {
    if (g_openFails) return -1;
    g_position = 0;
    return 42;
}
[[maybe_unused]] static off_t sceKernelLseek(int fd, off_t offset, int whence) {
    assert(fd == 42 && offset == 0);
    if (whence == SEEK_END) {
        if (g_endSeekFails) return -1;
        return g_seekSize >= 0 ? g_seekSize : static_cast<off_t>(g_contents.size());
    }
    assert(whence == SEEK_SET);
    if (g_rewindFails) return -1;
    g_position = 0;
    return 0;
}
static int64_t sceKernelRead(int fd, void* data, size_t count) {
    assert(fd == 42);
    if (g_readFails) return -1;
    const size_t available = g_contents.size() - g_position;
    const size_t take = count < available ? count : available;
    const size_t n = take < g_chunkSize ? take : g_chunkSize;
    if (n) std::memcpy(data, g_contents.data() + g_position, n);
    g_position += n;
    return static_cast<int64_t>(n);
}
static int sceKernelClose(int fd) {
    assert(fd == 42);
    ++g_closeCalls;
    return 0;
}

READER
}

int main() {
    using namespace T7MapImages;
    for (int i = 0; i < 513; ++i) g_contents.push_back(static_cast<uint8_t>(i % 251));

    size_t size = 0;
    const char* reason = nullptr;
    uint8_t* bytes = ReadWholeFile("/data/BO3-Customs/usermaps/zm_test/previewimage.png", &size, &reason);
    assert(bytes && size == g_contents.size()); // Old st_size-based reader yields 16 bytes.
    assert(std::memcmp(bytes, g_contents.data(), size) == 0);
    std::free(bytes);
    assert(g_closeCalls == 1);
    assert(g_statCalls == 0); // Don't consult the incompatible SDK stat layout.

    // Short reads must be retried until exactly the file size has been consumed.
    g_chunkSize = 1;
    bytes = ReadWholeFile("test", &size, &reason);
    assert(bytes && size == 513);
    std::free(bytes);
    assert(g_closeCalls == 2);

    g_contents.clear();
    bytes = ReadWholeFile("test", &size, &reason);
    assert(!bytes && reason && std::strstr(reason, "empty"));
    assert(g_closeCalls == 3);

    g_seekSize = static_cast<int64_t>(kMaxFileBytes) + 1;
    bytes = ReadWholeFile("test", &size, &reason);
    assert(!bytes && reason && std::strstr(reason, "oversized"));
    assert(g_closeCalls == 4);
    g_seekSize = -1;

    g_endSeekFails = true;
    bytes = ReadWholeFile("test", &size, &reason);
    assert(!bytes && reason && std::strstr(reason, "seek"));
    assert(g_closeCalls == 5);
    g_endSeekFails = false;

    g_contents.assign(10, 0x71);
    g_rewindFails = true;
    bytes = ReadWholeFile("test", &size, &reason);
    assert(!bytes && reason && std::strstr(reason, "seek"));
    assert(g_closeCalls == 6);
    g_rewindFails = false;

    g_readFails = true;
    bytes = ReadWholeFile("test", &size, &reason);
    assert(!bytes && reason && std::strstr(reason, "read"));
    assert(g_closeCalls == 7);
    g_readFails = false;

    g_seekSize = 20; // File shrank between sizing and reading.
    bytes = ReadWholeFile("test", &size, &reason);
    assert(!bytes && reason && std::strstr(reason, "read"));
    assert(g_closeCalls == 8);
    g_seekSize = -1;

    g_openFails = true;
    bytes = ReadWholeFile("test", &size, &reason);
    assert(!bytes && reason && std::strstr(reason, "open"));
    assert(g_closeCalls == 8);
    assert(g_statCalls == 0);
    std::puts("PASS: complete PNG reads despite bad stat layout; partial/empty/oversize/failure cleanup");
}
'''.replace("READER", read_whole_file)

with tempfile.TemporaryDirectory(prefix="bo3-icon-file-") as directory:
    cpp = Path(directory) / "read.cpp"
    binary = Path(directory) / "read"
    cpp.write_text(fixture, encoding="utf-8")
    subprocess.run([os.environ.get("CXX", "g++"), "-std=c++20", "-Wall", "-Wextra", "-Werror",
                    "-fsanitize=address,undefined", str(cpp), "-o", str(binary)], check=True)
    subprocess.run([str(binary)], check=True)
