# SPRX host regressions

## Transactional mod-zone planning regression (issues #2 and #5)

Run `python3 SPRX/tests/mod_stack_transaction_checks.py` on a Linux host with
Python 3 and a C++20 compiler. The test compiles the production `AddModZones`
body against deterministic planning and state fixtures. It first confirms that
a mod request yielding zero loadable zones incorrectly overwrote the active
stack's file/zone metadata in the previous revision. It then checks empty-mode
requests, insufficient companion-record capacity, total capacity rejection,
first-load rejection, rejected append, partial append, and successful replacement.
Failed plans must leave the exact active stack, loaded names and script claims
unchanged. Successful partial appends must not expose files from unplanned mods.
The production fix temporarily snapshots active stack metadata and rolls it back
if no zones can be planned; allocation failure also retains the old stack.

These are host boundary tests, not validation of BO3's actual asset allocation,
script precaching, multiplayer interaction, or PS4 firmware behavior.

## Sequential mod-loading readiness

`python3 SPRX/tests/mod_load_readiness_checks.py` exercises the production
readiness gate for outstanding mod zones. Selecting another mod must defer
while any previously scheduled zone is absent or still loading, even if the
engine's global loading flag briefly reports no active zone. The check also
verifies both menu prefetch and explicit apply retry after deferral. It does
not reproduce or identify the cause of a console application crash.

## Bounded script-asset metadata (issues #2 and #5)

`ASAN_OPTIONS=detect_leaks=0 python3 SPRX/tests/script_asset_name_safety_checks.py`
extracts the actual GSC/CSC script-name parser and Lua raw-file registration
path. Before the repair, a four-byte unterminated name immediately before an
unreadable memory page crashed in `ScriptBase`'s unbounded `strlen`. The code
now validates each full script/Lua name with the existing bounded readable
C-string primitive before parsing or logging it. The zone and key/value asset
name comparisons use the same boundary check. The host regression covers valid
case-insensitive script names, valid Lua registrations, unterminated names,
overlong names and guard pages under ASan/UBSan. This fixes a proven runtime
memory-safety hazard in script registration; it does **not** prove that the
reported zombiebots script errors were caused by this mechanism.

## Versioned, bounded diagnostics

The SPRX version and diagnostic schema are defined in `SPRX/version.hpp`.
Debug logs start with the version, source revision and compiler build time.
As of `4.00.1-dev.5`, these identifiers occupy the same first line, so a
single missing queued log entry cannot separate the version from the build
timestamp. Diagnostic schema 3 also reports image-hook installation outcomes
and bounded thumbnail failure reasons.
Hosted native builds embed the Git revision and mark modified trees, while
manual builds fall back to their stamped build time. The startup notification
shows the SPRX version separately from the unchanged Console.zip release.

`python3 SPRX/tests/mod_diagnostics_checks.py` extracts the production
defer/resume trace helper and verifies that ten thousand waiting frames yield
one message, followed by a single resume message. The source checks also
require zone-dispatch begin/return markers and readiness counts. A dispatch
return only means the engine accepted the call, **not** that every asset has
finished streaming. Debug log collection is still asynchronous and cannot
guarantee a fatal exception will be recorded before the game exits.

## Custom thumbnail failure diagnosis

In `4.00.1-dev.6` private console logs, dozens of distinct maps failed PNG
parsing with `a chunk runs past the end of the file` even though image hooks
were installed. The shared failure path read a file length from OpenOrbis
v0.5.4's `sceKernelStat` structure, whose layout is not binary-compatible with
the PS4 kernel's `struct stat`. `4.00.1-dev.7` now determines the PNG byte
length through `sceKernelLseek` on the open descriptor, rewinds and fully reads
it, retaining the 48 MiB upper bound. It does not modify the SDK stat type:
changing that typedef in isolation would conflict with prebuilt SDK libraries.
`ASAN_OPTIONS=detect_leaks=0 python3 SPRX/tests/map_image_file_read_checks.py`
extracts the exact reader and reproduces the old truncated-read bug using an
intentionally incorrect stat length. It also covers one-byte partial reads,
empty/oversized files, seek failures, read errors, size changes and FD cleanup.
This confirms the host-side read correction; actual PS4/PS5 image display
remains hardware-unverified.

