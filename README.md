<p align="center"><img src="Tool/logo.png" width="96" alt="PS4 .FF Porter"></p>

A tool to convert BO3 customs to PS4 and a SPRX to load them on HEN consoles.

## What you need

- A HEN console with BO3 **version 1.33** (**any region**).
- **BO3 installed on PC**, plus the maps or mods you want to port.
- The tool, `Console.zip` and the SPRX from the [Releases](../../releases) page.

## Getting started

1. Extract `Console.zip` and copy it to `/data/` on your console.
2. Put the SPRX anywhere you like, preferably in `/data/BO3-Customs/`.
3. Run the tool, drop a Steam map or mod into it, press **Convert** and wait for it to finish.
4. Copy the converted map into `/data/BO3-Customs/usermaps/`. Mods go in `/data/BO3-Customs/mods/`.
5. Launch the game and load the SPRX with your preferred method.

If your console runs an FTP server, the tool does steps 1, 2 and 4 for you: see **Sending to the console**.

## The tool

- **Convert**: drop maps or mods in, or use **Add** to pick from what you have installed.
- **Converted**: everything you have converted, ready to send to the console.
- **Community**: maps and mods that are already converted, ready to download.
- **Installed**: what is on your console. Maps and mods can be removed from here.
- **Settings**: your console, where maps are sent, and updates.


## Sending to the console

1. Open **Settings**, enter your console's IP address and press **Test**.
   The port is 2121 unless you add another, like `192.168.1.50:21`.
2. **Console files** downloads `Console.zip` and the newest SPRX and sends them to the console.
3. Press **Send to console** on anything you converted or downloaded.

- **Auto-FTP** sends everything by itself as soon as it is converted or downloaded.
- **Send to** picks where maps and mods go: the console's own storage, a USB drive or extended storage.

## Saves

Mods and maps that save (stats, settings) keep their files in `/data/BO3-Customs/saves/<mod or map>/`.

## Recovering from a broken mod

Fully close BO3 and launch it again. Selected mods start disabled on a fresh game
process; `active_mod.txt` is not automatically loaded. This remains true if the
console cannot delete that file. Returning to the main menu or restarting a map
is not a full restart and can retain loaded mod code.

Once back in the menu, leave the broken mod unticked. To remove it, close BO3
and remove only that mod's folder from the location where it was installed:
`/data/BO3-Customs/mods/` or the external drive's `BO3-Customs/mods/` folder.
Installed files and map/mod saves are separate; removing a mod does not require
removing your game save or the `BO3-Customs/saves/` folder.

If a host sends the same broken mod again, avoid rejoining that lobby until the
host disables or replaces it. Host-session activation can load mods again after
startup. Menu recovery can automatically untick a local UI mod whose menus fail
to build, but it cannot recover from every native crash or freeze; use a full
restart in that case.

## USB and extended storage

Maps and mods can also sit on a USB drive or extended storage, in a `BO3-Customs` folder at the root of the drive
(`BO3-Customs/usermaps/` and `BO3-Customs/mods/`). Everything else stays in `/data/BO3-Customs/`.

## LAN games

Everyone needs this SPRX.

- **Downloads**: missing the host's map or mods? They download, then you load in. Works on one network or between homes
  over any System Link tunnel. Circle cancels; the next join resumes.
- **Missing maps**: players without the map get a "MISSING MAP", "MISSING DLC" or "DOWNLOADING" card, and the host's
  map list shows how many are missing each map. Starting while someone is downloading asks first.
- **Player menu**: select a player to promote to host, kick, ban or mute. Promoting switches to Shadows of Evil first
  if anyone is missing the map.

## Found a problem?

[Open an issue](../../issues) and provide `/data/BO3-Customs/log.txt` from your console.
Grab it before you start the game again: every launch starts a new log.

Also attach the tool's log for the map that causes the issue: press **Log** in the tool, then **Save**.
If the tool was closed since, its logs are in `work\analysis\gui_logs` beside the tool.

### DEBUG GPU/streaming captures (dev.26)

The source now adds `[GPU]` records to the existing `/data/BO3-Customs/log.txt`
and debug-output path. They run on the existing watchdog, with up to seven
early checkpoints in the first 30 seconds of a custom level and snapshots at
the existing stall-report intervals. No extra rendering-thread logging, GPU
queue resets or forced completion values are used.

Each capture includes the copied level/routed-mod context, observed GPU
completed/waited/building frame values, changes since the previous sample,
client-frame age, asset-link counter, pending streaming, missing required image
and mesh parts, lighting-blob counts and sound-loading fraction. Unreadable
fields are marked unavailable; their zero values must not be interpreted as
successful loading. Resource counters are observations taken over time, not an
atomic engine snapshot or an automatic crash diagnosis.

An unchanged GPU value alone does not prove a GPU fault: the CPU may have
stopped submitting work. Include the surrounding `[Stall]` thread samples,
`[Stream]` entries and engine errors so those cases can be distinguished.
Loading/early-level stalls and later engine-idle stalls are labeled separately.
Compute-record snapshots repeat on the bounded stall rounds for comparison.

For a useful report, start a fresh BO3 process, reproduce one failing map run,
and save the log before starting BO3 again. Record PS4/PS5, the exact SPRX build,
map/version, internal or external storage and every enabled mod. If possible,
compare the identical converted files internally and externally with mods off.
The existing diagnostic memory buffer is capped at 256 KiB per game process;
fresh runs preserve room for the failure instead of accumulating unrelated runs.
These additions are included in dev.26 DEBUG. They collect evidence and do not
by themselves fix or prove the cause of a map freeze.

