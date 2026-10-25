#!/usr/bin/env python3
"""Compile the real detour and decoder against Linux syscall adapters."""
import os
from pathlib import Path
import platform
import shlex
import subprocess
import tempfile


def main():
    if platform.system() != "Linux" or platform.machine().lower() not in ("x86_64", "amd64"):
        raise SystemExit("These executable detour checks require x86-64 Linux.")
    tests = Path(__file__).resolve().parent
    source = tests.parent
    with tempfile.TemporaryDirectory(prefix="bo3-detour-checks-") as directory:
        work = Path(directory)
        # Upstream HDE64.c uses lowercase names on a case-insensitive SDK host.
        (work / "hde64.h").write_text('#include "HDE64.h"\n')
        (work / "table64.h").write_text('#include "Table64.h"\n')
        binary = work / "detour_checks"
        command = shlex.split(os.environ.get("CXX", "c++")) + [
            "-std=c++17", "-Wall", "-Wextra", "-O1", "-g",
            "-I", str(tests / "host"), "-I", str(work), "-I", str(source),
            str(source / "Detour.cpp"), str(source / "HDE64.c"),
            str(tests / "detour_checks.cpp"), "-o", str(binary),
        ]
        subprocess.run(command, check=True)
        return subprocess.run([str(binary)], check=False, cwd=work).returncode


if __name__ == "__main__":
    raise SystemExit(main())
