#!/usr/bin/env python3
"""Host regression for the production SPRX map-zone ownership predicate.

Extracts the real helper from t7_maps.cpp instead of copying its implementation.
No PS4 game files or PC BO3 installation are required.
"""
from pathlib import Path
import subprocess
import tempfile

source = Path(__file__).resolve().parents[1] / "t7_maps.cpp"
text = source.read_text(encoding="utf-8")
start = text.find("static bool ZoneFileMatchesPackage(")
assert start >= 0, "production zone-owner predicate missing"
open_brace = text.index("{", start)
depth = 0
end = None
for pos in range(open_brace, len(text)):
    if text[pos] == "{":
        depth += 1
    elif text[pos] == "}":
        depth -= 1
        if depth == 0:
            end = pos + 1
            break
assert end is not None
helper = text[start:end]
assert text.count("IsZoneFile(files[i]) && ZoneFileMatchesPackage(files[i], name)") == 2, "both zone and sound-bank scans must use predicate"

cases = [
    ("zm_alpha.ff", "zm_alpha", True),
    ("en_zm_alpha.ff", "zm_alpha", True),
    ("zm_alpha_patch.ff", "zm_alpha", True),
    ("en_zm_alpha.sabl", "zm_alpha", True),
    ("snd_zm_alpha_0.sabs", "zm_alpha", True),
    ("zm_alpha2.ff", "zm_alpha", False),
    ("en_zm_alphabeta.ff", "zm_alpha", False),
    ("zm_alpha2_patch.ff", "zm_alpha", False),
    ("xzm_alpha.ff", "zm_alpha", False),
    ("zm_alpha.ff", "zm_al", False),
    ("zm_alpha.ff", "zm_beta", False),
    ("zm_alpha_patch.ff", "zm_alpha_patch", True),
]
code = '#include <cstring>\n#include <cstdio>\n' + helper + '\nint main() {\n'
for index, (filename, package, expected) in enumerate(cases):
    code += f' if (ZoneFileMatchesPackage("{filename}", "{package}") != {str(expected).lower()}) return {index + 1};\n'
code += ' std::puts("PASS: 12 production map ownership cases"); return 0;\n}\n'
with tempfile.TemporaryDirectory() as directory:
    cpp = Path(directory) / "test.cpp"
    exe = Path(directory) / "test"
    cpp.write_text(code, encoding="utf-8")
    subprocess.run(["g++", "-std=c++20", "-Wall", "-Wextra", "-Werror", "-fsanitize=address,undefined", "-fno-omit-frame-pointer", str(cpp), "-o", str(exe)], check=True)
    subprocess.run([str(exe)], check=True)
