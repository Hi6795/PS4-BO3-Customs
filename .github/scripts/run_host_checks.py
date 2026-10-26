"""Run every committed SPRX host regression; fail on unregistered new checks."""
import os
from pathlib import Path
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[2]
ARGUMENTS = {
    "custom_maps_snapshot_checks.py": ["SPRX/t7_maps.cpp", "SPRX/t7_lua.cpp"],
    "map_table_snapshot_checks.py": ["SPRX/t7_maps.cpp"],
    "map_registry_capacity_checks.py": ["SPRX/t7_maps.cpp"],
    "mod_ui_claim_pruning_checks.py": ["SPRX/t7_maps.cpp"],
    "mod_stack_transaction_checks.py": [],
    "mod_zone_capacity_checks.py": ["SPRX/t7_maps.cpp"],
    "session_mode_override_checks.py": [],
    "frontend_reload_checks.py": [],
    "lua_tick_checks.py": ["SPRX/t7_lua.cpp"],
    "lua_lobby_map_checks.py": ["SPRX/t7_lua.cpp"],
    "lua_native_probe_checks.py": ["SPRX/t7_lua.cpp"],
    "file_open_snapshot_checks.py": [], "map_directory_capacity_checks.py": [],
    "map_file_ownership_checks.py": [], "map_uninstall_directory_checks.py": [],
    "run_detour_checks.py": [],
    "triage_runtime_errors.py": ["--self-test"],
}


def main():
    tests = ROOT / "SPRX/tests"
    scripts = {p.name for p in tests.glob("*.py")}
    if scripts != set(ARGUMENTS):
        raise SystemExit(f"Update CI test inventory: new={scripts - set(ARGUMENTS)}, missing={set(ARGUMENTS) - scripts}")
    failures = []
    environment = dict(os.environ, PYTHONDONTWRITEBYTECODE="1")
    for name, arguments in ARGUMENTS.items():
        print(f"::group::{name}", flush=True)
        result = subprocess.run([sys.executable, str(tests / name), *arguments],
                                cwd=ROOT, env=environment, timeout=180)
        if result.returncode:
            failures.append(name)
        print("::endgroup::", flush=True)
    cpp = sorted(tests.glob("*_tests.cpp"))
    with tempfile.TemporaryDirectory(prefix="bo3-host-") as directory:
        for source in cpp:
            binary = Path(directory) / source.stem
            print(f"::group::{source.name}", flush=True)
            build = subprocess.run(["g++", "-std=c++20", "-Wall", "-Wextra", "-Werror",
                                    "-O1", "-pthread", str(source), "-o", str(binary)], timeout=120)
            if build.returncode or subprocess.run([str(binary)], timeout=120).returncode:
                failures.append(source.name)
            print("::endgroup::", flush=True)
    print(f"{len(ARGUMENTS) + len(cpp)} regression programs; failures: {failures}")
    return bool(failures)


if __name__ == "__main__":
    raise SystemExit(main())
