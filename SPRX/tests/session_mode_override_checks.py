#!/usr/bin/env python3
"""Verify the production session-mode guard's nested restoration (issue #3)."""
from pathlib import Path
import os
import subprocess
import tempfile

source = (Path(__file__).resolve().parents[1] / "t7_maps.cpp").read_text(encoding="utf-8")
start = source.index("    struct SessionModeOverride\n")
end = source.index("    };", start) + len("    };\n")
guard = source[start:end]

fixture = r'''
#include <cstdint>
#include <cstdio>

GUARD

int main() {
    uint32_t session = 0xA5A30003u;
    {
        SessionModeOverride zombies(&session, 0);
        if (session != 0xA5A30000u) return 1;
        {
            SessionModeOverride multiplayer(&session, 2);
            if (session != 0xA5A30002u) return 2;
        }
        if (session != 0xA5A30000u) return 3;
    }
    if (session != 0xA5A30003u) return 4;

    try {
        SessionModeOverride zombies(&session, 0);
        if (session != 0xA5A30000u) return 5;
        throw 1;
    } catch (int) {}
    if (session != 0xA5A30003u) return 6;
    std::puts("PASS: nested session transitions and early unwinding restore the exact previous mode");
    return 0;
}
'''.replace("GUARD", guard)

with tempfile.TemporaryDirectory(prefix="bo3-session-mode-") as directory:
    path = Path(directory) / "session.cpp"
    exe = Path(directory) / "session"
    path.write_text(fixture, encoding="utf-8")
    subprocess.run([os.environ.get("CXX", "g++"), "-std=c++20", "-Wall", "-Wextra", "-Werror",
                    "-fsanitize=address,undefined", str(path), "-o", str(exe)], check=True)
    subprocess.run([str(exe)], check=True)
