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

| M03 | Optional hooks | Attempt optional signature-gated map hook installation on later map refresh, even when required hooks already installed | Implemented; native and console tests pending |
| M04 | Map init | Failed `BuildMatches` probe no longer permanently marks initialization done | Implemented; tests pending |
| H08 | Detour failure | An unrecoverable partial target patch now retains its owning detour record and mapping so duplicate attachment is blocked | Implemented; fault-injection tests pending |

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

## Oct 28 external-map / gameplay incident review (five supplied PS4 logs)

**Observed, not root-cause proven:**
- External `zm_ds_facility_zw.ff` opened at `/ext0/BO3-Customs/usermaps/...`, 205,864,064 bytes; global redirect table full `1536/1536`. The game then reported no frame/asset progress at 20, 40 and 60 seconds during map loading. A successful file open does not prove complete reads or asset linking.
- Internal `zm_radiant_hallway.ff` (201,920,896 bytes) and its localized fastfile and xpak companions opened from `/data`, then also showed a 20-second level-loading stall. The engine also requested a `.fd` whose logged route stayed native; whether this optional companion is necessary has not been established.
- Older logs showed three `Com_Error 2: 0xf7c0fe22` map launches with zero script-runtime errors, but no engine error decoding or exception dump establishes the cause.
- A gameplay/client crash was reported; available snapshots do not contain a conclusive faulting instruction or exception code. Do not assume external storage causes the gameplay failure.
- Logs come from multiple October 15–28 debug builds; do not treat observed behavior as proof of a regression in the current stabilization branch.

**Experimental source fix:** `FindPackageFile` now rejects truncated paths/non-regular files, and a missing per-map asset cannot resolve to a global registry entry owned by another package. Focused host regression includes foreign-package collision and same-package fallback. It addresses an invalid routing risk, not a confirmed cause of the observed stall.

**Next diagnostic evidence:** Capture post-open read errors/latency for external files, active DB/asset phase before watchdog snapshot, and the PS4 client's native crash exception PC/LR/stack. Compare identical converted map binaries on internal and external storage without mods.

## Oct 28 asset pool and available-memory investigation

The existing `SPRX/t7_maps.cpp` already has an on-demand pool extender, `GrowPool`, for selected asset types. It checks an expected pool element size/base limit and extends a drained free list through PS4 `sceKernelMmap`; its intended per-type cap is approximately one additional original pool's capacity (minimum 64 elements). This is **asset-count capacity**, not a reservation of additional game RAM. It does not reclaim or resize the engine's other memory arenas.

**Implemented, pending console stress test:**
- Serialize plugin-side free-list growth so two asset-link workers do not independently install new free-list chains.
- Clamp each growth batch to the remaining per-type allowed capacity, preventing limit overshoot when the cap is not a multiple of the batch size.
- Check limit arithmetic and mmap allocation-size arithmetic; report the size of a successful allocation in bytes for log-based diagnosis.

**Not established / not implemented:**
- Discord discussion's claim that 5 GB is reserved and half unused is not independently confirmed from this BO3 EBOOT or console memory telemetry. Do not reclaim/reclassify unknown game or system memory regions.
- No global pool-size increase, expanded RAM allowance, or patch to proprietary OS memory budgets was made.
- The native game's own free-list access and possible background allocators need hardware stress testing; serializing the plugin's growth calls does not prove the engine's allocator synchronization.
- More usable game memory does not automatically fix external-storage stalls: confirm read I/O and asset linking separately.

Recommended test: load the same converted map internally and externally, monitor `[Pools]` growth and exhausted-pool messages, collect a native crash dump and read-path timing if hangs persist.
