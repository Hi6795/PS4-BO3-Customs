"""Clean hosted PS4 build; fetch only checksum-pinned public upstream tools."""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import shutil
import subprocess
import tarfile
import tempfile
from urllib.request import Request, urlopen
import xml.etree.ElementTree as ET

from verify_sprx import verify

ROOT = Path(__file__).resolve().parents[2]
SOURCES = ("libjbc.cpp", "main.cpp", "platform.cpp", "Detour.cpp", "HDE64.c", "png.cpp", "t7_maps.cpp",
           "t7_mapimages.cpp", "t7_lua.cpp", "t7_kbm.cpp", "t7_log.cpp", "t7_config.cpp", "t7_share.cpp", "t7_builtins.cpp")


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def download(item, destination):
    if not destination.exists():
        temporary = destination.with_suffix(".part")
        request = Request(item["url"], headers={"User-Agent": "BO3-Auto-Build"})
        with urlopen(request, timeout=120) as response, temporary.open("wb") as output:
            shutil.copyfileobj(response, output)
        if sha256(temporary) != item["sha256"]:
            temporary.unlink()
            raise ValueError("Downloaded dependency differs from the reviewed lock")
        temporary.replace(destination)
    if sha256(destination) != item["sha256"] or ("size" in item and destination.stat().st_size != item["size"]):
        raise ValueError(f"Dependency checksum/size changed: {destination.name}")


