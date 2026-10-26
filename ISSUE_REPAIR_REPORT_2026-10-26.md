# BO3 Customs issue repair — 2026-10-26

Repository: `Hi6795/PS4-BO3-Customs`

Base: `nexus/standalone-converter` at `7603ef6d9ab918792bbe0f87cfcf564bda68f703`

Local branch: `repair/issues-2-6-20261026`
Targets: BO3 PS4 1.33; jailbroken PS4 firmware 11.02 and 13.52

## Open issue decisions

| Issue | Priority | Root cause or current finding | Correction / evidence | Status |
| --- | --- | --- | --- | --- |
| [#2](https://github.com/Hi6795/PS4-BO3-Customs/issues/2) — multi-mod replacement/order | High | Confirmed adjacent defect: when zone planning or append admitted **zero** zones, the global stack could still change, exposing uninstalled/unloaded mod file redirects. This does **not** prove the reported bot/GSC cascades were loader-caused. | Transactional `AddModZones`; append rollback/truncation; source-extracted rejection, append and partial-append regressions. | **CODE-FIXED (scope: stack planning); reported gameplay error unresolved / hardware-unverified** |
| [#5](https://github.com/Hi6795/PS4-BO3-Customs/issues/5) — script ownership | High | Rejected stack transitions could replace file/zone ownership metadata without changing the loaded set, causing a mismatch with active script claims. Earlier commits had already added first-mod script claiming and capacity guards. | Preserve stack metadata and claims on rejection; expose no unplanned appended mod; test normal, repeated and insufficient-capacity transitions. | **CODE-FIXED (scope: rejected transitions); full multi-mod ownership hardware-unverified** |
| [#3](https://github.com/Hi6795/PS4-BO3-Customs/issues/3) — temporary session mode | Medium | The temporary mode is restored by the existing `SessionModeOverride` guard (prior commit `aaa59f2`); synchronous engine callbacks can still observe the temporary mode. No incompatible callback was proven. | New source-extracted nested-transition/exception-unwinding regression; no speculative runtime change. | **HOST-VERIFIED GUARD; underlying observer risk unverified** |
| [#4](https://github.com/Hi6795/PS4-BO3-Customs/issues/4) — frontend reload | Medium | Existing `ReloadFrontend` invokes `KillServer` by design; `EXE_SERVERKILLED` is not alone evidence of a crash. No stale reference was demonstrated. | New test for not-ready/in-level guards, 100 repeated reloads, movie hook state and unchanged mod/claim counts. | **HOST-VERIFIED CONTROL FLOW; full frontend lifecycle hardware-unverified** |
| [#6](https://github.com/Hi6795/PS4-BO3-Customs/issues/6) — GSC state and FX precache | Medium | Actual map-specific asset and script mismatch cannot be determined from the issue description alone. The current runtime-error triage tool groups errors by script/offset and mod transitions (earlier commits). | Log triage self-test passed. Need reproducible map output and before/after logs under isolated/combined mods. No speculative asset substitution. | **BLOCKED on reproducer/assets and hardware** |

All five issues have zero public comments at the time of this investigation. The issue bodies contain no attached raw logs, maps or downloadable converted asset fixtures. **No issue was closed**, and no claim is made that all multi-mod gameplay failures are solved.

## Reproduction and source corrections

Before the first correction, the new `mod_stack_transaction_checks.py` failed with `FAIL: rejected zero-zone request changed live mod stack or claims` (exit 1). After the first correction, its append scenario failed with exit 7, proving an independent adjacent path. Both are now passing.

- `SPRX/t7_maps.cpp`: snapshot active `StackMod` entries before changing them during a new-zone plan; restore exact metadata when `StackEntries` returns zero. Preserve active script claims, loaded names and UI state. When appending, restore the previous stack if no additional mod was loaded, or trim the unplanned tail after a partial load. Memory allocation failure aborts safely. Changes stay inside mod-zone planning and do not alter game offsets or donor formats.
- `SPRX/tests/mod_stack_transaction_checks.py`: source-extracted executable tests for zero zones, insufficient companion slots, exhausted records, first-load rejection, backup allocation failure, rejected append, partial append and successful replacement.
- `SPRX/tests/mod_zone_capacity_checks.py`: retain the capacity precondition and require that rejected planning returns before script claims are reset.
- `SPRX/tests/session_mode_override_checks.py`: extract and exercise the existing production session-mode RAII guard.
- `SPRX/tests/frontend_reload_checks.py`: extract and exercise the existing frontend reload control path.
- `.github/scripts/run_host_checks.py`: register every new host test in the enforced inventory.
- `SPRX/tests/README.md`: explain coverage, reproductions and console limitations.

No code under `Tool/Porter` was changed. No Lua source in the upstream Console package was modified. The fork's donor-based conversion path, native version offsets and existing public APIs remain unchanged by this patch.

## Verification matrix

| Check | Exact command or method | Outcome |
| --- | --- | --- |
| Focused host regression | `ASAN_OPTIONS=detect_leaks=0 python3 SPRX/tests/mod_stack_transaction_checks.py` | **PASS** — 8 cases; original branches failed as described above |
| SPRX host suite | `ASAN_OPTIONS=detect_leaks=0 CXX=clang++-18 python3 .github/scripts/run_host_checks.py` | **PASS** — 18 programs, 0 failures |
| Release policy tests | `python3 -m unittest discover -s .github/scripts/tests -v` | **PASS** — 22 tests |
| Native SPRX compile/link/inspection | `python3 .github/scripts/build_sprx.py` | **PASS** — Debug, 14 native translation units; PS4 SCE dynamic library / GoldHEN FSELF, 430,416 bytes |
| Native corruption checks | `python3 .github/scripts/tests/check_native_artifact.py build/openorbis Debug` | **PASS** — valid build accepted, all six corrupted container/module variants rejected |
| Converter core cross-build | From `/tmp` with `DOTNET_CLI_HOME=/tmp/bo3-dotnet-home NUGET_PACKAGES=/tmp/bo3-nuget`: `dotnet build <repo>/Tool/Porter/src/FFPorter.Core.T7/FFPorter.Core.T7.csproj -p:EnableWindowsTargeting=true -p:NuGetAudit=false -v:q` | **PASS** — 0 warnings, 0 errors, .NET SDK 10.0.111 (not the pinned 10.0.112) |
| Desktop converter cross-build | Same environment: `dotnet build <repo>/Tool/Porter/src/FFPorter.Desktop.T7/FFPorter.Desktop.T7.csproj -p:EnableWindowsTargeting=true -p:NuGetAudit=false -v:q` | **PASS** — 0 warnings, 0 errors; compile only, no WPF execution |
| Windows regression test assembly cross-build | Same environment: `dotnet build <repo>/Tool/Porter/tests/FFPorter.Checks/FFPorter.Checks.csproj -p:EnableWindowsTargeting=true -p:NuGetAudit=false -v:n` | **BLOCKED** — Linux MSBuild restore graph stops without a reported error; required Windows .NET 10.0.112 runner remains the verification target |
| Console package SHA and Lua audit | `audit_console('/tmp/bo3-console-v4.00.zip', .github/bo3-release.json['console'])` | **PASS** — `0b9af752ba84fe38eb3e734d495d871d8cb5cf5cf7c7a0edad5a6ef29bc2d5f0`; 46 Lua files; required paths and CRC checks passed |
| Source whitespace review | `git diff --check` | **PASS** |
| Windows WPF converter GUI checks | `Tool/Porter/tests/run.cmd` | **NOT RUN** — Windows host and pinned .NET 10.0.112 not available on this Linux host |
| Converter `dotnet publish` / full Windows package | CI workflow on `windows-2025` | **NOT RUN** for this repair branch; no Windows execution claimed |
| Console Lua/HKS execution | BO3 game VM on PS4 | **NOT RUN** — Lua checks cover host/extracted paths and package integrity, not game VM behavior |
| Firmware 11.02 and 13.52 | BO3 1.33 console smoke/regression runs | **NOT RUN** — no console connected |

The first host suite was also executed with system `g++`, with all programs passing when leak detection was disabled for the restricted container. The initial invocation without `ASAN_OPTIONS=detect_leaks=0` produced `LeakSanitizer` inspection errors due to host process tracing and also lacked `clang++`; these were environment limitations, not product test failures. Subsequent GCC and Clang runs completed successfully.

The initially sandboxed .NET restore was denied access to NuGet (`NU1301`); the converter core and desktop cross-builds both passed after running with the already configured task-specific SDK home and authorized package restore. A direct `dotnet` invocation from `Tool/Porter` rejects the installed 10.0.111 because `global.json` requests 10.0.112. The cross-builds were invoked from `/tmp` so the available SDK could be used for additional compile evidence; this is not equivalent to CI's pinned Windows build.

Console.zip is an **external checksum-pinned release dependency** (862,304,932 compressed bytes; 89 archive entries), not a tracked directory in the source tree. Every included Lua file was CRC-tested and listed. The encrypted/binary game assets and actual HKS execution require an appropriate game/runtime environment.

## Remaining work / acceptance limits

1. Test fresh boot, two simultaneous mods, sequential append, replacement, partial unload, map transitions, mod UI reload and repeated selection on **both** PS4 firmware targets. Collect `/data/BO3-Customs/log.txt` before another launch.
2. Reproduce issue #2 with `zombiebots` alone, `zm8p_ps4_t143` alone, and both together. Compare earliest failing GSC/CSC signature relative to asset load events and zone readiness.
3. Reproduce issue #6 using the exact `zm_radiant_hallway` workshop files, converted `.ff`/`.xpak` and all donor/ref manifests. Verify FX precache and GSC variable initialization. Do not replace missing FX with incompatible assets.
4. Determine whether engine callbacks observe a harmful intermediate session mode during `LoadModeCommonForMods`, and whether game-side `KillServer` reload fully resets Lua/UI and asset pointer lifetimes.
5. Run the Windows .NET 10.0.112 converter regression suite and publish artifact checks via a manually authorized workflow or Windows runner. Compare both components' package version/installation layout before release.

Hardware-unverified issues remain **open**. No release publication, issue closure or merge was performed by this repair.
