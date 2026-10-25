#!/usr/bin/env python3
"""Host regression for the SPRX-owned map-file registry; not a PS4 test."""
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import sys

source = Path(sys.argv[1]).read_text(encoding='utf-8')
limit_match = re.search(r'constexpr int\s+kMaxFiles\s*=\s*(\d+)\s*;', source)
assert limit_match, 'missing kMaxFiles'
limit = int(limit_match.group(1))
start = source.index('static void AddFile(')
brace = source.index('{', start)
depth = 0
end = None
for pos in range(brace, len(source)):
    if source[pos] == '{': depth += 1
    elif source[pos] == '}':
        depth -= 1
        if depth == 0:
            end = pos + 1
            break
assert end is not None
function = source[start:end]
assert '++g_fileOverflow' in function, 'missing overflow accounting'
assert re.search(r'g_fileOverflow\s*=\s*0\s*;', source), 'overflow count must reset on scan'
assert 'file registry full' in source, 'missing overflow diagnostic'
program = r'''
#include <cstdio>
#include <cstring>
#include <cstdlib>
#include <string>
struct File { char rel[128]; char path[224]; int package; };
constexpr int kMaxFiles = LIMIT;
static File g_files[kMaxFiles];
static int g_fileCount = 0;
static int g_fileOverflow = 0;
FUNCTION
int main() {
  const char* folder = "/data/BO3-Customs/usermaps/zm_test/zone";
  for (int i = 0; i < 1601; ++i) {
    char rel[80]; std::snprintf(rel, sizeof(rel), "zm_test_%04d.ff", i);
    AddFile(rel, folder, 0);
  }
  if (g_fileCount != 1601 || g_fileOverflow) return 1;
  AddFile("zm_test_0000.ff", folder, 0);
  if (g_fileCount != 1601 || g_fileOverflow) return 2;
  if (std::strcmp(g_files[1600].path, "/data/BO3-Customs/usermaps/zm_test/zone/zm_test_1600.ff")) return 3;
  for (int i = 1601; i < kMaxFiles; ++i) {
    char rel[80]; std::snprintf(rel, sizeof(rel), "zm_test_%04d.ff", i);
    AddFile(rel, folder, 0);
  }
  if (g_fileCount != kMaxFiles || g_fileOverflow) return 4;
  AddFile("zm_test_overflow.ff", folder, 0);
  AddFile("zm_test_overflow2.ff", folder, 0);
  if (g_fileCount != kMaxFiles || g_fileOverflow != 2) return 5;
  return 0;
}
'''.replace('LIMIT', str(limit)).replace('FUNCTION', function)
compilers = [x for x in ('g++', 'clang++') if shutil.which(x)]
if not compilers:
    raise SystemExit('No C++ compiler available')
with tempfile.TemporaryDirectory() as d:
    cpp = Path(d) / 'check.cpp'
    cpp.write_text(program, encoding='utf-8')
    for compiler in compilers:
        exe = Path(d) / compiler
        subprocess.run([compiler, '-std=c++20', '-O1', '-g', '-Wall', '-Wextra', '-Werror', '-fsanitize=address,undefined', '-fno-omit-frame-pointer', str(cpp), '-o', str(exe)], check=True)
        subprocess.run([str(exe)], check=True)
        print(f'{compiler}: PASS (1601 unique entries, duplicate, full capacity, two overflows)')
print('PASS: source overflow reset and summary diagnostic present')
