# Changelog

Changes to the plugin (`BO3-Customs.sprx`) and the tool (PS4 .FF Porter). Newest first.

## 4.00

Everything since [v3.00][3.00].

### Plugin

**Added**
- Mods that need a PC DLL ***may work***, though not all
- Mods and maps save their files (stats, settings) in `/data/BO3-Customs/saves/`.
- PS4 keyboard for mod text boxes.
- The quit command closes the game.
- Touchpad mouse in menus and the pause menu.
- MODS menu shows each mod's picture and description.
- Right stick scrolls map and mod descriptions.
<br>
- LAN: download screen shows the host's read and upload speeds and your download and write speeds.
- LAN: downloads skip other languages' files.
- LAN: cards next to players missing the map: "MISSING MAP", "MISSING DLC" or "DOWNLOADING 45%".
- LAN: the host's map list shows how many players are missing each map.
- LAN: player menu to promote to host, kick, ban or mute (replaces the PSN profile).
- LAN: promoting switches to Shadows of Evil first if anyone is missing the map.
- LAN: starting while players are still downloading asks first.
- LAN: ping on the scoreboard.

**Changed**
- No mod limit beyond what the game has room for.
- Mods start unticked every launch.
- Max FPS 0 removes the cap.
- In a match the touchpad opens the scoreboard; the pointer only works when paused.
- Mod scripts, menus and PC-only menu files take priority, as on PC.

**Fixed**
- Mod freezes, crashes and broken menus; a mod whose menus fail is turned off.
- "UI Error" opening controller settings.
- "UI Error 9813" joining a Zombies LAN lobby.
- Touchpad pointer did nothing in menus.
- LAN downloads between homes: they now go through the game's connection and resume.
- LAN downloads were slow, flooded the host's upload and missed files in deeper mod folders.
- "Could not download" notices for maps the lobby only passed through.
- "Client/Server Index Mismatch" joining with your own mods loaded.

### Tool

**Added**
- Warning before converting a mod that needs a PC DLL.
- GPU video: map videos encode on NVIDIA or AMD cards, about 7x faster.
- Ctrl+A in the installed maps and mods picker.
- Multi-core switch in Settings.

**Changed**
- Converted maps go to `exports/usermaps/`, mods to `exports/mods/`.
- About twice as fast on big maps.
- Time left is far more accurate and learns your PC's speed.
- A paused Community download no longer holds up the others.
- Async is now called Parallel.

**Fixed**
- Every map failed on PCs without AVX.
- Every map failed on PCs without the Visual C++ runtime ("acts could not decompile script"); it now ships with the tool.
- "Guarded zone allocation failed" on PCs with less memory.
- "No PC shader reflection" on slow PCs or with antivirus scanning.
- Sending to the console failed on most FTP servers.
- Cancel was ignored between a Community download and its send.
- "Waiting" showed in green on some rows.
- An unreadable movie was reported as a GPU encoder failure.

## 3.00

Everything since [v2.00][2.00].

### Plugin

**Added**
- Mod support, with up to 6 mods loaded at once.
- A MODS menu like the PC one.
- LAN games: joining consoles download the host's map and mods.
- Uninstall a map from map select (Square).
- Long map names and descriptions scroll.
- Missing functions from PC only GSC files.

**Changed**
- Map descriptions are cleaned of Steam formatting.
- Binds, keyboard, mouse, graphics settings and mod variables share one `config.cfg`.
- Mod checks in map scripts follow the loaded mod, as on PC.
- The load message also counts mods.
- The debug build logs more, and also to the kernel log (PS5 too).

**Fixed**
- "Mouse connected" message with no mouse plugged in.
- Freeze with looping audio on maps whose effects ask for a negative number of particles.

### Tool

**Added**
- A missing DLL causing conversion to fail.
- Community tab: download maps and mods that are already converted.
- Converted tab: every converted map and mod in the output folder.
- Installed tab: what is on the console and its drives.
- Console support: save the IP, send with one button, or turn on Auto-FTP.
- Console files: Settings sends `Console.zip` and the newest SPRX.
- The tool updates itself from GitHub releases.
- Async and Multi-conversion (2 to 4 items at once).
- Settings window.
- Add menu with a picker for installed maps and mods.
- Mods convert as mods.
- Total size of the queue.
- Cache size when hovering Clear cache.
- A second launch hands its files to the open window.
- Logo and icon.
- Time left for each conversion and for the queue.
- Pause, resume and cancel for each map or mod being converted.
- Tessellation shaders and more shader instructions convert.
- The interface scales to the screen, with a size option in Settings.

