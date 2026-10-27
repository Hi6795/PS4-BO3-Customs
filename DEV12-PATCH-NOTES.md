# BO3 Customs v4.00.1 — dev.12 DEBUG

**Status:** Debugging candidate. Built and format-verified; **PS4 map-launch fix not yet confirmed on console**.

## Problem investigated

- The supplied October 27 debug log shows three failed launches of `zm_ds_house_zw` at 01:40.339, 02:39.193 and 06:46.153.
- All three report `Com_Error 2: ERREUR - 0xf7c0fe22`, followed by a server shutdown and return to frontend.
- There are zero script runtime errors for these launches; selected mod count and loaded mod count are both zero.
- The video shows a map launch progressing through the loading sequence and returning to the Zombies menu with an error.
- The abort occurs after `LoadLevelZones_h` prepares the level and before its native call returns, with no map-zone asset-load event recorded. The existing log does not establish whether the map fastfile is absent, misrouted, invalid or rejected later by the engine.

## What changed since dev.11

1. **Map fastfile routing:** The SPRX now checks the registered map package folder directly for `<map>.ff` and localized fastfiles such as `en_<map>.ff`, before using the bounded global redirect table. This addresses a real loader edge case: a discovered map may otherwise remain selectable even when that table has reached its 1,536-file capacity.
2. **Existing mod precedence preserved:** Explicit mod-stack file overrides still take precedence over the direct map fastfile route; the previous general file lookup remains as fallback.
3. **Better debug evidence:** Each recognized map launch now logs `[MapLoad] preflight` with the resolved fastfile path, presence, size, and redirect-table occupancy. Actual recognized map fastfile open calls log `[MapLoad] open` with the selected path and returned file descriptor. These messages will distinguish failed file routing from later engine rejection.
4. **Linux build compatibility:** Fixed the letter case of `HDE64.c`'s two local header includes so the existing OpenOrbis build works on case-sensitive filesystems.

**Not changed:** BO3 1.33 game offsets and signatures, mod activation semantics, Lua menu scripts, map converter, `Console.zip`, and original installed map files.

These notes describe the changes made to the latest checked-out fork source, `Hi6795/PS4-BO3-Customs` at commit `798e07de4376c851cf6bb41559bdfbd33d9df2e4`, plus the local patch. The dev.11 binary was not available for a byte-for-byte comparison. For upstream context, the latest public `ItsJokerZz/PS4-BO3-Customs` release located during review was v4.00, published October 21, 2026: https://github.com/ItsJokerZz/PS4-BO3-Customs/releases .

## Verification

| Check | Result |
| --- | --- |
| OpenOrbis LLVM 18 Debug compilation and linking (14 native units) | PASS |
| GoldHEN FSELF / SCE metadata, module exports and payload segment verification | PASS, four payload segments verified |
| New host tests: full redirect table, localized map FF, mod override precedence, fallback | PASS |
| Diagnostic transition, script-error and owner tests | PASS |
| Host tests: log bridge, missing-import lifetime, script priority | PASS |
| Mod stack and frontend reload tests | PASS with LeakSanitizer disabled (container `/proc` restriction) |
| Map-image AddressSanitizer host check | NOT RUN — LLVM 18 ASan runtime libraries missing |
| BO3 1.33 console test with `zm_ds_house_zw` | PENDING — console unavailable here |

**Binary:** `BO3-Customs-v4.00.1-dev.12-DEBUG.sprx` (453,344 bytes)

**SHA-256:** `b0a52b513c4c1a8166fcfa11328f737e2c84162fc35df04d199dd32cea3d1587`

## Recommended test

1. Keep a backup of your previous dev.11 SPRX. Replace only the SPRX with the versioned dev.12 debug binary and start BO3 anew so `/data/BO3-Customs/log.txt` is fresh.
2. First try the `zm_ds_house_zw` map in a local solo Zombies lobby; then repeat in LAN with the second player present if it succeeds.
3. Capture the **entire** new `/data/BO3-Customs/log.txt` immediately after the result. The new `[MapLoad]` entries identify whether the map fastfile is found, its selected path/size and the actual open result.
4. If the same engine error remains with a successful file open, supply the exact converted `zm_ds_house_zw.ff` and its converter build log. At that point the next step is validating its fastfile headers/content rather than making ungrounded additional SPRX hook changes.

**Important:** Do not announce `0xf7c0fe22` as solved yet. This build corrects the confirmed loader routing weakness and gathers the missing evidence; the root cause of the supplied specific map failure remains to be verified on hardware.
