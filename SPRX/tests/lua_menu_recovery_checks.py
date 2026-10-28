#!/usr/bin/env python3
"""Run the production recovery callbacks under Lua 5.1; no BO3 runtime emulation."""
from pathlib import Path
import subprocess
import sys

root = Path(__file__).resolve().parents[2]
mods = (root / "Lua/ui_scripts/mods.lua").read_text(encoding="utf-8")
maps = (root / "Lua/ui_scripts/mapselect.lua").read_text(encoding="utf-8")
lua = sys.argv[1]

def section(source, start, end):
    return source[source.index(start):source.index(end, source.index(start))]

mod_fixture = r'''
assert(loadfile("Lua/ui_scripts/mods.lua"))
local writes,delegated=0,0
local UI={}
local engine={SetDvar=function(name,value) assert(name=="bo3customs_mod" and type(value)=="string"); writes=writes+1 end}
local function Controller(value) return value or 0 end
local function Log() end
local function StatusText() return "host owns selection" end
local function ChosenList() return BO3CustomsChosenMods end
local function Same(a,b) return string.lower(a)==string.lower(b) end
local function ModelText(element,key) return element[key] end
UI.SetStatus=function() end
UI.SetChosen=function(list) BO3CustomsChosenMods=list end
UI.RefreshChecks=function() end
UI.RefreshStates=function() end
Mods_Unload=function(...) delegated=delegated+1; return "original",select("#",...) end
Mods_LoadMod=function(...) delegated=delegated+1; return "original load",select("#",...) end
'''
mod_fixture += section(mods, "function UI.Choose(", "function UI.SetChosen(")
mod_fixture += section(mods, "function UI.Toggle(", "function UI.Unload(")
mod_fixture += section(mods, "function UI.Unload(", "function UI.Open(")
mod_fixture += section(mods, "local ownedActions = {}", 'Define("Mods_Enabled"')
mod_fixture += section(mods, 'Define("Mods_LoadMod"', 'Define("Mods_RefreshListMods"')
mod_fixture += section(mods, "for name, action in pairs(ownedActions) do", '\nRefreshLobby()\nLog("installed")')
mod_fixture += r'''
local menu={}
UI.menu=menu
BO3CustomsChosenMods={}
Mods_LoadMod(menu,{folder="ds4c"},0)
assert(writes==1 and BO3CustomsChosenMods[1]=="ds4c" and delegated==0)
Mods_Unload(menu,nil,0)
assert(writes==2 and #BO3CustomsChosenMods==0 and delegated==0)
BO3CustomsChosenMods={"personal"}; BO3CustomsSessionMods={"host"}
Mods_Unload(menu,nil,0)
assert(writes==2 and BO3CustomsChosenMods[1]=="personal")
BO3CustomsSessionMods=nil
local a,b=Mods_Unload({},nil,0,"param",{})
assert(a=="original" and b==5 and delegated==1 and writes==2)
assert(Mods_LoadMod({},nil,0)=="original load" and delegated==2)
-- Widget refresh errors must not prevent the native deselection request.
UI.SetChosen=function() error("broken mod widget") end
assert(not pcall(Mods_Unload,menu,nil,0))
assert(writes==3)
BO3CustomsChosenMods={"ds4c"}
assert(not pcall(UI.Toggle,menu,0,"ds4c"))
assert(writes==4)
print("PASS: unload routes only owned menus, delegates other contexts, protects host choices and queues before widget refresh")
'''
subprocess.run([lua, "-"], input=mod_fixture, text=True, cwd=root, check=True)

map_fixture = r'''
assert(loadfile("Lua/ui_scripts/mapselect.lua"))
local Tabs={lists={}}
local choosing,owned=false,false
local function Choosing() return choosing end
local function Zombies() return 0 end
local function ModOwns() return owned end
local function LogOnce() end
local ZMMenuFile="zm menu"
local function StockMaps() return CoD.mapsTable end
local PrepareCategories,PrepareMaps=function(p,c,l) return p(c,l) end,function(p,c,l) return p(c,l) end
CoD={mapsTable={
  zm_good={session_mode=0,dlc_pack=0,unique_id=1,mapName="Good"},
  zm_custom={session_mode=0,dlc_pack=0,unique_id=2,mapName="Custom",previewImage={invalid=true}},
  zm_locked={session_mode=0,dlc_pack=1,unique_id=3},
  zm_hidden={session_mode=0,dlc_pack=-1,unique_id=4},
  mp_other={session_mode=1,dlc_pack=0,unique_id=5},
}}
Engine={IsMapValid=function(id) return id=="zm_good" or id=="zm_custom" end,Localize=function(s) return s end}
local prepared=0
ListHelper_Prepare=function(list,controller,name,prepare)
  prepared=prepared+1; list.rows=prepare(controller,list); return "native helper"
end
'''
map_fixture += section(maps, "local function BuildWith(", "local function WrapMapsList(")
map_fixture += section(maps, "local function PrepareZMMaps(", "local function Install()")
map_fixture += r'''
assert(WrapListPrepare())
local list={}
local full=CoD.mapsTable
local ok=pcall(BuildWith,{},function() error("missing dependency") end)
assert(not ok and CoD.mapsTable==full)
local result=ListHelper_Prepare(list,0,"ZMMapsList",function() error("function expected instead of nil") end)
assert(result=="native helper" and #list.rows==2 and CoD.mapsTable==full)
assert(list.rows[1].properties.mapId=="zm_good" and list.rows[2].properties.mapId=="zm_custom")
assert(list.rows[2].models.Image=="playlist_standard" and not list.rows[2].properties.purchasable)
local healthy={{models={custom="keep"}}}
ListHelper_Prepare(list,0,"ZMMapsList",function() return healthy end)
assert(list.rows==healthy)
choosing=true
assert(not pcall(ListHelper_Prepare,list,0,"ZMMapsList",function() error("playlist failure") end))
choosing=false
assert(not pcall(ListHelper_Prepare,list,0,"ChangeMapMaps",function() error("MP failure") end))
print("PASS: failed ZM list recovery retains valid stock/custom map IDs, excludes locked/hidden/MP maps, preserves successful builders and restores map tables")
'''
subprocess.run([lua, "-"], input=map_fixture, text=True, cwd=root, check=True)
