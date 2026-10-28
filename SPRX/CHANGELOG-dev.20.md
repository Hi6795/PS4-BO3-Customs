# BO3-Customs v4.00.1-dev.20 — Stabilization changes

Status: experimental debug release for PS4 / PS5 compatibility testing. Branch `fix/detour-install-safety`; main untouched.

## Crash-prevention-oriented changes

- **Prevent repeated mmap failure storms:** if the plugin fails to reserve memory for a tracked asset-pool expansion, it records one error and does not repeatedly attempt the same exhausted pool on subsequent asset links. This avoids hammering the allocator when memory is already tight.
- **Reduce long synchronous pool-growth pauses:** each asset-pool expansion attempts no more than approximately 1 MiB of raw records per batch. Larger records expand one entry at a time. The pre-existing per-pool growth caps and 64 MiB total extra-mapping budget remain in place.
- **Use remaining pool-growth memory:** the requested batch is reduced to fit the remaining 64 MiB budget instead of rejecting an otherwise affordable smaller expansion.
- **Reduce debug-log pressure:** cap distinct missing-asset messages to 400 per level instead of 3,000. Missing assets continue to use the game's native fallback; only diagnostic verbosity changes.
- **VM exhaustion diagnostics:** preserve the dev.19+ warning for the server child-script-variable limit. Do not bypass the original VM error handler, clear active scripts, or pretend the unrelated asset pools increase script VM memory.
- **Version bump:** internal label now reports `BO3-Customs-v4.00.1-dev.20-DEBUG`.

## What this can and cannot prevent

These are concrete guards against plugin-induced allocation retry storms and diagnostic overhead, with a bounded expansion path. They are not a proven fix for PS4 `zm_radiant_hallway` rendering/GPU-frame stalls (main offset `0xBADF5A`) or the PS5 high-player-count script VM variable limit. The prior logs have no conclusive native crash fault address. Avoid forcing game memory frees, GPU resets, or engine VM limit patches without validating the relevant engine internals.

## Validation plan

Build native SPRX, execute host regression suites, inspect binary label, then test on hardware: official map first; Radiant Hallway without mods, then with one mod; internal storage versus external storage; observe `[Pools]`, `[Stall]`, and `[VM]` logs. Back up your last working dev.19 before replacing it.

PR: https://github.com/Zorian1337/PS4-BO3-Customs/pull/19