**Changed**
- New look: side navigation, bigger text, more room, orange accent.
- The queue is one list; each row opens its own report.
- Fidelity score: 99% means everything came across, 100% an exact copy.
- Texture detail values follow the console's own rule.
- Faster conversions with the whole game installed.
- Scripts keep their PC-only calls; the plugin now runs them. Needs the newest SPRX.
- Mods use the game's own shaders instead of their PC copies.
- All text fits on one line.

**Fixed**
- Animated props stretched across the screen.
- Effects drawn as huge streaky sheets. Convert older maps again.
- A decal render state.
- Sound banks with one-frame looping sounds.
- Models and textures on maps whose PC files hold console-built data.

### Project

**Added**
- Off-screen checks for the tool.
- This changelog.

**Changed**
- License is now GPL-3.0-or-later (was MIT).
- Console files carry a rebuilt `zm_levelcommon`.

## Older releases

<details>
<summary><b>2.00</b> · October 1, 2026</summary>

[Release][2.00] · [`fd6d5ac`][fd6d5ac]

### Plugin

**Added**
- Keyboard and mouse support.
- New graphics options.
- "Refresh Maps" button in map select.
- "Restart" button in the pause menu.
- Custom maps load from other drives.
- Support for other languages.

**Changed**
- Split-screen fills the whole screen.

**Fixed**
- Custom maps in LAN games.
- Crashes on various maps.
- Powerup icons not showing.
- Preview images in map select.

### Tool

**Added**
- Clear cache button.
- Full progress while a map converts.

**Changed**
- The PS4 SDK is no longer needed.
- Better port accuracy.
- One file now, `PS4 .FF Porter.exe`, instead of `Tool.zip`.

**Fixed**
- Audio conversion.
- Various other issues.

### Project

**Changed**
- `Console.zip` has updated scripts.
- README updated.

</details>

<details>
<summary><b>1.51</b> · September 23, 2026</summary>

[Release][1.51] · [`22a4d46`][22a4d46] · [`edcfe68`][edcfe68] · [`a928ee8`][a928ee8]

### Tool

**Changed**
- A dump of the PC game is no longer needed.

### Project

**Changed**
- README updated.

</details>

<details>
<summary><b>1.50</b> · September 23, 2026</summary>

[Release][1.50] · [`6b3e0c7`][6b3e0c7] · [`52fe422`][52fe422]

### Plugin

**Fixed**
- "UI Error" on maps whose menus use PC-only scripts.

### Tool

**Added**
- Ready-made downloads: `Tool.zip` and `Console.zip`.

**Changed**
- The tool ships the game data it needs.

### Project

**Changed**
- README rewritten as a getting-started guide.

</details>

<details>
<summary><b>1.00</b> · September 22, 2026</summary>

[Release][1.00] · [`e5dd776`][e5dd776] · [`926e686`][926e686] · [`edf5ea8`][edf5ea8]

First release.

</details>

[3.00]: https://github.com/ItsJokerZz/PS4-BO3-Customs/releases/tag/v3.00
[2.00]: https://github.com/ItsJokerZz/PS4-BO3-Customs/releases/tag/v2.00
[1.51]: https://github.com/ItsJokerZz/PS4-BO3-Customs/releases/tag/v1.51
[1.50]: https://github.com/ItsJokerZz/PS4-BO3-Customs/releases/tag/v1.50
[1.00]: https://github.com/ItsJokerZz/PS4-BO3-Customs/releases/tag/v1.00
[fd6d5ac]: https://github.com/ItsJokerZz/PS4-BO3-Customs/commit/fd6d5ac
[22a4d46]: https://github.com/ItsJokerZz/PS4-BO3-Customs/commit/22a4d46
[edcfe68]: https://github.com/ItsJokerZz/PS4-BO3-Customs/commit/edcfe68
[a928ee8]: https://github.com/ItsJokerZz/PS4-BO3-Customs/commit/a928ee8
[52fe422]: https://github.com/ItsJokerZz/PS4-BO3-Customs/commit/52fe422
[e5dd776]: https://github.com/ItsJokerZz/PS4-BO3-Customs/commit/e5dd776
[926e686]: https://github.com/ItsJokerZz/PS4-BO3-Customs/commit/926e686
[edf5ea8]: https://github.com/ItsJokerZz/PS4-BO3-Customs/commit/edf5ea8
[6b3e0c7]: https://github.com/ItsJokerZz/PS4-BO3-Customs/commit/6b3e0c7
