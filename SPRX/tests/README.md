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
