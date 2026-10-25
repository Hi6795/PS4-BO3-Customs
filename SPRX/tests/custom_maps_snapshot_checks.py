#!/usr/bin/env python3
"""Host regression: custom-map Lua injection must use a reader-guarded snapshot.

Usage: python3 SPRX/tests/custom_maps_snapshot_checks.py SPRX/t7_maps.cpp SPRX/t7_lua.cpp
Compiles the production synchronization and snapshot helper on x86-64 Linux.
This is not a PS4 SPRX build or BO3/HKS execution.
"""
from pathlib import Path
import subprocess
import sys
import tempfile


def section(source: str, first: str, after: str) -> str:
    start = source.index(first)
    return source[start:source.index(after, start)]


def check(maps: str, lua: str) -> None:
    sync = section(maps, 'struct ListReader\n', 'static Detour g_openDetour')
    helper = section(maps, 'char* T7Maps_CustomMapsLuaSnapshot()', 'bool T7Maps_MapFile(')
    assert 'ListReader reading;' in helper
    assert 'malloc(length + 1)' in helper and 'memcpy(copy, source, length + 1)' in helper
    assert 'strnlen(source, sizeof(g_customMapsLua))' in helper
    assert 'T7Maps_CustomMapsLua()' not in lua
    caller = section(lua, 'static void RunCustomMapsLua()', 'static char* ReadScript(')
    assert 'T7Maps_CustomMapsLuaSnapshot()' in caller
    assert caller.index('T7Maps_CustomMapsLuaSnapshot()') < caller.index('RunLua(source,') < caller.index('free(source);')
    assert lua.count('RunCustomMapsLua();') == 4, 'all four injection sites must use a snapshot'
    harness = r'''#include <atomic>
#include <cassert>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <thread>
static std::atomic<int32_t> g_listReaders{0};
static std::atomic<bool> g_listWriting{false};
static char g_customMapsLua[262144]{};
static void sceKernelUsleep(int) { std::this_thread::yield(); }
''' + sync + r'''
namespace T7Maps { using ::ListReader; using ::g_customMapsLua; }
''' + helper + r'''
int main() {
    char* empty = T7Maps_CustomMapsLuaSnapshot();
    assert(empty && std::strcmp(empty, "rawset(_G, \"BO3CustomMaps\", {})\n") == 0);
    std::free(empty);
    BeginListWrite();
    std::memset(g_customMapsLua, 'x', sizeof(g_customMapsLua));
    EndListWrite();
    assert(T7Maps_CustomMapsLuaSnapshot() == nullptr);
    BeginListWrite(); g_customMapsLua[0] = 0; EndListWrite();
    constexpr int rounds = 16000;
    std::atomic<bool> done{false};
    std::atomic<int> failures{0};
    std::thread writer([&] {
        for (int i = 1; i <= rounds; ++i) {
            BeginListWrite();
            std::memset(g_customMapsLua, 'x', 4096);
            std::snprintf(g_customMapsLua, sizeof(g_customMapsLua), "MAP:%d:%d", i, i * 7);
            EndListWrite();
        }
        done.store(true);
    });
    auto reader = [&] {
        for (int n = 0; n < rounds || !done.load(); ++n) {
            char* copy = T7Maps_CustomMapsLuaSnapshot();
            if (!copy) continue;
            int a = 0, b = 0;
            if (std::sscanf(copy, "MAP:%d:%d", &a, &b) != 2 || b != a * 7) ++failures;
            std::this_thread::yield();
            if (std::sscanf(copy, "MAP:%d:%d", &a, &b) != 2 || b != a * 7) ++failures;
            std::free(copy);
        }
    };
    std::thread first(reader), second(reader);
    writer.join(); first.join(); second.join();
    assert(failures.load() == 0);
    assert(g_listReaders.load() == 0);
    BeginListWrite(); EndListWrite();
    std::puts("PASS: empty and unterminated sources; 16000 concurrent writes; two immutable snapshot readers");
}
''';
    with tempfile.TemporaryDirectory() as folder:
        cpp = Path(folder) / 'check.cpp'
        exe = Path(folder) / 'check'
        cpp.write_text(harness)
        subprocess.run(['g++', '-std=c++20', '-O2', '-pthread', '-Wall', '-Wextra', '-Werror', str(cpp), '-o', str(exe)], check=True)
        subprocess.run([str(exe)], check=True, timeout=40)


if __name__ == '__main__':
    maps = Path(sys.argv[1] if len(sys.argv) > 1 else 'SPRX/t7_maps.cpp').read_text(encoding='utf-8')
    lua = Path(sys.argv[2] if len(sys.argv) > 2 else 'SPRX/t7_lua.cpp').read_text(encoding='utf-8')
    check(maps, lua)
