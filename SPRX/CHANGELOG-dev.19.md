# BO3-Customs v4.00.1-dev.19 — Debug Changelog

**Status:** Experimental PS4 GoldHEN build. Changes are on `fix/detour-install-safety` in `Zorian1337/PS4-BO3-Customs`; `main` remains unchanged.

## Changes since the user's dev.18 baseline

### Asset pools and memory
- Expanded the **on-demand additional-entry ceiling** for four lightweight asset pools: `localize` (23,552 original -> up to 70,656 total entries), `rawfile` (4,300 -> up to 12,900), `scriptparsetree` (1,024 -> up to 3,072), and `scriptbundle` (1,024 -> up to 3,072). These are maxima, not allocations made at startup.
- Kept the existing limits for heavier pools such as models, materials, animations, and sound.
- Enforced a shared 64 MiB budget for additional pool mappings; allocate only when the relevant tracked pool runs out.
- Added lock protection around plugin-initiated pool expansion, exact per-pool cap enforcement, overflow checks, and growth-size logging.
- Replaced racy first-use pool-lookup table initialization with an immutable scan.

### Custom map / external storage reliability
- Prevented truncated or directory-valued files from being used as fastfile map paths.
- Prevented a missing map-specific asset from silently falling back to a same-named file owned by another package.
- Reject malformed directory-record lengths and truncated indexed asset paths.
- Allowed optional map hook installation to retry on refresh, and prevented a failed initial game probe from permanently marking installation complete.

### Sharing and detour safety
- Added throttled retry for incomplete network/sharing setup, with safeguards against duplicate auxiliary workers.
- Improved failed detour-patch ownership tracking and prevented retry over an unrecoverably partially restored target.
- Preserved current detour lifetime behavior; did **not** add unsafe live trampoline unloading.

### Versioning
- Embedded debug build label updated to `BO3-Customs-v4.00.1-dev.19-DEBUG`.
- Standalone compiled file: `BO3-Customs-v4.00.1-dev.19-DEBUG.sprx`.

## Verification and known limitations
- GitHub Actions host regression checks, PS4 cross-compilation, and native binary checks must pass for this commit before distributing the binary.
- Physical PS4 gameplay, USB/external map loads, mod transitions, and eight-player scenarios remain **unverified** for dev.19.
- Earlier logs show gameplay and asset-loading stalls on an older dev.14 build; these changes are **not confirmed crash fixes**.
- No unverified 5 GB memory region was reclaimed. No EBOOT or system memory reservation changes.
- The game's native asset allocator might require additional synchronization: successful compilation alone does not prove safe execution with simultaneous native asset workers.

## Recommended smoke test
1. Back up the previous SPRX and logs.
2. Launch an official map without mods and test gameplay.
3. Launch the same converted custom map from internal storage and then external storage, still without mods.
4. Repeat map transitions and inspect `/data/BO3-Customs/log.txt` for `[Pools]`, `[MapLoad]`, and watchdog messages.
5. Only then test multiple mods and network clients.

Source PR: https://github.com/Zorian1337/PS4-BO3-Customs/pull/19
