#!/usr/bin/env python3
"""Host regression: exercise the exact ListFolder body with mock PS4 dirents."""
from pathlib import Path
import re
import subprocess
import tempfile

source = (Path(__file__).resolve().parents[1] / "t7_maps.cpp").read_text()
match = re.search(r"constexpr int\s+kMaxListed\s*=\s*(\d+)\s*;", source)
assert match, "missing kMaxListed"
capacity = int(match.group(1))
start = source.index("static int ListFolder(")
end = source.index("\nstatic void AddFile(", start)
function = source[start:end]

prefix = r'''
#include <cstdio>
#include <cstring>
#include <cstdint>
#include <cstddef>
#include <cstdarg>
constexpr int SCE_KERNEL_O_RDONLY = 0;
constexpr int SCE_KERNEL_O_DIRECTORY = 0;
constexpr uint8_t SCE_KERNEL_DT_DIR = 4;
constexpr size_t kListedNameMax = 96;
struct SceKernelDirent {
    uint32_t d_fileno;
    uint16_t d_reclen;
    uint8_t d_type;
    char d_name[96];
};
static int total = 0, nextEntry = 0, warnings = 0, malformed = 0;
static int sceKernelOpen(const char*, int, int) { nextEntry = 0; return 1; }
static int sceKernelClose(int) { return 0; }
static int sceKernelGetdents(int, char* dst, size_t cap) {
    int used = 0;
    while (nextEntry < total && used + (int)sizeof(SceKernelDirent) <= (int)cap) {
        if (malformed && nextEntry == 1) {
            SceKernelDirent bad{};
            bad.d_fileno = 2;
            bad.d_type = SCE_KERNEL_DT_DIR;
            const size_t header = offsetof(SceKernelDirent, d_name);
            size_t emitted;
            if (malformed == 1) { bad.d_reclen = 0; emitted = header; }
            else if (malformed == 2) { bad.d_reclen = 65535; emitted = sizeof(bad); }
            else { bad.d_reclen = (uint16_t)(header + 2); memcpy(bad.d_name, "xy", 2); emitted = bad.d_reclen; }
            memcpy(dst + used, &bad, emitted);
            used += (int)emitted;
            ++nextEntry;
            break;
        }
        SceKernelDirent item{};
        item.d_fileno = (uint32_t)(nextEntry + 1);
        item.d_reclen = sizeof(item);
        item.d_type = SCE_KERNEL_DT_DIR;
        snprintf(item.d_name, sizeof(item.d_name), "zm_map_%04d", nextEntry++);
        memcpy(dst + used, &item, sizeof(item));
        used += sizeof(item);
    }
    return used;
}
[[maybe_unused]] static void T7Log_Write(const char*, ...) { ++warnings; }
'''
suffix = r'''
int main() {
    static char names[kMaxListed][kListedNameMax];
    total = 130;
    int count = ListFolder("/fake/usermaps", true, names, kMaxListed);
    if (count != 130) { fprintf(stderr, "130 entries: got %d\n", count); return 1; }
    if (strcmp(names[0], "zm_map_0000") || strcmp(names[129], "zm_map_0129")) return 2;
    total = kMaxListed + 1;
    warnings = 0;
    count = ListFolder("/fake/usermaps", true, names, kMaxListed);
    if (count != kMaxListed || warnings != 1) {
        fprintf(stderr, "overflow: count=%d warnings=%d\n", count, warnings);
        return 3;
    }
    total = 0;
    warnings = 0;
    if (ListFolder("/fake/usermaps", true, names, kMaxListed) != 0 || warnings) return 4;
    for (int kind = 1; kind <= 3; ++kind) {
        total = 2;
        malformed = kind;
        warnings = 0;
        count = ListFolder("/fake/usermaps", true, names, kMaxListed);
        const int expectedWarnings = kind == 3 ? 0 : 1;
        if (count != 1 || warnings != expectedWarnings || strcmp(names[0], "zm_map_0000")) {
            fprintf(stderr, "malformed %d: count=%d warnings=%d\\n", kind, count, warnings);
            return 5;
        }
    }
    puts("PASS: 130 entries, overflow warning, empty, invalid lengths, unterminated name");
}
'''
with tempfile.TemporaryDirectory() as temp:
    cpp = Path(temp) / "scan.cpp"
    exe = Path(temp) / "scan"
    cpp.write_text(prefix + f"constexpr int kMaxListed = {capacity};\n" + function + suffix)
    subprocess.run(["g++", "-std=c++20", "-Wall", "-Wextra", "-Werror", "-O2", "-fsanitize=address,undefined", "-fno-sanitize-recover=all", str(cpp), "-o", str(exe)], check=True)
    subprocess.run([str(exe)], check=True)
