Checks for the tool. Nothing here ships: the release profile publishes `src/FFPorter.Desktop.T7` only.

```text
run.cmd                      build everything and run every offline check
run.cmd --only shell,ftp     run some of them: settings, cleanup, windows, ftp, community, pause, delete,
                             extras, console, shell, sound, updates
run.cmd --shots              render the windows to work\out as pictures (shot_*.png)
```

The offline checks open the app's windows off screen and talk to stand-in servers on this PC
(a map server, a GitHub, a console's FTP server). They do not touch the internet, a console or your settings.

These reach the real world and are only run by hand:

```text
run.cmd --real                    the real map list and the real releases, read only
run.cmd --real-download <map>     download one map from the list, pausing and resuming part-way
run.cmd --real-update             let a stand-in older build update itself from the latest release
run.cmd --console <ip[:port]>     sign in to a console's FTP server and look, changing nothing
```

- `FFPorter.Checks` is the check program. Each file is one area; `Program.cs` lists them.
- `UpdateProbe` is a tiny single-file app that carries the updater, so a whole update (download, hand-over,
  swap, restart) can run for real without the full tool. `run.cmd` publishes it twice as `work\probe_one` and `work\probe_two`.
- Everything the checks write goes under `work\`, which git ignores.
