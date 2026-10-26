"""Build manifests and dependency audits shared by CI and the release publisher."""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import zipfile

COMPONENTS = {"porter": ("PS4.FF.Porter.exe", b"MZ"),
              "sprx": ("BO3-Customs.sprx", b"\x4f\x15\x3d\x1d")}


def digest(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def check_identity(sha, run_id, attempt):
    if not re.fullmatch(r"[0-9a-f]{40}", sha) or int(run_id) < 1 or int(attempt) < 1:
        raise ValueError("Invalid source/run identity")


def check_target_report(report, binary_sha):
    if not isinstance(report, dict) or report.get("sprx_sha256") != binary_sha:
        raise ValueError("SPRX target inspection belongs to a different binary")
    if (report.get("format") != "PS4 SCE dynamic library / GoldHEN FSELF" or
            report.get("machine") != "x86-64" or report.get("payload_segments_verified") != 4 or
            report.get("console_validated") is not False or report.get("sony_signed") is not False):
        raise ValueError("Missing SCE module/container validation")
    required = {"_init", "_fini", "module_start", "module_stop", "plugin_load", "plugin_unload", "g_pluginName"}
    if set(report.get("exports", [])) != required or not report.get("init") or not report.get("fini"):
        raise ValueError("Missing GoldHEN entrypoints/exports")
    if report.get("configuration") not in ("Debug", "Release"):
        raise ValueError("Unknown native configuration")


def package(component, binary, destination):
    root = Path(__file__).resolve().parents[2]
    sha = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
    run_id, attempt = int(os.environ["GITHUB_RUN_ID"]), int(os.environ["GITHUB_RUN_ATTEMPT"])
    check_identity(sha, run_id, attempt)
    if sha != os.environ["GITHUB_SHA"]:
        raise ValueError("Checkout differs from the build event")
    name, magic = COMPONENTS[component]
    binary = Path(binary)
    with binary.open("rb") as stream:
        if binary.stat().st_size < 256 or stream.read(len(magic)) != magic:
            raise ValueError(f"Invalid {component} output: {binary.name}")
    destination = Path(destination)
    destination.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(binary, destination / name)
    shutil.copyfile(root / "LICENSE", destination / "LICENSE")
    files = {p.name: {"sha256": digest(p), "size": p.stat().st_size}
             for p in (destination / name, destination / "LICENSE")}
    manifest = {"schema": 1, "component": component, "commit": sha,
                "run_id": run_id, "run_attempt": attempt, "files": files,
                "console_validated": False}
    if component == "sprx":
        manifest["configuration"] = os.environ.get("BO3_SPRX_CONFIGURATION", "Debug")
        report = json.loads(Path(os.environ["BO3_SPRX_VALIDATION_REPORT"]).read_text())
        check_target_report(report, files[name]["sha256"])
        if report["configuration"] != manifest["configuration"]:
            raise ValueError("Target report configuration differs from this build")
        manifest["target_validation"] = report
    (destination / f"manifest-{component}.json").write_text(json.dumps(manifest, indent=2) + "\n")


def unpack_component(archive, destination, component, sha, run_id, attempt):
    check_identity(sha, run_id, attempt)
    name, magic = COMPONENTS[component]
    manifest_name = f"manifest-{component}.json"
    expected = {name, "LICENSE", manifest_name}
    destination = Path(destination)
    destination.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(archive) as bundle:
        members = bundle.infolist()
        names = [m.filename for m in members if not m.is_dir()]
        if set(names) != expected or len(names) != len(expected):
            raise ValueError("Artifact has missing, duplicate or unexpected files")
        if any(m.file_size > 512 * 1024 * 1024 or (m.external_attr >> 16) & 0o170000 == 0o120000
               for m in members):
            raise ValueError("Artifact contains an oversized file or symlink")
        if bundle.getinfo(manifest_name).file_size > 32 * 1024:
            raise ValueError("Oversized build manifest")
        manifest = json.loads(bundle.read(manifest_name))
        identity = (manifest.get("schema"), manifest.get("component"), manifest.get("commit"),
                    manifest.get("run_id"), manifest.get("run_attempt"))
        if identity != (1, component, sha, int(run_id), int(attempt)):
            raise ValueError("Artifact belongs to a different commit, component or build attempt")
        if set(manifest.get("files", {})) != {name, "LICENSE"}:
            raise ValueError("Manifest file list is incomplete")
        for filename in expected:
            target = destination / filename
            with bundle.open(filename) as source, target.open("wb") as output:
                shutil.copyfileobj(source, output)
            if filename != manifest_name:
                recorded = manifest["files"][filename]
                if target.stat().st_size != recorded.get("size") or digest(target) != recorded.get("sha256"):
                    raise ValueError(f"Artifact checksum mismatch: {filename}")
        with (destination / name).open("rb") as stream:
            if (destination / name).stat().st_size < 256 or stream.read(len(magic)) != magic:
                raise ValueError("Artifact is not the expected binary format")
        if component == "sprx":
            report = manifest.get("target_validation")
            check_target_report(report, manifest["files"][name]["sha256"])
            if manifest.get("configuration") != report["configuration"]:
                raise ValueError("Target report configuration mismatch")
    return manifest


def audit_console(archive, lock):
    archive = Path(archive)
    if archive.stat().st_size != lock["size"] or digest(archive) != lock["sha256"]:
        raise ValueError("Console.zip differs from the reviewed dependency lock")
    required = {name.casefold() for name in lock["required_lua"]}
    found = set()
    lua = []
    seen = set()
    with zipfile.ZipFile(archive) as bundle:
        for member in bundle.infolist():
            name = member.filename.replace("\\", "/")
            path = PurePosixPath(name)
            if path.is_absolute() or ".." in path.parts or ":" in name:
                raise ValueError("Unsafe path in Console.zip")
            if member.is_dir():
                continue
            key = name.casefold().removeprefix("data/")
            if key in seen or (member.external_attr >> 16) & 0o170000 == 0o120000:
                raise ValueError("Duplicate file or symlink in Console.zip")
            seen.add(key)
            if key.endswith(".lua"):
                if not member.file_size or member.file_size > 8 * 1024 * 1024:
                    raise ValueError("Empty or oversized console Lua dependency")
                content = bundle.read(member)  # Read every Lua entry, checking decompression/CRC.
                lua.append({"path": name, "sha256": hashlib.sha256(content).hexdigest(), "size": len(content)})
                found.add(key)
        missing = sorted(required - found)
        if missing:
            raise ValueError("Missing console Lua: " + ", ".join(missing))
    return {"url": lock["url"], "sha256": lock["sha256"], "size": lock["size"],
            "lua_files": lua, "hks_execution_tested": False}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("component", choices=COMPONENTS)
    parser.add_argument("binary")
    parser.add_argument("destination")
    args = parser.parse_args()
    package(args.component, args.binary, args.destination)