`ASAN_OPTIONS=detect_leaks=0 python3 SPRX/tests/map_icon_loading_checks.py`
tests retry-after-refresh, valid-image retention, registry-protected preview
lookup, and a maximum of 64 detailed image failures plus one suppression
notice. The runtime emits `[MapImages] hooks installed` or a specific hook
failure at startup. Failed custom PNGs report which step failed (path lookup,
file access, PNG parsing/decoding, game allocation, or image-buffer validation).
As of `4.00.1-dev.6`, custom maps missing their dedicated `previewimage.png`
and any valid `workshop.json` thumbnail use an available `loadingimage.png` as
their map-selection icon. Missing artwork or a transient GPU allocation failure
is retried after five seconds of process time if the menu asks for it again,
without repeatedly retrying every render; a map refresh also forces a retry.
Use `ASAN_OPTIONS=detect_leaks=0 python3 SPRX/tests/png_fixture_checks.py`
to confirm that the production PNG decoder accepts known-valid RGBA, RGB and
grayscale pixel fixtures. When all candidate art is missing or a PNG cannot be
decoded, the `$white` fallback is still used. Host regressions do **not** prove
that all thumbnails display correctly on BO3 1.33 hardware: pixel uploads,
image-hook offsets, and GPU visibility remain hardware-unverified. Use the
schema 3 `[MapImages]` lines and an offending PNG if a white icon persists.
Do not attribute the separate PS4 freeze to thumbnail loading without evidence.

## Database-thread XPAK pointer boundary checks

`ASAN_OPTIONS=detect_leaks=0 CXX=clang++-18 python3 SPRX/tests/xpak_pointer_safety_checks.py`
extracts the actual `SafeReadableCString` implementation and runs it against
invalid pointers, missing terminators, maximum-length names, and a string
ending immediately before a protected memory page. Static checks ensure the
XPAK detour validates both the requested XPAK name and the current zone name
before using them as strings. This avoids a known unsafe read path in the
hook; it does not establish the root cause of the private PS5 crash.

## Custom-map selection icons and dynamic-image cache

`ASAN_OPTIONS=detect_leaks=0 CXX=clang++-18 python3 SPRX/tests/map_icon_loading_checks.py`
compiles the production icon cache and preview path lookup. It checks that
missing image entries do not hammer the filesystem each UI draw, can retry
after a map registry refresh, and do not discard still-valid GPU images.
The preview filename is resolved while holding the map registry reader lock,
so a concurrent rescan cannot invalidate its storage before path resolution.
Actual console UI rendering and GPU texture lifetime still need hardware checks.

## Session mode and frontend reload checks (issues #3 and #4)

`python3 SPRX/tests/session_mode_override_checks.py` extracts the exact C++
scope guard from `LoadModeCommonForMods` and checks nested mode transitions,
unchanged upper session bits, and restoration during C++ stack unwinding.
It does not prove that game engine callbacks cannot observe the temporary mode.

`python3 SPRX/tests/frontend_reload_checks.py` compiles the production
`ReloadFrontend` function with a deterministic synchronous KillServer callback.
It covers not-ready and in-level no-ops, absent movie hooks, and 100 repeated
frontend reloads, preserving loaded-mod and script-claim metadata while clearing
transient UI state. The `4.00.1-dev.9` regression additionally makes KillServer
re-enter `ReloadFrontend`; the inner callback must preserve the outer call's
quiet-movie scope. The old boolean assignment cleared that scope too soon.
It cannot simulate a real BO3 server shutdown or HKS reload.

`python3 SPRX/tests/mod_ui_claim_pruning_checks.py SPRX/t7_maps.cpp` extracts
the production UI registry pruner and checks that unloading a mod removes its
Lua file registrations **and** its UI/DLL classification. Retaining the latter
forever could cause incorrect behavior if the same mod folder was replaced by
a version without those assets. A surviving mod's registrations must not be
removed; repeatedly pruning is idempotent and names match case-insensitively.

`python3 SPRX/tests/script_claim_lifecycle_checks.py` extracts the production
script claim registration, partial-unload pruning and full reset routines. It
checks two mods sharing GSC/CSC names, independent script ownership, reload of
an overlapping mod, and preservation of interned names while game assets may
still point to them. This only models ordered asset callbacks, not the engine's
asynchronous linker; reversing callback order and PS4/PS5 runtime behavior
remain separate acceptance checks for issues #2 and #5.

## Detour host regression checks

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
