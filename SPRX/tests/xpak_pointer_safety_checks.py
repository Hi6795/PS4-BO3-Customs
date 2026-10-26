#!/usr/bin/env python3
"""Exercise the production bounded pointer scan used by the XPAK-open detour."""
from pathlib import Path
import os
import subprocess
import tempfile


root = Path(__file__).resolve().parents[1]
platform = (root / "platform.cpp").read_text(encoding="utf-8")
header = (root / "platform.hpp").read_text(encoding="utf-8")
maps = (root / "t7_maps.cpp").read_text(encoding="utf-8")

signature = "bool SafeReadableCString(uintptr_t addr, size_t maxBytes, size_t* length)"
begin = platform.index(signature)
end = platform.index("\nbool SafeStrStr(", begin)
production = platform[begin:end]
assert signature in header

start = maps.index("static uint64_t XPakOpen_h(")
stop = maps.index("\nstatic bool AsksForFsGame(", start)
hook = maps[start:stop]
assert "SafeReadableCString((uintptr_t)pak, kNameMax, &pakLength)" in hook
assert "SafeReadableCString((uintptr_t)zone, kNameMax, nullptr)" in hook
assert hook.index("SafeReadableCString((uintptr_t)pak") < hook.index("StackName(")
assert hook.index("SafeReadableCString((uintptr_t)zone") < hook.index("StackName(")
assert hook.index("SafeReadableCString((uintptr_t)pak") < hook.index("snprintf(file,")
assert "strlen(pak)" not in hook
assert "g_unsafeXpakReports.fetch_add(1, std::memory_order_relaxed) < 4" in hook
assert "[ModLoad] xpak remap ignored unsafe text" in hook

fixture = r'''
#include <cassert>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <sys/mman.h>
#include <unistd.h>

static uintptr_t readableStart;
static uintptr_t readableEnd;
static uintptr_t ReadableEnd(uintptr_t addr, size_t need) {
    if (!need || addr < readableStart || addr >= readableEnd || addr + need < addr) return 0;
    return readableEnd;
}

PRODUCTION

int main() {
    const long pageSize = sysconf(_SC_PAGESIZE);
    assert(pageSize > 128);
    void* region = mmap(nullptr, size_t(pageSize) * 2, PROT_READ | PROT_WRITE,
                        MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
    assert(region != MAP_FAILED);
    char* bytes = static_cast<char*>(region);
    readableStart = reinterpret_cast<uintptr_t>(bytes);
    readableEnd = readableStart + uintptr_t(pageSize);
    assert(mprotect(bytes + pageSize, size_t(pageSize), PROT_NONE) == 0);

    size_t length = 900;
    assert(!SafeReadableCString(0, 56, &length));
    assert(!SafeReadableCString(1, 56, &length));
    assert(!SafeReadableCString(readableEnd, 56, &length));
    assert(!SafeReadableCString(readableStart, 0, &length));
    assert(!SafeReadableCString(UINTPTR_MAX - 8, 56, &length));
    assert(length == 900);

    std::memset(bytes, 'a', size_t(pageSize));
    const uintptr_t tail = readableEnd - 4;
    std::memcpy(reinterpret_cast<void*>(tail), "mod", 4);
    assert(SafeReadableCString(tail, 56, &length) && length == 3);
    assert(SafeReadableCString(tail, 56, nullptr));

    // A missing NUL before the inaccessible page must be rejected without
    // touching the protected second page.
    bytes[pageSize - 1] = 'z';
    assert(!SafeReadableCString(tail, 56, &length));
    assert(length == 3);

    std::memcpy(bytes, "zombiebots", 11);
    assert(SafeReadableCString(readableStart, 56, &length) && length == 10);
    std::memset(bytes, 'b', 56);
    assert(!SafeReadableCString(readableStart, 56, &length));

    bytes[55] = '\0';
    assert(SafeReadableCString(readableStart, 56, &length) && length == 55);
    assert(!SafeReadableCString(readableStart, 55, &length));

    assert(munmap(region, size_t(pageSize) * 2) == 0);
    std::puts("PASS: XPAK strings reject invalid/unterminated pointers and respect readable page bounds");
}
'''.replace("PRODUCTION", production)

with tempfile.TemporaryDirectory(prefix="bo3-xpak-pointer-") as directory:
    source = Path(directory) / "xpak.cpp"
    binary = Path(directory) / "xpak"
    source.write_text(fixture, encoding="utf-8")
    subprocess.run([os.environ.get("CXX", "g++"), "-std=c++20", "-Wall", "-Wextra", "-Werror",
                    "-fsanitize=address,undefined", str(source), "-o", str(binary)], check=True)
    subprocess.run([str(binary)], check=True)