## Console layout

<details>
<summary>Every file BO3-Customs uses on the console</summary>

```text
/data/BO3-Customs/
├── BO3-Customs.sprx
├── config.cfg
├── log.txt
├── active_mod.txt
├── ui_scripts/
│   ├── graphics.lua
│   ├── kbm_strings.lua
│   ├── mapselect.lua
│   ├── maptable.lua
│   ├── mods.lua
│   ├── mouse.lua
│   └── restart.lua
├── lui/
│   ├── lua/
│   │   └── lobby/
│   │       └── lobbysteam.lua
│   └── ui/
│       ├── t6/
│       │   └── menus/
│       │       └── keyboardtextfield.lua
│       ├── t7/
│       │   └── utility/
│       │       ├── modsutility.lua
│       │       ├── pcutility.lua
│       │       └── starterpackutility.lua
│       └── uieditor/
│           ├── menus/
│           │   ├── lobby/
│           │   │   └── common/
│           │   │       └── lobbyserverbrowseronline.lua
│           │   ├── mods/
│           │   │   └── menumodsload.lua
│           │   ├── pc/
│           │   │   ├── keyboardinputoverlay.lua
│           │   │   ├── serverbrowserfilters.lua
│           │   │   └── serversettings.lua
│           │   └── core_patch_pc_require.lua
│           └── widgets/
│               ├── charactercustomization/
│               │   ├── tauntbindswidget.lua
│               │   └── tauntwidget.lua
│               ├── hud/
│               │   └── console/
│               │       ├── console.lua
│               │       ├── consoleentry.lua
│               │       ├── consoleentrycontainer.lua
│               │       ├── consoleicon.lua
│               │       └── consoletext.lua
│               ├── lobby/
│               │   ├── common/
│               │   │   └── splitscreenlobbybuttonpc.lua
│               │   └── lists/
│               │       └── lobbymembermp45.lua
│               ├── mods/
│               │   ├── entrymodsload.lua
│               │   └── widgetmodsload.lua
│               ├── pc/
│               │   ├── choosecharacter/
│               │   │   └── choosecharacter_cardclickbutton.lua
│               │   ├── craft/
│               │   │   ├── craftemblemeditmouseinstructions.lua
│               │   │   └── craftsideselectorbutton.lua
│               │   ├── serverbrowser/
│               │   │   ├── serverbrowserbutton.lua
│               │   │   ├── serverbrowsercheckbox.lua
│               │   │   ├── serverbrowserdetails.lua
│               │   │   ├── serverbrowserflag.lua
│               │   │   ├── serverbrowserheader.lua
│               │   │   ├── serverbrowserheadericoncolumn.lua
│               │   │   ├── serverbrowserheadernamedcolumn.lua
│               │   │   ├── serverbrowserplayerrow.lua
│               │   │   ├── serverbrowserrow.lua
│               │   │   └── serverbrowserrowinternal.lua
│               │   └── utility/
│               │       ├── inputtextfield.lua
│               │       ├── starterpackwatermark.lua
│               │       └── textinputwidget.lua
│               └── horizontalscrollingtextbox_14pt.lua
├── zone/
│   ├── snd/
│   │   ├── all/
│   │   │   ├── zm_levelcommon.all.sabl
│   │   │   └── zm_levelcommon.all.sabs
│   │   └── en/
│   │       ├── zm_levelcommon.en.sabl
│   │       └── zm_levelcommon.en.sabs
│   ├── en_zm_levelcommon.ff
│   ├── en_zm_levelcommon.xpak
│   ├── zm_levelcommon.ff
│   └── zm_levelcommon.xpak
├── saves/
│   └── <mod or map>/
│       └── ...                    files a mod or map saves, like its stats and settings
├── mods/
│   └── <mod>/
│       ├── workshop.json
│       ├── previewimage.png
│       ├── core_mod.ff
│       ├── zm_mod.ff
│       └── ...
└── usermaps/
    └── <map>/
        ├── previewimage.png
        ├── loadingimage.png
        ├── workshop.json
        ├── <map>.ff
        ├── <map>.xpak
        ├── <lang>_<map>.ff
        ├── <lang>_<map>.xpak
        ├── snd/
        │   └── <lang>/
        │       ├── <map>.<lang>.sabl
        │       └── <map>.<lang>.sabs
        └── video/
            └── <name>.mkv
```

Maps and mods on a USB drive or extended storage use the same `usermaps/` and `mods/` layout in a `BO3-Customs` folder at the root of the drive.

</details>

## Building

- **The SPRX**: VS 2022, PS4 SDK 12.00, .NET 10.0 SDK and Python 3.
- **The tool**: .NET 10.0 SDK.

```text
cd Tool/Porter
dotnet publish src/FFPorter.Desktop.T7/FFPorter.Desktop.T7.csproj -p:PublishProfile=FolderProfile
```

The single exe lands in `Tool/Porter/publish/`.

## Credits

- **ItsJokerZz** — SPRX and port development.
- **flatz** — `downgrade_elf.py` and `make_fself.py`.

## License

GPL-3.0-or-later — see [LICENSE](LICENSE).
