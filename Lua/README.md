# BO3-Customs editable Lua companions

This folder contains editable copies of the supplied companion bundle's Mods
and map-selection scripts. Deploy them with the remaining five ui_scripts files
and all 39 precompiled lui files from that bundle. They are companion files,
not code embedded in the SPRX.

Copy lui/ and ui_scripts/ to /data/BO3-Customs. That internal location always
has priority over root-level BO3-Customs folders on mounted external drives.
The native loader checks ext0 through ext7, then usb0 through usb7. No roots
configuration is required. It does not recursively scan drives. Fully restart
BO3 after replacing cached companion files.

## Recovery behavior

mapselect.lua retains successful offline Zombies map-list builders. If a
builder fails, it attempts a replacement list using valid stock/custom map IDs
and stock row fields. Invalid, hidden and unavailable rows are excluded. This
does not repair a mod's gameplay scripts or change online playlist recovery.
The row contract follows the public T7LuaRepo ZMMapsList source:
https://github.com/KingslayerKyle/T7LuaRepo/blob/main/Dumps/PC_Ship_2025-05-31/ui/uieditor/datasources.lua

mods.lua routes load/unload actions through Customs only while its Mods menu
owns the action. Otherwise an existing handler retains control. Deselection
queues the native action before rebuilding widgets, so a broken widget cannot
prevent that request. Physical unloading still uses the existing apply/menu-close
process. Optional session-selection globals are consumed only if published by
the matching native loader; this PR does not add host-download activation.

## Validation

Run the production callback checks with an existing Lua 5.1 interpreter:

    python3 SPRX/tests/lua_menu_recovery_checks.py /path/to/lua

These checks parse the complete scripts and execute the recovery callbacks
against controlled UI fixtures. They do not emulate the BO3 engine. Console
testing is still needed for menu lifecycle and compatibility with other mods.
