#!/usr/bin/env python3
"""Host regression: map-table Lua must be copied under list-reader protection.

Usage: python3 SPRX/tests/map_table_snapshot_checks.py SPRX/t7_maps.cpp
Compiles the production ListReader/snapshot helper into a host stress fixture.
This does not execute BO3/HKS or build a PS4 SPRX.
"""
from pathlib import Path
import subprocess
import sys
import tempfile


def section(source: str, first: str, after: str) -> str:
    start = source.index(first)
    end = source.index(after, start)
    return source[start:end]


def run(source: str) -> None:
    sync = section(source, 'struct ListReader\n', 'static Detour g_openDetour')
    snapshot = section(source, 'static char* SnapshotMapsTableLua()', 'static uint64_t MapsTable_h')
    assert 'ListReader reading;' in snapshot, 'snapshot must be protected by list reader'
    assert 'malloc(' in snapshot and 'memcpy(' in snapshot, 'snapshot must be a private copy'
    hook = section(source, 'static uint64_t MapsTable_h', 'static bool SameNoCase')
    assert 'SnapshotMapsTableLua()' in hook
    assert 'free(source);' in hook
    assert hook.index('SnapshotMapsTableLua()') < hook.index('T7Lua_RunOnTop') < hook.index('free(source);')
    harness = r'''#include <atomic>
#include <cassert>
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <thread>
#include <vector>
static std::atomic<int> g_listReaders{0};
static std::atomic<bool> g_listWriting{false};
static char g_mapsTableLua[262144 + 65536 + 64]{};
static void sceKernelUsleep(int) { std::this_thread::yield(); }
''' + sync + '\n' + snapshot + r'''
int main() {
    assert(SnapshotMapsTableLua() == nullptr); // no map-table script
    BeginListWrite();
    std::memset(g_mapsTableLua, 'z', sizeof(g_mapsTableLua));
    EndListWrite();
    assert(SnapshotMapsTableLua() == nullptr); // unterminated input
    BeginListWrite();
    g_mapsTableLua[0] = 0;
    EndListWrite();
    constexpr int rounds = 16000;
    std::atomic<bool> done{false};
    std::atomic<int> failures{0};
    std::thread writer([&] {
        for (int i = 1; i <= rounds; ++i) {
            BeginListWrite();
            std::memset(g_mapsTableLua, 'x', 1024);
            std::snprintf(g_mapsTableLua, sizeof(g_mapsTableLua), "MAP:%d:%d", i, i * 7);
            EndListWrite();
        }
        done.store(true);
    });
    auto reader = [&] {
        for (int n = 0; n < rounds || !done.load(); ++n) {
            char* copy = SnapshotMapsTableLua();
            if (!copy) continue;
            int a = 0, b = 0;
            if (std::sscanf(copy, "MAP:%d:%d", &a, &b) != 2 || b != a * 7)
                ++failures;
            std::this_thread::yield();
            if (std::sscanf(copy, "MAP:%d:%d", &a, &b) != 2 || b != a * 7)
                ++failures;
            std::free(copy);
        }
    };
    std::thread first(reader), second(reader);
    writer.join(); first.join(); second.join();
    assert(failures.load() == 0);
    // A callback can reacquire ListReader after the snapshot: no held reader.
    assert(g_listReaders.load() == 0);
    BeginListWrite(); EndListWrite();
    std::puts("PASS: 16000 concurrent map-list rewrites; two readers receive immutable valid snapshots");
}
''';
    with tempfile.TemporaryDirectory() as folder:
        cpp = Path(folder) / 'map_table.cpp'
        exe = Path(folder) / 'map_table'
        cpp.write_text(harness)
        subprocess.run(['g++', '-std=c++20', '-O2', '-pthread', '-Wall', '-Wextra', '-Werror', str(cpp), '-o', str(exe)], check=True)
        subprocess.run([str(exe)], check=True, timeout=40)


if __name__ == '__main__':
    path = Path(sys.argv[1]) if len(sys.argv) > 1 else Path('SPRX/t7_maps.cpp')
    run(path.read_text(encoding='utf-8'))
