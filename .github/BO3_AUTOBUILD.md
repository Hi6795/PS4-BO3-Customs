# BO3 Customs automatic builds and prereleases

Every push to `nexus/standalone-converter` starts **BO3 Auto Build**. A newer
push cancels an older in-progress build. No game-source fixes are merged to
`main` by this system.

The workflow runs the committed SPRX host regression programs, release-policy
and package-verification tests, the converter's existing offline Windows test
suite, the Windows converter publish profile, and the original ORBIS SPRX
project. Every artifact records its exact source SHA, workflow run, run
attempt, file sizes and SHA-256 checksums. Expired, mismatched, incomplete or
failed builds cannot be published.

## Windows ORBIS runner

GitHub's hosted Windows image can build the .NET converter. The existing SPRX
project needs the installed ORBIS SDK and its Visual Studio integration on an
authorized Windows x64 build machine. No SDK is downloaded, embedded in
artifacts or uploaded to the repository by these workflows.

1. Register a repository runner from
   [Settings → Actions → Runners](https://github.com/Hi6795/PS4-BO3-Customs/settings/actions/runners/new).
2. Give it labels `self-hosted`, `Windows`, `X64`, `bo3-orbis` and keep its
   Actions runner current (checkout uses Node 24).
3. Install Python 3.11 or later, PowerShell 7, MSBuild, the ORBIS Visual Studio
   targets and the SDK used by this project. Make `python` available to the
   runner service. Set the service's `SCE_ORBIS_SDK_DIR` to that SDK and restart
   the service after changing its environment.
4. Set the repository Actions variable `BO3_ORBIS_RUNNER_ENABLED` to `true`.
5. Rerun **BO3 Auto Build** for the fix branch and inspect all four jobs.

Until this runner is configured, the SPRX job is skipped and the final
`complete` job fails with a setup message. A converter artifact alone never
becomes a full console release.

SPRX candidates default to the project's **Debug** configuration because
`t7_log.cpp` disables runtime logging in Release. Set
`BO3_SPRX_CONFIGURATION=Release` to select the project's Release configuration;
the selected value appears in the SPRX manifest. This setting does not change
the automatic prerelease status.

The self-hosted runner executes only the maintained fix branch. These
workflows have no pull-request execution trigger.

## Release timing

**BO3 Quiet Release** checks hourly at minute 27 and after build completion.
The default quiet period is **24 hours**, measured from the most recent of:

- The candidate commit's committer timestamp.
- Completion/update of its latest successful build.
- Issue activity since that commit in the fork or upstream repository,
  including comments and changes to closed issues.

Open issues labeled `release-blocker` or `regression` in either repository
block publication regardless of age. Existing unlabeled reports remain
known issues and do not automatically restart the clock unless updated.
Tracker/API errors stop the release check; they are never interpreted as a
quiet tracker.

| Repository Actions variable | Effect |
| --- | --- |
| `BO3_RELEASE_QUIET_HOURS` unset or `24` | Default 24-hour quiet period |
| `BO3_RELEASE_QUIET_HOURS=48` or `72` | Two- or three-day quiet period |
| `BO3_RELEASE_QUIET_HOURS=0` | Publish each fully passing fix without waiting; blocker checks still apply |
| `BO3_AUTO_RELEASE_ENABLED=false` | Pause automatic publication while builds continue |

GitHub scheduling can be delayed. Release timing is checked when the job
actually runs. Artifacts are retained for 14 days; expired artifacts require
a fresh build, which restarts the quiet period.

## Published files

Each new `nexus-ci-<12-character-source-sha>` tag is an **experimental
prerelease** and never replaces GitHub's latest stable release. Files include:

- `BO3-Customs.sprx` and `PS4.FF.Porter.exe` from the exact same build attempt.
- The checksum-pinned upstream `Console.zip` from `.github/bo3-release.json`.
- Both build manifests, a combined `build-info.json`, `LICENSE` and
  `SHA256SUMS.txt`.

The publisher verifies the entire Console.zip checksum, required Lua files,
every Lua file's decompression/CRC and per-file SHA-256, and safe archive
paths. It does not execute the console's HKS interpreter. Updating the
upstream archive requires a reviewed change to the dependency lock; changes
to the same upstream download URL do not silently enter a release.

All assets are checked locally before a draft is created. The branch, build
attempt, tracker and cooldown are checked again before the draft becomes
public. A changed candidate or new report keeps the release unpublished.
An interrupted upload retains a draft for retry. An already published tag is
never overwritten, and no workflow auto-promotes an experimental build to
stable. These checks are snapshots, not an atomic transaction across GitHub
issues and releases.

Passing automation and a quiet tracker do not establish console stability.
The manifests and release notes explicitly mark PS4 BO3 1.33 hardware
validation as pending.

## Maintenance and validation

The hourly issue watch should inspect build failures after each committed
fix and add new host regressions to `.github/scripts/run_host_checks.py`.
Unregistered new Python checks fail CI instead of being silently omitted.

```sh
python3 -m unittest discover -s .github/scripts/tests -v
python3 .github/scripts/run_host_checks.py
```

The host checks need GCC, Clang, Python 3.11+ and liblua5.4. They are distinct
from the Windows offline suite and the ORBIS compile. No live release API
writes occur during the publisher's unit tests.

The automation files also live on the default branch because GitHub runs
scheduled and workflow-run handlers from that branch. That setup commit
contains only automation configuration/scripts/documentation; the candidate
game source stays on `nexus/standalone-converter`.

References: [GitHub workflow events](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows),
[self-hosted runners](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners),
[.NET setup](https://github.com/actions/setup-dotnet),
[artifact storage](https://github.com/actions/upload-artifact).
