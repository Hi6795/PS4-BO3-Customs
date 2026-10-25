#!/usr/bin/env python3
"""Compile the production RemoveTree body with fake PS4 dirents and check fail-closed deletion."""
from pathlib import Path
import subprocess
import tempfile

source = (Path(__file__).resolve().parents[1] / "t7_maps.cpp").read_text()
start = source.index("static int RemoveTree(")
end = source.index("\nstatic void* UninstallThread(", start)
function = source[start:end]
prefix = r'''
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <cstdint>
#include <cstddef>
#include <climits>
#include <vector>
#include <string>
constexpr int SCE_KERNEL_O_RDONLY = 0;
constexpr int SCE_KERNEL_O_DIRECTORY = 0;
constexpr uint8_t SCE_KERNEL_DT_DIR = 4;
struct SceKernelDirent {
    uint32_t d_fileno;
    uint16_t d_reclen;
    uint8_t d_type;
    char d_name[96];
};
static std::vector<char> data;
static int reads = 0, deletes = 0, removed = 0, failRead = 0;
static int sceKernelOpen(const char*, int, int) { reads = 0; return 1; }
static int sceKernelClose(int) { return 0; }
static int sceKernelGetdents(int, char* dst, size_t cap) {
    if (failRead) return -1;
    if (reads++) return 0;
    if (data.size() > cap) return -1;
    memcpy(dst, data.data(), data.size());
    return (int)data.size();
}
static int sceKernelUnlink(const char*) { ++deletes; return 0; }
static int sceKernelRmdir(const char*) { ++removed; return 0; }
static void add(const char* name, int kind=0) {
    SceKernelDirent d{};
    d.d_fileno = 1;
    d.d_type = 0;
    d.d_reclen = sizeof(d);
    if (kind == 1) d.d_reclen = 0;
    if (kind == 2) d.d_reclen = 65535;
    if (kind == 3) { d.d_reclen = offsetof(SceKernelDirent, d_name) + 2; memcpy(d.d_name, "xy", 2); }
    if (!kind) snprintf(d.d_name, sizeof(d.d_name), "%s", name);
    size_t n = kind == 3 ? d.d_reclen : sizeof(d);
    auto* p = (char*)&d;
    data.insert(data.end(), p, p+n);
}
'''
suffix = r'''
int main() {
    data.clear(); add("a.ff"); add("b.ff");
    deletes = removed = 0;
    if (RemoveTree("/data/BO3-Customs/usermaps/zm_a", 0) || deletes != 2 || removed != 1) return 1;
    for (int kind = 1; kind <= 3; ++kind) {
        data.clear(); add("a.ff"); add("bad", kind);
        deletes = removed = 0;
        if (RemoveTree("/data/BO3-Customs/usermaps/zm_a", 0) != 1 || deletes || removed) {
            fprintf(stderr, "unsafe mutation for invalid record %d: %d %d\n", kind, deletes, removed); return 2;
        }
    }
    data.clear(); add("a.ff"); add("../outside.ff");
    deletes = removed = 0;
    if (RemoveTree("/data/BO3-Customs/usermaps/zm_a", 0) != 1 || deletes || removed) return 6;
    data.clear(); add("a.ff"); data.push_back('x');
    deletes = removed = 0;
    if (RemoveTree("/data/BO3-Customs/usermaps/zm_a", 0) != 1 || deletes || removed) return 3;
    data.clear(); add("a.ff"); failRead = 1;
    deletes = removed = 0;
    if (RemoveTree("/data/BO3-Customs/usermaps/zm_a", 0) != 1 || deletes || removed) return 4;
    failRead = 0;
    data.clear(); add("."); add(".."); add("a.ff");
    deletes = removed = 0;
    if (RemoveTree("/data/BO3-Customs/usermaps/zm_a", 0) || deletes != 1 || removed != 1) return 5;
    puts("PASS: valid listing, malformed records, truncated tail, read failure, dot entries, path traversal");
}
'''
with tempfile.TemporaryDirectory() as temp:
    cpp = Path(temp) / "remove.cpp"
    exe = Path(temp) / "remove"
    cpp.write_text(prefix + function + suffix)
    subprocess.run(["g++", "-std=c++20", "-Wall", "-Wextra", "-Werror", "-O1", "-fsanitize=address,undefined", "-fno-sanitize-recover=all", str(cpp), "-o", str(exe)], check=True)
    subprocess.run([str(exe)], check=True)
