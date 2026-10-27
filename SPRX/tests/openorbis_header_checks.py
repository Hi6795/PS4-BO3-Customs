"""Check actual target headers and every project translation unit; no linking/downloads."""
import argparse
import os
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("sdk", type=Path, help="Existing extracted OpenOrbis PS4 toolchain")
    args = parser.parse_args()
    sdk = args.sdk.resolve()
    source = ROOT / "SPRX"
    if not (sdk / "include/orbis/libkernel.h").is_file():
        parser.error("SDK does not contain include/orbis/libkernel.h")
    cc = os.environ.get("CC", "clang-18")
    cxx = os.environ.get("CXX", "clang++-18")
    common = ["--target=x86_64-pc-freebsd12-elf", "-fPIC", "-Oz",
              "-D_POSIX_C_SOURCE=200809L", "-DBO3_OPENORBIS=1", "-D_DEBUG",
              "-isysroot", str(sdk), "-isystem", str(sdk / "include"),
              "-I", str(source / "openorbis/include"), "-I", str(source)]
    cpp = ["-std=c++20", "-fdeclspec", "-fno-exceptions", "-fno-rtti",
           "-isystem", str(sdk / "include/c++/v1")]

    def run(command):
        result = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        if result.returncode:
            print(result.stdout)
            result.check_returncode()

    project = ET.parse(source / "BO3-Customs.vcxproj")
    names = sorted({n.attrib["Include"] for n in project.iter()
                    if n.tag.endswith("ClCompile") and "Include" in n.attrib})
    for name in names:
        is_c = name.endswith(".c")
        run([cc if is_c else cxx, *common, *([] if is_c else cpp),
             "-fsyntax-only", str(source / name)])

    with tempfile.TemporaryDirectory(prefix="bo3-stat-abi-") as tmp:
        probe = Path(tmp) / "probe.cpp"
        assembly = Path(tmp) / "probe.s"
        probe.write_text('#include "headers.hpp"\n'
                         'static_assert(sizeof(SceKernelStat) == 120);\n'
                         'static_assert(offsetof(SceKernelStat, st_size) == 72);\n'
                         'int probe(const char* path, int fd) { SceKernelStat s{};\n'
                         'return sceKernelStat(path, &s) + sceKernelFstat(fd, &s); }\n')
        run([cxx, *common, *cpp, "-S", str(probe), "-o", str(assembly)])
        text = assembly.read_text()
        for symbol in ("sceKernelStat", "sceKernelFstat"):
            assert symbol in text and "bo3_" + symbol not in text, symbol

    print(f"All {len(names)} PS4 translation units passed target syntax checks.")
    print("Native stat record: size 120, st_size offset 72; stat/fstat import names preserved.")


if __name__ == "__main__":
    main()
