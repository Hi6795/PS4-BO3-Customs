#!/usr/bin/env python3
"""Exercise bounded mod-load diagnostics and check SPRX build identification."""
from pathlib import Path
import os
import re
import subprocess
import tempfile


root = Path(__file__).resolve().parents[1]
maps = (root / "t7_maps.cpp").read_text(encoding="utf-8")
log = (root / "t7_log.cpp").read_text(encoding="utf-8")
main = (root / "main.cpp").read_text(encoding="utf-8")
version = (root / "version.hpp").read_text(encoding="utf-8")
builder = (root.parent / ".github/scripts/build_sprx.py").read_text(encoding="utf-8")
project = (root / "BO3-Customs.vcxproj").read_text(encoding="utf-8")

assert re.search(r'#define BO3_CUSTOMS_SPRX_VERSION "4\.00\.1-dev\.\d+"', version)
assert '#define BO3_CUSTOMS_DIAGNOSTIC_SCHEMA 3' in version
assert '#include "version.hpp"' in log
assert '#include "version.hpp"' in main
assert 'BO3_CUSTOMS_SPRX_VERSION' in log and 'BO3_CUSTOMS_SOURCE_REV' in log
assert 'BO3-Customs debug log (Debug build of %s %s) | SPRX version %s' in log
assert 'BO3_CUSTOMS_DIAGNOSTIC_SCHEMA' in log
assert 'BO3_CUSTOMS_SPRX_VERSION' in main
assert '<ClInclude Include="version.hpp" />' in project
assert '-DBO3_CUSTOMS_SOURCE_REV=' in builder
assert 'diagnostic_schema=int(schema.group(1))' in builder

begin = maps.index("static void TraceModWait(")
end = maps.index("static const char* RecordZoneName(", begin)
trace = maps[begin:end]

for function, state in (("PrefetchMods", "g_prefetchDeferred"), ("ApplyModsNow", "g_applyDeferred")):
    start = maps.index(f"static void {function}()")
    stop = maps.index("static ", start + 1)
    body = maps[start:stop]
    assert f"TraceModWait({state}," in body, f"{function} is missing deferral transition diagnostics"
    assert body.index(f"TraceModWait({state},") < body.index("if (engineBusy || trackedPending)")

assert '[ModLoad] zone dispatch' in maps
assert 'dispatch returned' in maps
assert '[ModLoad] mode common' in maps
assert '[ModLoad] zones ready' in maps
assert '[ModLoad] append' in maps

fixture = r'''
#include <cstdarg>
#include <cstdio>
#include <string>
#include <vector>

static std::vector<std::string> lines;
static int g_waitZoneCount = 2;
static int g_loadedCount = 1;
static void T7Log_Write(const char* format, ...) {
    char buffer[512];
    va_list args;
    va_start(args, format);
    std::vsnprintf(buffer, sizeof(buffer), format, args);
    va_end(args);
    lines.emplace_back(buffer);
}

PRODUCTION

int main() {
    bool prefetch = false;
    bool apply = false;
    TraceModWait(prefetch, false, false, "prefetch");
    if (!lines.empty()) return 1;

    TraceModWait(prefetch, true, false, "prefetch");
    if (!prefetch || lines.size() != 1 || lines[0].find("deferred") == std::string::npos) return 2;
    for (int i = 0; i < 10000; ++i) TraceModWait(prefetch, false, true, "prefetch");
    if (lines.size() != 1) return 3;

    TraceModWait(apply, false, true, "apply");
    if (!apply || lines.size() != 2 || lines[1].find("apply") == std::string::npos) return 4;
    TraceModWait(prefetch, false, false, "prefetch");
    if (prefetch || lines.size() != 3 || lines[2].find("resumed") == std::string::npos) return 5;
    TraceModWait(apply, false, false, "apply");
    if (apply || lines.size() != 4 || lines[3].find("resumed") == std::string::npos) return 6;
    TraceModWait(apply, false, false, "apply");
    if (lines.size() != 4) return 7;

    std::puts("PASS: one diagnostic per defer/resume transition across 10000 retry frames");
}
'''.replace("PRODUCTION", trace)

with tempfile.TemporaryDirectory(prefix="bo3-mod-diagnostics-") as directory:
    cpp = Path(directory) / "diagnostics.cpp"
    binary = Path(directory) / "diagnostics"
    cpp.write_text(fixture, encoding="utf-8")
    subprocess.run([os.environ.get("CXX", "g++"), "-std=c++20", "-Wall", "-Wextra", "-Werror",
                    "-fsanitize=address,undefined", str(cpp), "-o", str(binary)], check=True)
    subprocess.run([str(binary)], check=True)
