# Detour host regression checks

Run on x86-64 Linux with Python 3 and a C++17 compiler:

```sh
python3 SPRX/tests/run_detour_checks.py
```

The runner compiles the production `Detour.cpp` and `HDE64.c`. Only the four
kernel calls used by the detour are adapted for the host. `mmap`, `mprotect`
and `munmap` use real Linux pages; injected protection failures leave the
target read/execute only. Each check runs in a child process so a write fault
is reported as a failed check. Temporary build outputs are removed on exit.

The checks execute an original function, its replacement and the relocated
trampoline. They cover successful attachment/detachment, duplicate attachment,
denied writes to both trampoline jumps, a denied target write followed by a
successful retry, failed restoration followed by a successful retry, and
invalid inputs. A failed restore must preserve the installed detour and its
trampoline allocation; releasing either would leave the active hook dangling.

Before the protection-result checks were added, the same suite had four
failures out of six: the denied target write and denied restore both received
SIGSEGV, and the two denied trampoline writes were incorrectly accepted.

These checks establish host behavior at the syscall boundary. They do not
build a PS4 plugin or establish console, firmware, game-region or mod
compatibility. The source fix is not a verified resolution of the reported
BO1 Multiplayer image-pool exhaustion or UI/Lua errors. Test the eventual
console candidate on BO3 1.33, including ordinary loading, refused hook writes,
cleanup and retry, before treating it as console validated.

## Lua menu tick host regression

On Linux with Python 3 and the `liblua5.4` shared library installed:

```sh
python3 SPRX/tests/lua_tick_checks.py SPRX/t7_lua.cpp
```

This extracts the **actual embedded `k_tickLua` C++ string** and executes it in
host Lua 5.4. The test verifies that a failing `BO3CustomsTick` does not prevent
the lobby tick from running, that repeated failures are logged once, that a
successful tick re-arms diagnostics, and that missing callbacks/log sinks are
handled. The original unguarded tick fails the test with the intentional Lua
error. This check does not execute BO3's HKS VM, load any game mod, or validate
the PS4 SPRX. In particular it does not prove that a mod's gameplay scripts
(such as Offline & Unlimited Gobblegum) function correctly.

## Lua lobby map-wrapper host regression

On Linux with Python 3 and the `liblua5.4` shared library installed:

```sh
python3 SPRX/tests/lua_lobby_map_checks.py SPRX/t7_lua.cpp
```

This extracts the embedded `k_lobbyParityLua` string and exercises the
`GameLobbySetMap` and `IsMapValid` wrappers. It covers Engine methods
registered after the first `require`, numeric UGC map IDs, ordinary map
passthrough, repeated module loads, replaced methods, and recreated Engine
tables. The previous one-shot `watched.maps` flag prevented late or replaced
methods from receiving the wrappers. The host regression fails against that
implementation.

This is an isolated host Lua 5.4 compatibility test, not BO3's HKS runtime or
a PS4 game launch. It does not establish that Reaper Collection's map crashes,
menu errors, or unrelated conversion problems are resolved.

## Lua LUI native-probe host regression

On Linux with Python 3 and the `liblua5.4` shared library installed:

```sh
python3 SPRX/tests/lua_native_probe_checks.py SPRX/t7_lua.cpp
```

This extracts the production `patchLui` function from embedded
`k_lobbyParityLua` and checks retry after a throwing or nil-returning LUI
constructor, class recreation, constructor replacement, and idempotency.
Previously, `nativeDone[class]` was set before a successful probe and keyed
only by class name, permanently suppressing these retries.

The isolated original fixture fails the retry assertion; the patched fixture
passes. These host Lua 5.4 checks do **not** validate BO3 HKS, the complete
SPRX build, any console firmware, or the unresolved controller-settings
UI Error 9756. Console.zip and Tool/Porter are unchanged by this fix.

## Map-table Lua snapshot host regression

On Linux with Python 3 and a C++20 compiler:

```sh
python3 SPRX/tests/map_table_snapshot_checks.py SPRX/t7_maps.cpp
```

The runner extracts the production `ListReader`, writer barrier, and
`SnapshotMapsTableLua` helper from `t7_maps.cpp`, compiles them into a host
fixture, and runs two readers against 16,000 map-list rewrites. It also checks
empty and unterminated buffers, verifies that the map-table hook executes Lua
using the copied source, and ensures the reader lock is released before any
Lua callback can reenter the map-list APIs. Previously `MapsTable_h` passed a
shared buffer directly to Lua while `T7Maps_Refresh` could rewrite it.

This validates only source-level snapshot consistency and host concurrency.
It does **not** prove issue #43's 42-map launch error is fixed, run BO3 HKS,
compile the full SPRX, or test PS4 BO3 1.33. The issue's two attached runtime
logs were inaccessible when this change was prepared.

## Custom-map Lua injection snapshot host regression

On Linux with Python 3 and a C++20 compiler:

```sh
python3 SPRX/tests/custom_maps_snapshot_checks.py SPRX/t7_maps.cpp SPRX/t7_lua.cpp
```

The test extracts the production map-list reader/writer barrier and the new
caller-owned snapshot helper. It checks empty and unterminated input, and two
readers against 16,000 concurrent writes. All four custom-map Lua injection
sites must execute their own immutable copy and release it after HKS returns.
Previously, `T7Maps_CustomMapsLua()` exposed a shared buffer without holding
the list-reader guard during Lua compilation and execution.

This closes a separately verified source-level race; it does not establish the
cause of issue #43's 42-map threshold. No full SPRX build, HKS or PS4 hardware
validation is implied. Console.zip, its Lua files, and Tool/Porter are unchanged.

## Per-worker mod-load attribution regression

The mod loader uses `ModLoadContexts` to keep `LoadXFile` zone attribution and
plain-zone recursion guards isolated per engine thread. The host regression
checks nested restoration, concurrent workers, and fail-closed slot exhaustion.

```sh
g++ -std=c++20 -Wall -Wextra -Wpedantic -Werror -O2 -pthread \
  SPRX/tests/mod_load_context_tests.cpp -o /tmp/bo3_mod_load_context_tests
/tmp/bo3_mod_load_context_tests
```

This regression checks the exact production header. It does not verify that
two-mod crashes, GSC runtime state, or asset ownership work correctly on
BO3 1.33 hardware. The related draft PR #1 remains unmerged into main.

## Map-file registry capacity regression

Run `python3 SPRX/tests/map_registry_capacity_checks.py SPRX/t7_maps.cpp` on an x86-64
Linux host with Python 3 and GCC or Clang. This extracts the production
`AddFile` function, checks that more than 1,536 distinct files can be
registered without truncation, checks duplicate handling, and verifies
capacity exhaustion is counted. The source check also requires a per-scan
counter reset and a single diagnostic for overflow. The original 1,536-file
registry cannot pass the 1,601-file case. This is an SPRX-owned file index,
not a change to BO3 engine zone slots or memory pools. Host checks do not
establish that issue #43's 42-map launch failure is resolved; that requires
complete runtime logs and PS4 BO3 1.33 hardware validation.
