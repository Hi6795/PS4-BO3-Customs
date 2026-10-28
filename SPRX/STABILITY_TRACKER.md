# BO3-Customs stability work tracker

Scope: **Zorian1337/PS4-BO3-Customs only**. Do not write to Hi6795 or ItsJokerZz repositories. Base for this branch: merged PR #18, commit `8225c7bf7d71a02b099b64cec5b8452dca5eec75`.

## Implemented on this feature branch (not yet console-verified)

| ID | Area | Change | Verification |
| --- | --- | --- | --- |
| H01 | Detour attach | Record targets only after successful installation; failed attempts remain retryable; stop safely at 96 entries | Source change; host/PS4 tests pending |
| H02 | Detour memory | Check `sceKernelMprotect` before writing; restore target bytes on a failed write when possible | Source change; fault-injection test pending |
| H03 | Detour concurrency | Serialize registration and detach operations with atomic lock | Source change; multi-thread testing pending |
| H04 | Detour detach | Do not remove mapping when original bytes cannot be restored | Source change; thread-quiescence still **unresolved** |
| M01 | Map hooks | Mark required hooks initialized only when all original function pointers exist; retry on later refresh | Source change; host/console tests pending |
| M02 | Map signature patches | Avoid repeating one-shot signature modifications during hook retries | Source change; tests pending |

| S03 | Sharing init | Retry unavailable imports and server-thread start without duplicate asks worker or repeated peer reset; five-second retries from frame tick | Implemented; console test pending |

## Follow-up risks (no claim of resolution)

| ID | Area | Investigation/action |
| --- | --- | --- |
| H05 | Detour detach | A fixed 20 ms grace interval does **not** establish that trampoline users have exited. Design explicit quiescence or defer freeing until process shutdown. |
| H06 | Detour failed restore | If a partially written patch cannot be restored, currently retaining the trampoline protects from immediate dangling jump targets but leaves recovery unresolved. |
| H07 | Native patch integrity | Add target-byte validation, fault injection and instruction-cache coherency checks appropriate to PS4. |
| S01 | Sharing init | Retry state implemented; verify on-console recovery after transient import/thread failures and test worker concurrency. |
| S02 | 8-client map sharing | Measure direct 4-connection and UDP 4-peer limits with up to eight lobby members before changing capacities. |
| L01 | Mod lifecycle | Exercise mode transitions, nested frontend reloads and asset ownership under existing regression suite and on console. |
| N01 | Multiplayer | Add bounded admission/spawn/snapshot diagnostics for 5–8 players without assuming `kMemberSlots=18` means eight-player game support. |
| T01 | Regression | Run existing host checks plus additional hook failure/retry tests, then PS4 tests. |

## Rules for merging
1. Keep current `main` and user-authored fixes preserved; no force pushes, resets or upstream writes.
2. Record the exact changed files and compare with `main` before merge.
3. Host tests and target compile must pass; console validation required before claiming gameplay stability.
4. The user explicitly excluded EBOOT fingerprinting from this work.