def build(configuration, tool_directory):
    lock = json.loads((ROOT / ".github/openorbis-lock.json").read_text())
    cache = Path(os.environ.get("BO3_TOOLCHAIN_CACHE", ROOT / "build/toolchain-cache"))
    cache.mkdir(parents=True, exist_ok=True)
    for key in ("sdk", "goldhen_crt", "goldhen_license"):
        download(lock[key], cache / key)
    output = ROOT / "build/openorbis"
    if output.exists():
        shutil.rmtree(output)
    output.mkdir(parents=True)
    sdk = output / "sdk"
    sdk.mkdir()
    # Extract only necessary regular files. Nothing from user attachments is
    # uploaded or needed by this pipeline, and archive links are not followed.
    with tarfile.open(cache / "sdk", "r:gz") as archive:
        for member in archive:
            path = PurePosixPath(member.name)
            if path.is_absolute() or ".." in path.parts or "\\" in member.name:
                raise ValueError("Unsafe SDK archive path")
            if not member.isfile() or path.parts[:2] != ("OpenOrbis", "PS4Toolchain"):
                continue
            relative = PurePosixPath(*path.parts[2:])
            if relative.parts[0] not in ("include", "lib") and str(relative) not in (
                    "link.x", "LICENSE", "bin/linux/create-fself", "bin/linux/readoelf"):
                continue
            target = sdk / str(relative)
            target.parent.mkdir(parents=True, exist_ok=True)
            with archive.extractfile(member) as source, target.open("wb") as destination:
                shutil.copyfileobj(source, destination)
    packager, inspector = tool_directory / "create-fself", tool_directory / "readoelf"
    for tool, key in ((packager, "packager_sha256"), (inspector, "inspector_sha256")):
        original = sdk / "bin/linux" / tool.name
        if sha256(original) != lock[key]:
            raise ValueError("Target tool differs from the reviewed lock")
        # Workspaces may be mounted without executable-file support.
        shutil.copyfile(original, tool)
        tool.chmod(0o755)
    environment = dict(os.environ, OO_PS4_TOOLCHAIN=str(sdk))
    cc, cxx, linker = [os.environ.get(key, default) for key, default in
                       (("CC", "clang-18"), ("CXX", "clang++-18"), ("LD", "ld.lld-18"))]
    versions = {tool: subprocess.check_output([tool, "--version"], env=environment, text=True).splitlines()[0]
                for tool in (cc, cxx, linker)}
    source = ROOT / "SPRX"
    project = ET.parse(source / "BO3-Customs.vcxproj")
    inventory = {node.attrib["Include"] for node in project.iter() if node.tag.endswith("ClCompile") and "Include" in node.attrib}
    if inventory != set(SOURCES):
        raise ValueError("Native source inventory changed; update the hosted build too")
    common = ["--target=x86_64-pc-freebsd12-elf", "-fPIC", "-funwind-tables", "-Oz", "-D_POSIX_C_SOURCE=200809L",
              "-DBO3_OPENORBIS=1", "-D_DEBUG" if configuration == "Debug" else "-DNDEBUG", "-isysroot", str(sdk),
              "-isystem", str(sdk / "include"), "-I", str(source / "openorbis/include"), "-I", str(source)]
    objects = output / "objects"
    objects.mkdir()

    def run(command):
        print("Building:", " ".join(str(x) for x in command), flush=True)
        result = subprocess.run([str(x) for x in command], env=environment, stdout=subprocess.PIPE,
                                stderr=subprocess.STDOUT, text=True, cwd=ROOT)
        with (output / "build.log").open("a") as stream:
            stream.write(" ".join(str(x) for x in command) + "\n" + result.stdout)
        print(result.stdout, end="", flush=True)
        result.check_returncode()

    run([cc, *common, "-x", "c", "-c", cache / "goldhen_crt", "-o", objects / "crtprx.o"])
    for name in SOURCES:
        flags = [] if name.endswith(".c") else ["-std=c++20", "-fdeclspec", "-fno-exceptions", "-fno-rtti",
                                                "-isystem", str(sdk / "include/c++/v1")]
        run([cc if name.endswith(".c") else cxx, *common, *flags, "-c", source / name, "-o", objects / (name + ".o")])
    libraries = ["c++", "c++abi", "SceLibcInternal", "kernel", "SceSysmodule", "SceNet", "ScePad", "SceMouse",
                 "SceUserService", "SceSystemService"]
    if configuration == "Debug":
        # This derived .so describes real Unity imports. Its code never enters
        # the plugin; only the two NIDs from the existing ORBIS archive do.
        run([cc, *common, "-c", source / "openorbis/unity_imports.c", "-o", output / "unity-imports.o"])
        run([linker, "-shared", "-soname", "libkernel_unity.so", output / "unity-imports.o",
             "-o", sdk / "lib/libkernel_unity.so"])
        libraries.insert(0, "kernel_unity")
    elf, oelf, sprx = [output / ("BO3-Customs." + ext) for ext in ("elf", "oelf", "sprx")]
    run([linker, *sorted(objects.glob("*.o")), "-o", elf, "-m", "elf_x86_64", "-pie", "--script", sdk / "link.x",
         "-e", "_init", "--eh-frame-hdr", "-L" + str(sdk / "lib"), *["-l" + name for name in libraries]])
    run([packager, "-in=" + str(elf), "-out=" + str(oelf), "-lib=" + str(sprx),
         "--paid", "0x3800000000000011", "--libname", "BO3-Customs", "--sdkver", "0x4508101"])
    with (output / "readoelf.txt").open("w") as stream:
        subprocess.run([str(inspector), "--all", str(oelf)], env=environment, stdout=stream, check=True)
    report = verify(elf, oelf, sprx, configuration)
    report.update(dependencies=lock, compiler_versions=versions, translation_units=len(SOURCES),
                  third_party_notices={"GoldHEN": (cache / "goldhen_license").read_text(),
                                       "OpenOrbis": (sdk / "LICENSE").read_text()})
    (output / "target-validation.json").write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report, indent=2))
    return sprx


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--configuration", choices=("Debug", "Release"), default=os.environ.get("BO3_SPRX_CONFIGURATION", "Debug"))
    parser.add_argument("--package", action="store_true", help="Use actual GitHub run identity to prepare its artifact")
    args = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix="bo3-target-tools-") as tool_directory:
        binary = build(args.configuration, Path(tool_directory))
    if args.package:
        from build_support import package
        os.environ["BO3_SPRX_CONFIGURATION"] = args.configuration
        os.environ["BO3_SPRX_VALIDATION_REPORT"] = str(binary.parent / "target-validation.json")
        package("sprx", binary, ROOT / "build/artifact-sprx")
