# BO3 Customs v4.00.1 dev.14 — DEBUG

**Status:** PS4 1.33 diagnostic/hardening candidate. Native build and host checks pass; **the external-storage black screen is not yet confirmed fixed on console**.

## What changed from dev.13

1. **Protect the native frontend/bootstrap zone files.** The general file-redirect hook no longer substitutes a custom shared zone, map or mod file for the game's `core_*` bootstrap assets, including localized versions and `.ff`, `.fd`, `.xpak`, `.sabl` or `.sabs` companions. The supported `core_mod` custom-mod zone is deliberately exempt. This removes a genuine unsafe override path that previously had no diagnostic trace. Whether the affected console contains a conflicting zone file remains unknown.
2. **Trace non-map external redirects.** Debug logs now include `[Routing] external open` with the original path, routed external path, file descriptor/result and time spent in the native open call. Dev.13 logged custom-map opens but did not expose shared-zone or mod-file redirects, including redirects during frontend initialization.
3. **Report prevented core-zone overrides.** If a custom file would have shadowed a native `core_*` bootstrap file, the debug log records `[Routing] ignored custom override of native bootstrap ...`. The native game path is used instead.
4. **Identify the running build.** Startup logs now contain `[Build] BO3-Customs-v4.00.1-dev.14-DEBUG`, alongside the build timestamp, to avoid ambiguity when multiple SPRXs exist.
5. **Expanded regression coverage.** The real file-open hook is exercised with a full 1536-entry registry containing conflicting `core_frontend.ff`, localized `en_core_frontend.ff` and supported `core_mod.ff` paths. It verifies native core files stay native, custom mod routing remains intact, and the dev.13 map/companion routing scenarios still pass.

## What remains unchanged

- Dev.13's external map/companion routing, language fallback, mod precedence, `/ext0` mount diagnostics and remaining BO3 features.
- The game SDK, firmware-specific kernel offsets and unrelated project components.
- The release is still a **DEBUG** build, not a hardware-qualified stable release.

## Investigation context

The dev.13 test log supplied on October 27, 2026 contains 321 lines and confirms that `/ext0/BO3-Customs` was accessible, 127 custom maps were indexed, no mods were selected, and the native `core_frontend` zone load was underway. The last saved line is at **00:46.725** during asset-default processing. No map-load request, explicit engine error or watchdog stall snapshot was recorded. That evidence does **not** establish that a `core_*` redirect caused the blank screen; the new protection eliminates that source-level risk and the extra routing logs will make the next external test more conclusive.

## Build and verification

| Check | Result |
| --- | --- |
| OpenOrbis LLVM 18 / GoldHEN DEBUG compile, link and package | PASS |
| PS4 FSELF/OELF/ELF validator, including six rejected invalid variants | PASS |
| All 14 translation-unit target syntax checks / SDK stat ABI | PASS |
| Full-table external map routing and new native-bootstrap guard | PASS |
| Frontend reload state, mod-stack transactions | PASS |
| Script priority, missing-import lifetime, diagnostic hooks and log bridge | PASS |
| Map-image GPU surface test (ASAN/UBSAN; LeakSanitizer disabled for sandbox `/proc`) | PASS |
| Real PS4 external-storage startup and map load | **PENDING** |

Artifact: `Releases/BO3-Customs-v4.00.1-dev.14-DEBUG.sprx`

- Size: **453,344 bytes**
- SHA-256: `00ada5d032fdf9b466db55f624a0717b288efd0187999cacd79a5e34132e8703`

## Tester checklist

1. Install only `BO3-Customs-v4.00.1-dev.14-DEBUG.sprx` with the same `Console.zip` files under `/data/BO3-Customs/`.
2. Keep the existing external maps and mods in place and reproduce the same startup path.
3. Confirm the log contains the new `[Build]` line. Look for `[Routing] ignored custom override`, `[Routing] external open`, `[Stall]` and `[Error]` lines.
4. If the black screen persists, save `/data/BO3-Customs/log.txt` before relaunch. An absence of `ignored custom override` indicates that the specific collision prevented in this build was not observed.

Rollback: reinstall the retained dev.13 DEBUG SPRX. No user content is removed or migrated by this patch.
