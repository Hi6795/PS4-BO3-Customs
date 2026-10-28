# BO3-Customs v4.00.1-dev.21-DEBUG — Stabilization and performance

Experimental debug build for PS4 and PS5 testing, branched from `fix/detour-install-safety`. The `main` branch is unchanged.

## Changes since dev.20

- Fixed unsafe fixed-size Lua callback formatting: bounded `snprintf` cursor updates, rejecting truncated scripts and quoted arguments; null text is handled safely.
- Fixed a shared 64 KiB map-directory scanner buffer race and added record-bounded directory filename parsing.
- Hardened streamed compressed-block ring checks against unsigned integer overflow and malformed lengths.
- Changed custom-map texture-ready timeout behavior: avoid forcing success while the streamer reports pending work, or is not started/done.
- Reduced repeated Lua compatibility helper injection during gameplay from every 6 frames to every 30 frames, while keeping immediate refresh for newly created UI states or explicit requests.
- Reduced in-game debug Dvar log polling from every 6 frames to every 30 frames; menus preserve their previous cadence.
- Skip the lobby-only Lua maintenance callback during a match.
- Retained dev.20 safeguards for failed asset-pool mapping retries, limited expansion batches, and reduced missing-asset log pressure.
- Embedded binary label changed to `BO3-Customs-v4.00.1-dev.21-DEBUG`.

## Verification and limitations

Automated host regressions and native SPRX compilation must complete successfully for this build. Hardware validation has **not** been performed. Reduced recurring work on the game thread can help microstutters but does not guarantee improved FPS. The prior PS4 GPU-frame stall at BO3 offset `0xBADF5A` and PS5 server script-variable exhaustion remain unproven as fixed; no direct GPU-fence manipulation or mid-game VM memory clear is attempted.

## Recommended testing

Back up dev.20. First verify official maps and menu/mod navigation, then test `zm_radiant_hallway` with no mods and with mods, and test the PS5 18-player scenario conservatively. Keep stall and VM logs for before/after comparisons.

PR: https://github.com/Zorian1337/PS4-BO3-Customs/pull/19
