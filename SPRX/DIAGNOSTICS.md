# Script floods and mode transitions

These diagnostics address the logging gaps in issues #1 and #2. They are enabled
in Debug builds. They do not suppress script exceptions, bypass clientfield
validation, change zone priorities, unload mods or write player profiles.

## Distinct script errors

The old shared 200-line limit could be exhausted by one repeated GSC failure,
hiding later GSC failures and all CSC failures. The replacement keeps 64 distinct
error keys for each of GSC, CSC and unexpected VM instances, using fixed storage
under 8 KiB. A key contains the code position and hashes of the bounded message
and dialog text. Code positions are only compared within a level.

The first occurrence is logged immediately. Repeats are logged at occurrences
2, 4, 8, 16 and so on. This preserves frequency information without scanning the
script asset table and writing a line for every repeat. Distinct errors arriving
after a repeat flood still receive their own first-occurrence line. Each VM has
an independent limit. Overflow and diagnostic-lock contention are counted and
reported explicitly; this is bounded sampling, not a complete event archive.
Messages are limited to 191 characters, so errors differing only beyond that
prefix can share a key. The existing global runtime-error total still counts
every invocation, including those omitted from detailed diagnostics.

`Com_Error` and the next level boundary report per-VM totals, retained distinct
errors, overflow and contention. A level boundary starts a fresh table. The
native error handler receives every original integer/floating argument and its
return value is returned unchanged, including when diagnostics are contended.

## Mode and UI ownership

`[Transition]` lines capture mode-common/frontend zone requests and the before
and after states of `LoadLevelZones`, including unchanged mod lists. They report
the session mode, loaded common, selected/loaded mod names, base mod-zone mask,
common-pool ordering and pending reload/wait counts. The mod-zone mask describes
the four base zone names; the selected/loaded list identifies stacked mods.

UI snapshots use the existing nonblocking UI try-lock and never execute Lua.
They show readiness, current UI state, utility/mouse owner states and forced
injection. `observed-generation` counts observed state-pointer changes; it is
not an allocation-generation guarantee and cannot detect reuse of the same
address between observations. A busy UI produces an explicit skipped snapshot.
The level name is copied before the native call for safe after-call logging.

To investigate ZM-to-MP failures, retain the log from before switching modes
through the first UI/script/native error. Compare incoming session/common,
loaded mod names, pending reload and UI/utility owner state. A mismatch in this
trace is a lead to inspect, not evidence that a save was physically overwritten.

## Validation and remaining work

Run on a host with Python 3 and a C++20 compiler:

```sh
g++ -std=c++20 -Wall -Wextra -Werror -O1 -pthread SPRX/tests/script_error_budget_tests.cpp -o /tmp/error-budget
/tmp/error-budget
python3 SPRX/tests/diagnostic_hook_checks.py
```

Tests exercise a million repeated errors, later distinct errors, independent VM
limits, overflow/reset and concurrent accounting. Additional harnesses compile
the actual error/transition hook bodies with host stubs and verify native-call
passthrough, result preservation, frontend dispatch and UI lock balance. The
dedicated GitHub workflow runs these checks without a PS4 SDK.

Host checks do not validate engine offsets or reproduce a console crash. Build
the Debug SPRX with the repository's supported PS4 toolchain, then validate mode
switches and map/peer joins on console. Exact clientfield-schema comparisons and
VM caller-stack decoding still require verified engine layouts; this change
does not guess them. Complex mismatch, bot/post-spawn crashes and the reported
save warning remain runtime investigation targets.

## Failed-import record lifetime

Failed imports are captured as owned, bounded script names and import metadata,
separately for GSC and CSC. The dialog consumes these records once and never
reads a retained script-buffer pointer. Names with unreadable headers/offsets
use an explicit fallback. Level epochs invalidate old records, including a
resolver that returns after the next level has started. Snapshot capture and
consumption use a nonblocking lock; contention omits optional diagnostics rather
than delaying the native resolver/error handler.

`python3 SPRX/tests/missing_import_lifetime_checks.py` compiles the actual hook
bodies, captures an import and revokes access to its original script page before
showing the error. The unfixed hook crashes with SIGSEGV; the corrected hook
reports the copied name and forwards the original error. The test also covers
independent VM records, one-time consumption, level reset, reset during native
resolution and argument/return passthrough. This fixes a secondary crash in
Debug error reporting, not the underlying unresolved import or map mismatch.

## In-match GSC dvar capture

Debug ticks poll `bo3customs_gsc_log` every six ticks without requiring a ready
UI or acquiring its lock. During gameplay the legacy `bo3customs_log` channel is
also drained. The menu path captures a pending legacy value before running the
UI queue, so UI messages cannot replace it first; a failed Lua call does not
discard that pending value.

The dedicated channel keeps GSC producers separate from the UI queue. Existing
producers using the legacy channel remain compatible. These dvars are
single-value, best-effort mailboxes: multiple writes between polls can still
replace one another. They are not a substitute for LogPrint or saved per-player
state snapshots. In-game captures use the `[ScriptLog]` label.

Run `python3 SPRX/tests/game_log_bridge_checks.py` to exercise the actual drain
and tick bodies with host stubs. It covers message ordering, failed Lua calls,
gameplay with no available UI and menu routing. Native toolchain/console
validation is still required; this change captures diagnostics, not a claim
that the underlying native crash is fixed.
