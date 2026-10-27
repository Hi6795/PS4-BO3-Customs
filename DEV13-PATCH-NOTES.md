# BO3 Customs v4.00.1 — dev.13 DEBUG

**Status:** External-storage compatibility test build. Native build and host regressions pass; **the reported PS4 loading hang is not yet confirmed fixed on hardware**.

## Why this test build exists

- The user reports that custom maps work when installed on internal console storage, whereas a friend loading maps from external storage encounters hangs and crashes.
- The friend's complete dev.12 debug log shows `zm_ds_facility_zw.ff` and `en_zm_ds_facility_zw.ff` successfully opened from `/ext0/BO3-Customs/...`, followed by at least 60 seconds with no frame or asset-link progress.
- The same log shows the global file-redirect table at `1536/1536`, its maximum capacity. Dev.12 bypassed the full table **only for `.ff`**. Other zone and bank file types could still miss their package-specific redirect.
- File opens alone do **not** establish successful continued reads, mount stability, or which engine thread held the loading critical section. The crash's complete root cause remains unverified.

## Confirmed changes since dev.12

1. **External map companion-file routing:** Registered map packages now resolve `.ff`, `.fd`, `.xpak`, `.sabl` and `.sabs` directly, even with all 1,536 global file slots occupied. This includes localized filenames and `snd/<language>/...` bank paths.
2. **Existing behavior retained:** Mod-stack overrides still take priority; files absent from the owning map fall back to the existing global redirect lookup and original game open.
3. **Mount diagnostics:** Debug builds record the return result of each PS4 external/USB sandbox mount attempt and whether its `/drive/BO3-Customs` folder is accessible, using new `[Storage]` log lines. This detects failed aliases during initialization. It does not remount drives while a level loads or guarantee future reads.
4. **Regression coverage:** An updated focused test executes the real `FileOpen_h` code with a full global file table, an external-drive map package, localized fastfiles, sound-bank subdirectories, an explicit mod override and fallback to the original path.

**Unchanged:** BO3 1.33 offsets and signatures, map contents, PC converter, Lua scripts, `Console.zip`, existing installation paths, mod selection rules, and the original global-file table capacity.

## Verification performed

| Check | Result |
| --- | --- |
| OpenOrbis LLVM 18 Debug cross-build and GoldHEN FSELF creation | PASS |
| Native image/metadata verification, including malformed-container rejection | PASS (6 invalid variants rejected) |
| Focused map companion-file and external routing regression | PASS |
| Diagnostic hooks, log bridge, script import lifetime, priority and error-budget tests | PASS |
| Mod-stack transaction and frontend reload tests | PASS with LeakSanitizer disabled due to sandbox `/proc` restrictions |
| OpenOrbis header and target syntax checks | PASS: all 14 translation units |
| Map-image sanitizer test | NOT RUN: local LLVM 18 ASan runtime libraries unavailable |
| On-console load from external storage | **PENDING** |

**Binary:** `BO3-Customs-v4.00.1-dev.13-DEBUG.sprx` (453,344 bytes)

**SHA-256:** `6f92867d940e7f4c7aea9cdcddf7bc5ddea9c424fa32a1bf8dc203a31b9f6006`

## Controlled PS4 test needed

1. Back up dev.12, then load dev.13 on the **friend's** PS4. Keep map, mods, game build and lobby conditions unchanged for a direct comparison.
2. Try `zm_ds_facility_zw` from `/ext0/BO3-Customs/usermaps/zm_ds_facility_zw/`; keep the whole debug log, including `[Storage]`, `[MapLoad]` and any `[Stall]` lines.
3. If the external test still hangs, **on that same PS4**, temporarily copy the **same exact converted map folder** into `/data/BO3-Customs/usermaps/` (if there is sufficient free space), leaving the external copy intact. The loader scans internal storage first, so it will use the internal copy. Restart the game and retry once with the same dev.13 build and no active mods.
4. Report whether external fails while that identical internal copy succeeds. If so, investigate drive alias stability, file read latency and transfer integrity; if both fail, also investigate engine DB loading, installed-map count, title/HEN differences and converted asset dependencies.

**Do not announce this as a confirmed resolution of the dev.12 console crash until the above test succeeds.**
