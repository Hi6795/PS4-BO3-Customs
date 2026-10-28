#include "headers.hpp"
#include "t7_lua.hpp"
#include "t7_maps.hpp"
#include "t7_config.hpp"
#include "t7_share_lua.hpp"
#include "t7_lua_files.hpp"
#include <system_service.h>

#include <atomic>

namespace T7Lua
{
constexpr uintptr_t kLuaState        = 0xE9831D0;
constexpr uintptr_t kUiReady         = 0xF7969C8;
constexpr uintptr_t kLockWords       = 0xCF60A50;
constexpr int32_t   kUiLock          = 53;
constexpr uintptr_t kHksLoad         = 0xC41D70;
constexpr uintptr_t kHksBufferReader = 0xC41D50;
constexpr uintptr_t kLuaPcall        = 0xC27C40;
constexpr uintptr_t kLuaGrowStack    = 0xC1BC40;
constexpr uintptr_t kDvarFind        = 0xFB6ED0;
constexpr uintptr_t kDvarString      = 0xFB69C0;
constexpr uintptr_t kDvarSetByName   = 0xFBC5A0;
constexpr uintptr_t kFindXAsset      = 0x8591B0;
constexpr uintptr_t kExecFindReturn  = 0xEE77D9;
constexpr int       kLuiMaxFolders   = 5;
constexpr uintptr_t kLuiAddMenu      = 0x11CDD10;
constexpr uintptr_t kLuaRequire      = 0xDB5320;
constexpr uintptr_t kFrameLimitSkip  = 0xF0BBF3;
constexpr uintptr_t kSplitNarrow     = 0x6FD736;
constexpr uintptr_t kSplitLayouts    = 0x16094F0;
constexpr uintptr_t kViewValues      = 0x5E28D0;
constexpr uintptr_t kCgArray         = 0x2CFBA40;
constexpr uintptr_t kLocalClients    = 0x35EBFC0;
constexpr uintptr_t kCurrentMap      = 0xCD01DE0;
constexpr uintptr_t kTanf            = 0x121AA40;
constexpr uintptr_t kAtanf           = 0x121AA60;

constexpr uintptr_t kCgSize        = 3418304;
constexpr uintptr_t kCgAnimLens    = 0x2D8964;
constexpr uintptr_t kCgAdsFraction = 0x11ABCC;
constexpr uintptr_t kCgTanHalfFovX = 0x131DB8;
constexpr uintptr_t kCgTanHalfFovY = 0x131DBC;
constexpr uintptr_t kCgMainTanFovY = 0x131DC0;
constexpr uintptr_t kCgFovX        = 0x131DC4;

constexpr size_t  kDvarType     = 0x14;
constexpr size_t  kDvarValue    = 0x20;
constexpr int32_t kDvarTypeBool = 1;

constexpr const char* kSplitScreenProxy = "bo3customs_splitscreen";
constexpr const char* kSplitScreenDvar  = "splitscreen_horizontal";

constexpr float kStockFov = 65.0f;
constexpr float kDegToRad = 0.017453292f;

constexpr int32_t kRawFileType = 47;

constexpr size_t kStateGlobal    = 16;
constexpr size_t kStateTop       = 72;
constexpr size_t kStateStackLast = 88;
constexpr size_t kStateStack     = 96;
constexpr size_t kGlobalSharing  = 472;
constexpr size_t kGlobalCompiler = 1384;

constexpr int32_t kSharingOn = 1;

static const char k_probeLua[] =
    "local engine = rawget(_G, 'Engine')\n"
    "local cod = rawget(_G, 'CoD')\n"
    "if rawget(_G, 'BO3CustomsMapTabs') ~= nil or engine == nil or cod == nil then return end\n"
    "if engine.GetCurrentMap == nil or engine.GetCurrentMap() ~= 'core_frontend' then return end\n"
    "if rawget(_G, 'DataSources') == nil or rawget(_G, 'DataSourceHelpers') == nil then return end\n"
    "engine.SetDvar('bo3customs_pending', 1)\n";

static const char k_graphicsProbeLua[] =
    "local engine = rawget(_G, 'Engine')\n"
    "local cod = rawget(_G, 'CoD')\n"
    "if rawget(_G, 'BO3CustomsGraphics') ~= nil or engine == nil or type(cod) ~= 'table' then return end\n"
    "if type(rawget(cod, 'OptionsUtility')) ~= 'table' or type(rawget(_G, 'ListHelper_Prepare')) ~= 'function' then return end\n"
    "engine.SetDvar('bo3customs_graphics_pending', 1)\n";

static const char k_restartProbeLua[] =
    "local engine = rawget(_G, 'Engine')\n"
    "if rawget(_G, 'BO3CustomsRestart') ~= nil or engine == nil or type(rawget(_G, 'CoD')) ~= 'table' then return end\n"
    "if type(rawget(_G, 'ListHelper_Prepare')) ~= 'function' then return end\n"
    "engine.SetDvar('bo3customs_restart_pending', 1)\n";

static const char k_mouseProbeLua[] =
    "local engine = rawget(_G, 'Engine')\n"
    "if rawget(_G, 'BO3CustomsMouse') ~= nil or engine == nil or type(rawget(_G, 'LUI')) ~= 'table' then return end\n"
    "engine.SetDvar('bo3customs_mouse_pending', 1)\n";

static const char k_modsDataProbeLua[] =
    "local engine = rawget(_G, 'Engine')\n"
    "if engine ~= nil and rawget(_G, 'BO3CustomMods') == nil then engine.SetDvar('bo3customs_mods_data', 1) end\n";

static const char k_menusProbeLua[] =
    "local engine = rawget(_G, 'Engine')\n"
    "if engine == nil then return end\n"
    "local ok, menu = pcall(function() return LUI.createMenu.main end)\n"
    "engine.SetDvar('bo3customs_menus', (ok and menu ~= nil) and 1 or 2)\n";

static const char k_modsProbeLua[] =
    "local engine = rawget(_G, 'Engine')\n"
    "local cod = rawget(_G, 'CoD')\n"
    "if rawget(_G, 'BO3CustomsModsUI') ~= nil or engine == nil or type(cod) ~= 'table' then return end\n"
    "local lobby = rawget(cod, 'LobbyMenus')\n"
    "if type(lobby) ~= 'table' or type(rawget(lobby, 'AddButtonsForTarget')) ~= 'function' then return end\n"
    "if rawget(_G, 'DataSources') == nil or rawget(_G, 'DataSourceHelpers') == nil then return end\n"
    "engine.SetDvar('bo3customs_mods_pending', 1)\n";

static const char k_refreshedLua[] =
    "local tabs = rawget(_G, 'BO3CustomsMapTabs')\n"
    "if tabs ~= nil and tabs.Refreshed ~= nil then tabs.Refreshed() end\n";

static const char k_lobbyMapsLua[] = R"LUA(
local G = _G
local engine = rawget(G, 'Engine')
if engine == nil then return end
local Lobby = rawget(G, 'BO3CustomsLobbyMaps')
if Lobby == nil then
  Lobby = { version = -1 }
  rawset(G, 'BO3CustomsLobbyMaps', Lobby)
end

local function trace(text)
  Lobby.pending = Lobby.pending or {}
  if #Lobby.pending < 50 then
    Lobby.pending[#Lobby.pending + 1] = 'lobby: ' .. string.sub(tostring(text), 1, 140)
  end
  local tabs = rawget(G, 'BO3CustomsMapTabs')
  if type(tabs) ~= 'table' or type(tabs.logs) ~= 'table' then return end
  while #Lobby.pending > 0 and #tabs.logs < 200 do
    tabs.logs[#tabs.logs + 1] = table.remove(Lobby.pending, 1)
  end
end
Lobby.trace = trace

local function ask(text)
  local ok, value = pcall(engine.GetDLCBitForMapName, 'bo3customs:' .. text)
  if ok and type(value) == 'number' then return value end
  return 0
end
Lobby.ask = ask

local function call(name, ...)
  local fn = rawget(G, name)
  if type(fn) ~= 'function' then return nil end
  local ok, value = pcall(fn, ...)
  if ok then return value end
  return nil
end

local function plainName(text)
  if type(text) ~= 'string' then return nil end
  text = string.gsub(text, '^%[[^%]]*%]', '')
  if text == '' then return nil end
  return text
end

local function register()
  if Lobby.registered then return end
  local okC, controller = pcall(engine.GetPrimaryController)
  controller = okC and controller or 0
  for _, getter in ipairs({ 'GetSelfGamertag', 'GetGamertagForController' }) do
    local fn = engine[getter]
    if type(fn) == 'function' then
      local ok, name = pcall(fn, controller)
      name = ok and plainName(name) or nil
      if name ~= nil then
        Lobby.registered = ask('me:' .. name) == 1
        return
      end
    end
  end
end

local function nextMap()
  local okG, global = pcall(engine.GetGlobalModel)
  if not okG or global == nil then return nil, nil end
  local okM, model = pcall(engine.GetModel, global, 'MapVote.mapVoteMapNext')
  if not okM or model == nil then return nil, nil end
  local okV, value = pcall(engine.GetModelValue, model)
  if okV and type(value) == 'string' and value ~= '' then return value, model end
  return nil, model
end
Lobby.nextMap = nextMap

local function memberName(element)
  if element == nil or type(element.getModel) ~= 'function' then return nil end
  local ok, model = pcall(element.getModel, element)
  if not ok or model == nil then return nil end
  for _, field in ipairs({ 'gamertag', 'clanTagAndGamertag' }) do
    local okX, child = pcall(engine.GetModel, model, field)
    if okX and child ~= nil then
      local okV, value = pcall(engine.GetModelValue, child)
      local name = okV and plainName(value) or nil
      if name ~= nil then return name end
    end
  end
  return nil
end

Lobby.hosting = function()
  return ask('lanhost') == 1
end

local function note(key, text)
  Lobby.notes = Lobby.notes or {}
  Lobby.noteCount = Lobby.noteCount or 0
  if Lobby.notes[key] == text or Lobby.noteCount >= 80 then return end
  Lobby.notes[key] = text
  Lobby.noteCount = Lobby.noteCount + 1
  trace(text)
end

local function lobbyMap()
  local ok, value = pcall(engine.DvarString, nil, 'ui_mapname')
  if ok and type(value) == 'string' and value ~= '' then return string.lower(value) end
  return (nextMap())
end

local function dlcMissing(owned, map)
  if type(owned) ~= 'number' or owned == 0 or map == nil then return false end
  local cod = rawget(G, 'CoD')
  local bits = type(cod) == 'table' and rawget(cod, 'BitUtility') or nil
  if type(bits) ~= 'table' or type(bits.IsBitwiseAndNonZero) ~= 'function' then return false end
  local okB, bit = pcall(engine.GetDLCBitForMapName, map)
  if not okB or type(bit) ~= 'number' or bit == 0 then return false end
  local okA, has = pcall(bits.IsBitwiseAndNonZero, owned, bit)
  return okA and has == false
end
Lobby.dlcMissing = dlcMissing

local function stockMissing(element, map, name)
  local owned = nil
  local ok, model = pcall(element.getModel, element)
  if ok and model ~= nil then
    local okD, child = pcall(engine.GetModel, model, 'dlcBits')
    if okD and child ~= nil then
      local okV, value = pcall(engine.GetModelValue, child)
      if okV and type(value) == 'number' and value ~= 0 then owned = value end
    end
  end
  if owned == nil and name ~= nil and Lobby.clientByName ~= nil then
    local client = Lobby.clientByName(name)
    if client ~= nil then owned = client.dlcBits end
  end
  return dlcMissing(owned, map)
end

local function cardLabel(value)
  if value == 1 then return 'missing' end
  if value == 2 then return 'missing the map pack' end
  if value >= 100 then return 'downloading' end
  if value == 0 then return 'has it or sent no map list' end
  return 'no lobby map'
end

local function cardValue(element)
  if element == nil or type(element.getModel) ~= 'function' or ask('lan') ~= 1 then return nil end
  local name = memberName(element)
  if name == nil then
    note('card name', 'card: a lobby row has no gamertag yet')
    return nil
  end
  local map = lobbyMap()
  local value = ask('card:' .. (map or '') .. '|' .. name)
  if value == -2 then
    value = 0
    if map ~= nil and stockMissing(element, map, name) then value = 2 end
  end
  note('card ' .. name, 'card for ' .. name .. ' on ' .. tostring(map) .. ': ' .. cardLabel(value))
  return value
end

Lobby.missing = function(element)
  local value = cardValue(element)
  if value == nil then return nil end
  return value == 1 or value == 2 or value >= 100
end

local function wrapRed()
  local original = rawget(G, 'ShowClientInRedForMissingDLC')
  if type(original) ~= 'function' or original == Lobby.red then return end
  Lobby.red = function(element, controller)
    if Lobby.markName ~= nil then
      local okTag, err = pcall(Lobby.markName, element)
      if not okTag then note('card attach', 'card attach failed: ' .. tostring(err)) end
    end
    if ask('lan') == 1 then return false end
    return original(element, controller)
  end
  rawset(G, 'ShowClientInRedForMissingDLC', Lobby.red)
end

local Tag = { row = 62, half = 8, pad = 6, every = 500 }

local function markName(widget)
  if widget == nil or widget.bo3CardState ~= nil then return end
  local label = widget.gamertag
  local lui = rawget(G, 'LUI')
  if label == nil or type(label.setText) ~= 'function' or type(lui) ~= 'table' then return end
  if type(lui.UIText) ~= 'table' or type(lui.UIImage) ~= 'table' or type(lui.UIElement) ~= 'table' then return end
  local card = lui.UIImage.new()
  card:setLeftRight(true, false, -Tag.row - 100, -Tag.row)
  card:setTopBottom(false, false, -Tag.half - 3, Tag.half + 3)
  card:setRGB(0.05, 0.05, 0.05)
  card:setAlpha(0)
  widget:addElement(card)
  local tag = lui.UIText.new()
  tag:setLeftRight(true, false, -Tag.row - 94, -Tag.row - 4)
  tag:setTopBottom(false, false, -Tag.half, Tag.half)
  tag:setTTF('fonts/RefrigeratorDeluxe-Regular.ttf')
  if Enum ~= nil and Enum.LUIAlignment ~= nil and Enum.LUIAlignment.LUI_ALIGNMENT_LEFT ~= nil then
    tag:setAlignment(Enum.LUIAlignment.LUI_ALIGNMENT_LEFT)
  end
  tag:setRGB(1, 0.3, 0.25)
  tag:setAlpha(0)
  tag:setText('MISSING MAP')
  widget:addElement(tag)
  local state = { tag = tag, card = card, shown = false, width = -1, text = 'MISSING MAP' }
  widget.bo3CardState = state
  Lobby.cards = (Lobby.cards or 0) + 1
  if Lobby.cards == 1 then trace('name cards attached to the lobby rows') end
  local function fit()
    local width = 0
    if type(tag.getTextWidth) == 'function' then
      local okW, measured = pcall(tag.getTextWidth, tag)
      if okW and type(measured) == 'number' then width = measured end
    end
    if width <= 0 then width = string.len(state.text) * 8 end
    width = math.floor(width + 0.5)
    if width ~= state.width then
      state.width = width
      local right = -Tag.row
      card:setLeftRight(true, false, right - width - 2 * Tag.pad, right)
      tag:setLeftRight(true, false, right - width - Tag.pad, right - Tag.pad + 2)
    end
  end
  local function show()
    local okV, value = pcall(cardValue, widget)
    if not okV then
      note('card error', 'card check failed: ' .. tostring(value))
      value = nil
    end
    local text = nil
    if value == 1 then
      text = 'MISSING MAP'
    elseif value == 2 then
      text = 'MISSING DLC'
    elseif type(value) == 'number' and value >= 100 then
      text = 'DOWNLOADING ' .. tostring(math.floor(value - 100 + 0.5)) .. '%'
    end
    local shown = text ~= nil
    if shown and text ~= state.text then
      state.text = text
      tag:setText(text)
      if value >= 100 then tag:setRGB(1, 0.6, 0.1) else tag:setRGB(1, 0.3, 0.25) end
    end
    if shown then fit() end
    if shown ~= state.shown then
      state.shown = shown
      tag:setAlpha(shown and 1 or 0)
      card:setAlpha(shown and 0.8 or 0)
    end
  end
  local set = label.setText
  label.setText = function(self, ...)
    local result = set(self, ...)
    pcall(show)
    return result
  end
  local ticker = lui.UIElement.new()
  ticker:setLeftRight(true, false, 0, 1)
  ticker:setTopBottom(true, false, 0, 1)
  ticker:setAlpha(0)
  widget:addElement(ticker)
  local function schedule()
    ticker:beginAnimation('bo3_missing', Tag.every, false, false, CoD.TweenType.Linear)
    ticker:setAlpha(0)
  end
  ticker:registerEventHandler('transition_complete_bo3_missing', function()
    pcall(show)
    schedule()
  end)
  schedule()
  pcall(show)
end
Lobby.markName = markName

local function wrapNames()
  local cod = rawget(G, 'CoD')
  local class = type(cod) == 'table' and rawget(cod, 'LobbyMemberGamertag') or nil
  if type(class) ~= 'table' or type(class.new) ~= 'function' or rawget(class, 'bo3CardHook') ~= nil then return end
  if type(rawget(G, 'LUI')) ~= 'table' or LUI.UIElement == nil then return end
  local new = class.new
  rawset(class, 'bo3CardHook', true)
  class.new = function(...)
    local widget = new(...)
    if widget ~= nil then
      local ok, err = pcall(markName, widget)
      if not ok then note('card attach', 'card attach failed: ' .. tostring(err)) end
    end
    return widget
  end
end

local function lanGame()
  local okI, inGame = pcall(engine.IsInGame)
  return okI and inGame == true and call('IsLive') ~= true
end

local function wrapPing()
  local value = rawget(G, 'GetScoreboardPingValueAlpha')
  if type(value) == 'function' and value ~= Lobby.pingValue then
    Lobby.pingValue = function(alpha, ...)
      if lanGame() then return alpha end
      return value(alpha, ...)
    end
    rawset(G, 'GetScoreboardPingValueAlpha', Lobby.pingValue)
  end
  local bars = rawget(G, 'GetScoreboardPingBarAlpha')
  if type(bars) == 'function' and bars ~= Lobby.pingBars then
    Lobby.pingBars = function(ping, ...)
      if lanGame() then return 0 end
      return bars(ping, ...)
    end
    rawset(G, 'GetScoreboardPingBarAlpha', Lobby.pingBars)
  end
  local cod = rawget(G, 'CoD')
  local row = type(cod) == 'table' and rawget(cod, 'ScoreboardRowWidget') or nil
  if type(row) == 'table' and type(row.new) == 'function' and rawget(row, 'bo3Ping') == nil then
    local new = row.new
    rawset(row, 'bo3Ping', true)
    row.new = function(...)
      local element = new(...)
      if element ~= nil and lanGame() then
        pcall(function()
          if element.PingText ~= nil then element.PingText:setAlpha(1) end
        end)
      end
      return element
    end
  end
end

local function lobbyTypes()
  local enum = rawget(G, 'Enum')
  if type(enum) ~= 'table' or type(enum.LobbyType) ~= 'table' then return nil end
  return enum.LobbyType
end

local function lobbyOf(xuid)
  local types = lobbyTypes()
  if xuid == nil or types == nil or type(engine.LobbyIsPlayerInLobby) ~= 'function' then return nil end
  for _, kind in ipairs({ types.LOBBY_TYPE_GAME, types.LOBBY_TYPE_PRIVATE }) do
    local ok, inside = pcall(engine.LobbyIsPlayerInLobby, kind, xuid)
    if ok and inside then return kind end
  end
  return nil
end

local function sessionClients()
  local enum = rawget(G, 'Enum')
  local modules = type(enum) == 'table' and enum.LobbyModule or nil
  local types = lobbyTypes()
  if type(modules) ~= 'table' or types == nil or type(engine.LobbyGetSessionClients) ~= 'function' then
    return nil, 'the lobby player list is not available'
  end
  local module = modules.LOBBY_MODULE_CLIENT
  if Lobby.hosting() then module = modules.LOBBY_MODULE_HOST end
  local ok, data = pcall(engine.LobbyGetSessionClients, module, types.LOBBY_TYPE_GAME)
  if not ok then return nil, tostring(data) end
  if type(data) ~= 'table' or type(data.sessionClients) ~= 'table' then return nil, 'the lobby sent no player list' end
  return data.sessionClients
end

Lobby.clientByName = function(name)
  local clients = sessionClients()
  if clients == nil or name == nil then return nil end
  for _, client in ipairs(clients) do
    if type(client) == 'table' and plainName(client.gamertag) == name then return client end
  end
  return nil
end

Lobby.stockLacking = function(map)
  local clients = sessionClients()
  if clients == nil then return 0 end
  local count = 0
  for _, client in ipairs(clients) do
    if type(client) == 'table' and client.isHost ~= true and client.isLocal ~= true and dlcMissing(client.dlcBits, map) then
      count = count + 1
    end
  end
  if count > 0 then note('pack ' .. tostring(map), tostring(count) .. ' player(s) lack the map pack for ' .. tostring(map)) end
  return count
end

local function clientFor(xuid, gamertag)
  local clients = sessionClients()
  if clients == nil then return nil end
  local plain = plainName(gamertag)
  for _, client in ipairs(clients) do
    if type(client) == 'table' then
      local same = false
      if xuid ~= nil and client.xuid ~= nil then
        local ok, equal = pcall(function() return client.xuid == xuid end)
        same = ok and equal == true
      end
      if not same and plain ~= nil then same = plainName(client.gamertag) == plain end
      if same then return client end
    end
  end
  return nil
end

local function localized(ref)
  local ok, text = pcall(engine.Localize, ref)
  if ok and type(text) == 'string' and text ~= '' then return text end
  return ref
end

local function closeMenu(menu, controller)
  if menu ~= nil then pcall(function() menu:goBack(controller) end) end
end

local function kickPlayer(controller, kind, xuid)
  local enum = rawget(G, 'Enum')
  local reason = enum and enum.LobbyDisconnectClient and enum.LobbyDisconnectClient.LOBBY_DISCONNECT_CLIENT_KICK
  return pcall(engine.LobbyDisconnectClient, controller, kind, xuid, reason)
end

local function lacksLobbyMap(gamertag, client)
  local name = plainName(gamertag)
  if name == nil and client ~= nil then name = plainName(client.gamertag) end
  if name == nil then return false end
  local map = lobbyMap()
  local value = ask('card:' .. (map or '') .. '|' .. name)
  if value == -2 then
    if client == nil then client = Lobby.clientByName(name) end
    return client ~= nil and dlcMissing(client.dlcBits, map)
  end
  return value == 1 or value >= 100
end

local function clock()
  local ok, ms = pcall(engine.milliseconds)
  if ok and type(ms) == 'number' then return ms end
  return 0
end

local function handOver(param, client)
  local id = client ~= nil and client.xuidstr or nil
  local types = lobbyTypes()
  local kind = param.kind or (types ~= nil and types.LOBBY_TYPE_GAME) or 1
  local done = false
  if type(id) == 'string' and id ~= '' then
    local query = string.format('promote:%d|%d|%s', math.floor(tonumber(param.controller) or 0), math.floor(tonumber(kind) or 1), id)
    done = ask(query) == 1
  end
  trace('promote ' .. tostring(param.gamertag) .. ' (id ' .. tostring(id) .. ', lobby type ' .. tostring(kind) .. '): ' .. (done and 'handing the lobby over' or 'refused'))
end

local function lackingPlayers()
  local names = {}
  local clients = sessionClients()
  if clients == nil then return names end
  for _, client in ipairs(clients) do
    if type(client) == 'table' and client.isLocal ~= true and client.isHost ~= true and lacksLobbyMap(client.gamertag, client) then
      names[#names + 1] = plainName(client.gamertag) or '?'
    end
  end
  return names
end

local function promoteAction(self, element, controller, param, menu)
  closeMenu(menu, controller)
  local client = clientFor(param.xuid, param.gamertag)
  local lacking = lackingPlayers()
  if #lacking == 0 and not lacksLobbyMap(param.gamertag, client) then
    handOver(param, client)
    return
  end
  if #lacking == 0 then lacking[1] = plainName(param.gamertag) or '?' end
  local current = lobbyMap() or ''
  local map = 'zm_zod'
  if string.sub(current, 1, 3) == 'mp_' then map = 'mp_sector' end
  local setMap = rawget(G, 'SetMap')
  if current == map or type(setMap) ~= 'function' then
    trace('promote ' .. tostring(param.gamertag) .. ': refused, ' .. table.concat(lacking, ', ') .. ' do not have ' .. current)
    return
  end
  local ok, err = pcall(setMap, param.controller or 0, map)
  trace('promote ' .. tostring(param.gamertag) .. ': ' .. table.concat(lacking, ', ') .. ' do not have ' .. current .. ', switching the lobby to ' .. map .. ' first' .. (ok and '' or (' failed: ' .. tostring(err))))
  if ok then Lobby.promoteLater = { param = param, at = clock() + 3000 } end
end

local function promoteLater()
  local pending = Lobby.promoteLater
  if pending == nil or clock() < pending.at then return end
  Lobby.promoteLater = nil
  if not Lobby.hosting() then return end
  local client = clientFor(pending.param.xuid, pending.param.gamertag)
  if lacksLobbyMap(pending.param.gamertag, client) then
    trace('promote ' .. tostring(pending.param.gamertag) .. ': refused, they do not have ' .. tostring(lobbyMap()))
    return
  end
  local lacking = lackingPlayers()
  if #lacking > 0 then
    trace('promote: ' .. table.concat(lacking, ', ') .. ' still do not have ' .. tostring(lobbyMap()) .. ' and will drop out')
  end
  handOver(pending.param, client)
end

local function kickAction(self, element, controller, param, menu)
  kickPlayer(param.controller, param.kind, param.xuid)
  trace('kicked ' .. tostring(param.gamertag))
  closeMenu(menu, controller)
end

local function banAction(self, element, controller, param, menu)
  local client = clientFor(param.xuid, param.gamertag)
  local id = client ~= nil and client.xuidstr or ''
  local name = plainName(param.gamertag) or (client ~= nil and plainName(client.gamertag)) or ''
  ask('ban:' .. tostring(id) .. '|' .. name)
  kickPlayer(param.controller, param.kind, param.xuid)
  trace('banned ' .. tostring(param.gamertag) .. ' (id ' .. tostring(id) .. ')')
  closeMenu(menu, controller)
end

local function lanDetails(controller)
  local list = {}
  local okRoot, root = pcall(engine.GetModelForController, controller)
  if not okRoot or root == nil then return list end
  local function value(name)
    local okM, model = pcall(engine.CreateModel, root, name)
    if not okM or model == nil then return nil end
    local okV, v = pcall(engine.GetModelValue, model)
    if okV then return v end
    return nil
  end
  local xuid = value('Social.selectedFriendXUID')
  local gamertag = value('Social.selectedFriendGamertag')
  if xuid == nil then return list end
  local okMe, me = pcall(engine.GetXUID64, controller)
  local kind = lobbyOf(xuid)
  local params = { controller = controller, xuid = xuid, gamertag = gamertag, kind = kind }
  local function add(text, action, last, disabled)
    list[#list + 1] = { models = { displayText = text },
      properties = { disabled = disabled == true, action = action, actionParam = params, isLastButtonInGroup = last == true } }
  end
  local other = not (okMe and me == xuid)
  if other and kind ~= nil and Lobby.hosting() then
    local target = clientFor(xuid, gamertag)
    local id = target ~= nil and target.xuidstr or nil
    local reachable = type(id) ~= 'string' or id == '' or ask('canhost:' .. id) == 1
    add(reachable and 'Promote to Host' or "Promote to Host (Can't Host)", promoteAction, false, not reachable)
    add('Kick from Lobby', kickAction)
    add('Ban from Lobby', banAction, true)
  end
  if other and kind ~= nil then
    local okMuted, muted = pcall(engine.IsPlayerMuted, controller, kind, xuid)
    if okMuted and muted then
      add(localized('MENU_UNMUTE_PLAYER'), rawget(G, 'UnMutePlayer'), true)
    else
      add(localized('MENU_MUTE_PLAYER'), rawget(G, 'MutePlayer'), true)
    end
  end
  trace('player menu for ' .. tostring(gamertag) .. ': ' .. tostring(#list) .. ' option(s), lobby type ' .. tostring(kind))
  return list
end

local function wrapDetails()
  local sources = rawget(G, 'DataSources')
  local setup = rawget(G, 'ListHelper_SetupDataSource')
  if type(sources) ~= 'table' or type(setup) ~= 'function' then return end
  local original = rawget(sources, 'SocialPlayerDetailsButtons')
  if type(original) ~= 'table' or rawget(original, 'bo3Lan') ~= nil then return end
  local okLan, lan = pcall(setup, 'SocialPlayerDetailsButtons', lanDetails, nil, nil, nil, original.getSpacerAfterRow)
  if not okLan or type(lan) ~= 'table' then return end
  rawset(sources, 'SocialPlayerDetailsButtons', setmetatable({ bo3Lan = true }, { __index = function(_, key)
    if ask('lan') == 1 then return lan[key] end
    return original[key]
  end }))
  trace('player menu ready for LAN lobbies')
end

local function wrapProfile()
  local original = rawget(G, 'OpenPlatformProfile')
  if type(original) ~= 'function' or original == Lobby.profileHook then return end
  Lobby.profile = original
  Lobby.profileHook = function(self, element, controller, param, menu, ...)
    local open = rawget(G, 'OpenOverlay')
    if type(param) ~= 'table' and ask('lan') == 1 and type(open) == 'function' then
      local ok, err = pcall(open, self, 'Social_PlayerDetailsPopup', controller, '', '')
      if not ok then trace('player menu failed to open: ' .. tostring(err)) end
      return
    end
    return original(self, element, controller, param, menu, ...)
  end
  rawset(G, 'OpenPlatformProfile', Lobby.profileHook)
end

local function enforceBans()
  if not Lobby.hosting() then return end
  local clients, why = sessionClients()
  if clients == nil then
    note('bans', 'ban check cannot read the players: ' .. tostring(why))
    return
  end
  local okC, controller = pcall(engine.GetPrimaryController)
  controller = okC and controller or 0
  Lobby.kicked = Lobby.kicked or {}
  local now = 0
  local okT, ms = pcall(engine.milliseconds)
  if okT and type(ms) == 'number' then now = ms end
  local types = lobbyTypes()
  local seen = 0
  for _, client in ipairs(clients) do
    if type(client) == 'table' and client.xuid ~= nil and client.isHost ~= true and client.isLocal ~= true then
      seen = seen + 1
      local id = type(client.xuidstr) == 'string' and client.xuidstr or ''
      local name = plainName(client.gamertag) or ''
      if ask('banned:' .. id .. '|' .. name) == 1 then
        local key = id .. '|' .. name
        if now - (Lobby.kicked[key] or -10000) > 3000 then
          Lobby.kicked[key] = now
          local kind = lobbyOf(client.xuid) or (types ~= nil and types.LOBBY_TYPE_GAME) or 1
          local ok = kickPlayer(controller, kind, client.xuid)
          trace('kicked banned player ' .. name .. (ok and '' or ' (the kick failed)'))
        end
      end
    end
  end
  note('bans', 'ban check sees ' .. tostring(seen) .. ' other player(s)')
end

local function wrapChallenges()
  local cod = rawget(G, 'CoD')
  local utility = type(cod) == 'table' and rawget(cod, 'ChallengesUtility') or nil
  if type(utility) ~= 'table' then return end
  local original = rawget(utility, 'GetDailyChallenge')
  if type(original) ~= 'function' or original == Lobby.dailyHook then return end
  Lobby.dailyHook = function(...)
    local ok, value = pcall(original, ...)
    if ok then return value end
    note('daily', 'daily challenge skipped, the stats are not loaded: ' .. tostring(value))
    return nil
  end
  rawset(utility, 'GetDailyChallenge', Lobby.dailyHook)
end

local PauseMenus = { StartMenu_Main = true, DOA_INGAME_PAUSE = true }

local function holdsPauseMenu(element, depth)
  local okC, child = pcall(function() return element:getFirstChild() end)
  child = okC and child or nil
  while child ~= nil do
    local okN, name = pcall(function() return child.menuName end)
    if okN and name ~= nil and PauseMenus[name] then return true end
    if depth > 1 and holdsPauseMenu(child, depth - 1) then return true end
    local okS, nextChild = pcall(function() return child:getNextSibling() end)
    child = okS and nextChild or nil
  end
  return false
end

local function watchPause()
  local lui = rawget(G, 'LUI')
  local roots = type(lui) == 'table' and rawget(lui, 'roots') or nil
  local open = false
  if type(roots) == 'table' then
    for _, root in pairs(roots) do
      if not open then open = holdsPauseMenu(root, 2) end
    end
  end
  if open ~= Lobby.pauseOpen then
    Lobby.pauseOpen = open
    ask('pausemenu:' .. (open and '1' or '0'))
  end
end

local function downloadingPlayers()
  local names = {}
  local clients = sessionClients()
  if clients == nil then return names end
  local map = lobbyMap()
  for _, client in ipairs(clients) do
    local name = type(client) == 'table' and client.isLocal ~= true and client.isHost ~= true and plainName(client.gamertag) or nil
    if name ~= nil then
      local value = ask('card:' .. (map or '') .. '|' .. name)
      if value >= 100 then
        names[#names + 1] = name .. ' (' .. tostring(math.floor(value - 100 + 0.5)) .. '%)'
      elseif value == 1 then
        names[#names + 1] = name
      end
    end
  end
  return names
end

local function guardLaunch(original)
  return function(self, element, controller, ...)
    if not Lobby.hosting() then return original(self, element, controller, ...) end
    local waiting = downloadingPlayers()
    local tabs = rawget(G, 'BO3CustomsMapTabs')
    local choose = type(tabs) == 'table' and rawget(tabs, 'ShowChoice') or nil
    if #waiting == 0 or type(choose) ~= 'function' then return original(self, element, controller, ...) end
    local count = #waiting
    local text = table.concat(waiting, ', ') .. (count == 1 and ' is' or ' are') .. ' still downloading the map. Start anyway?'
    local ok, shown = pcall(choose, self, controller, 'STILL DOWNLOADING', text, {
      { label = 'Start Anyway', run = function()
        trace('start: started while ' .. table.concat(waiting, ', ') .. ' downloaded')
        original(self, element, controller)
      end },
      { label = 'Wait', focus = true },
    })
    if not ok or not shown then
      trace('start: the download popup did not open (' .. tostring(shown) .. '), starting')
      return original(self, element, controller, ...)
    end
    trace('start: asked first, ' .. table.concat(waiting, ', ') .. ' still downloading')
  end
end

local function wrapLaunch()
  local original = rawget(G, 'LobbyLANLaunchGame')
  if type(original) ~= 'function' or original == Lobby.launchHook then return end
  Lobby.launchHook = guardLaunch(original)
  rawset(G, 'LobbyLANLaunchGame', Lobby.launchHook)
  local cod = rawget(G, 'CoD')
  local buttons = type(cod) == 'table' and rawget(cod, 'LobbyButtons') or nil
  local swapped = 0
  if type(buttons) == 'table' then
    for _, button in pairs(buttons) do
      if type(button) == 'table' and rawget(button, 'action') == original then
        rawset(button, 'action', Lobby.launchHook)
        swapped = swapped + 1
      end
    end
  end
  trace('start: download check on ' .. tostring(swapped) .. ' LAN start button(s)')
end

local function step(name, fn)
  local ok, err = pcall(fn)
  if not ok then note('step ' .. name, name .. ' failed: ' .. tostring(err)) end
end

Lobby.tick = function()
  if Lobby.pending ~= nil and #Lobby.pending > 0 then
    local tabs = rawget(G, 'BO3CustomsMapTabs')
    if type(tabs) == 'table' and type(tabs.logs) == 'table' then
      while #Lobby.pending > 0 and #tabs.logs < 200 do
        tabs.logs[#tabs.logs + 1] = table.remove(Lobby.pending, 1)
      end
    end
  end
  step('register', register)
  step('red check', wrapRed)
  step('name cards', wrapNames)
  step('ping', wrapPing)
  step('daily challenge', wrapChallenges)
  step('start check', wrapLaunch)
  step('pause menu', watchPause)
  step('player menu', wrapDetails)
  step('profile', wrapProfile)
  step('ban check', enforceBans)
  step('promote', promoteLater)
  local version = ask('mapsversion')
  if version ~= Lobby.version then
    Lobby.version = version
    local _, model = nextMap()
    if model ~= nil and type(engine.ForceNotifyModelSubscriptions) == 'function' then
      pcall(engine.ForceNotifyModelSubscriptions, model)
    end
  end
end

pcall(Lobby.tick)
)LUA";

static const char k_tickLua[] =
    "local tick = rawget(_G, 'BO3CustomsTick')\n"
    "if tick ~= nil then tick() end\n"
    "local lobby = rawget(_G, 'BO3CustomsLobbyMaps')\n"
    "if lobby ~= nil and lobby.tick ~= nil then pcall(lobby.tick) end\n";

static const char k_lobbyTickLua[] =
    "local lobby = rawget(_G, 'BO3CustomsLobbyMaps')\n"
    "if lobby ~= nil and lobby.tick ~= nil then pcall(lobby.tick) end\n";

#ifdef _DEBUG
static const char k_logFlushLua[] =
    "local tabs = rawget(_G, 'BO3CustomsMapTabs')\n"
    "local logs = type(tabs) == 'table' and tabs.logs or nil\n"
    "if type(logs) ~= 'table' or #logs == 0 then return end\n"
    "local take = {}\n"
    "local size = 0\n"
    "while #logs > 0 and #take < 12 do\n"
    "  local line = string.gsub(tostring(logs[1]), '[%c\"]', ' ')\n"
    "  if #take > 0 and size + #line > 600 then break end\n"
    "  take[#take + 1] = line\n"
    "  size = size + #line + 1\n"
    "  table.remove(logs, 1)\n"
    "end\n"
    "Engine.SetDvar('bo3customs_log', table.concat(take, '\\n'))\n";
#endif

static const char k_menuWatchLua[] =
    "local G = _G\n"
    "if rawget(G, 'BO3CustomsMenuWatch') ~= nil then return end\n"
    "local cod = rawget(G, 'CoD')\n"
    "if type(cod) ~= 'table' then return end\n"
    "local menu = cod.Menu\n"
    "if type(menu) ~= 'table' then return end\n"
    "local add = menu.AddToCurrMenuNameList\n"
    "local drop = menu.RemoveFromCurrMenuNameList\n"
    "if type(add) ~= 'function' or type(drop) ~= 'function' then return end\n"
    "rawset(G, 'BO3CustomsMenuWatch', 'trying')\n"
    "rawset(G, 'BO3CustomsPreGame', 0)\n"
    "rawset(G, 'BO3CustomsBlocking', 0)\n"
    "local function named(...)\n"
    "  for i = 1, select('#', ...) do\n"
    "    local v = select(i, ...)\n"
    "    if type(v) == 'string' then return v end\n"
    "  end\n"
    "  return nil\n"
    "end\n"
    "local function kind(name)\n"
    "  if string.find(name, 'PreGame', 1, true) ~= nil then return 'BO3CustomsPreGame' end\n"
    "  if string.find(name, 'StartMenu', 1, true) ~= nil\n"
    "    or string.find(name, 'Pause', 1, true) ~= nil\n"
    "    or string.find(name, 'Options', 1, true) ~= nil then return 'BO3CustomsBlocking' end\n"
    "  return nil\n"
    "end\n"
    "local function bump(name, by)\n"
    "  local key = kind(name)\n"
    "  if key ~= nil then\n"
    "    local n = (rawget(G, key) or 0) + by\n"
    "    if n < 0 then n = 0 end\n"
    "    rawset(G, key, n)\n"
    "    local apply = rawget(G, 'BO3CustomsApplyIsPC')\n"
    "    if apply ~= nil then apply() end\n"
    "  end\n"
    "end\n"
    "local ok = pcall(function()\n"
    "  menu.AddToCurrMenuNameList = function(...)\n"
    "    local name = named(...)\n"
    "    if name ~= nil then bump(name, 1) end\n"
    "    return add(...)\n"
    "  end\n"
    "  menu.RemoveFromCurrMenuNameList = function(...)\n"
    "    local name = named(...)\n"
    "    if name ~= nil then bump(name, -1) end\n"
    "    return drop(...)\n"
    "  end\n"
    "end)\n"
    "rawset(G, 'BO3CustomsMenuWatch', ok and 'on' or 'failed')\n";

static const char k_isPcLua[] =
    "local G = _G\n"
    "local apply = rawget(G, 'BO3CustomsApplyIsPC')\n"
    "if apply == nil then\n"
    "  apply = function()\n"
    "    local engine = rawget(G, 'Engine')\n"
    "    local cod = rawget(G, 'CoD')\n"
    "    if engine == nil or engine.GetCurrentMap == nil or type(cod) ~= 'table' then return end\n"
    "    if rawget(G, 'BO3CustomsIsPCWas') == nil then rawset(G, 'BO3CustomsIsPCWas', { was = cod.isPC }) end\n"
    "    local saved = rawget(G, 'BO3CustomsIsPCWas')\n"
    "    local inMap = tostring(engine.GetCurrentMap()) ~= 'core_frontend'\n"
    "    local pre = rawget(G, 'BO3CustomsPreGame') or 0\n"
    "    local block = rawget(G, 'BO3CustomsBlocking') or 0\n"
    "    if inMap and pre > 0 and block == 0 then rawset(cod, 'isPC', true)\n"
    "    else rawset(cod, 'isPC', saved.was) end\n"
    "  end\n"
    "  rawset(G, 'BO3CustomsApplyIsPC', apply)\n"
    "end\n"
    "apply()\n";

static const char k_pcUtilityLua[] =
    "local G = _G\n"
    "local cod = rawget(G, 'CoD')\n"
    "if type(cod) ~= 'table' then return end\n"
    "local function ready(util)\n"
    "  return type(util) == 'table' and type(rawget(util, 'GetOptionInfo')) == 'function'\n"
    "end\n"
    "local util = rawget(cod, 'PCUtil')\n"
    "if not ready(util) then\n"
    "  local tries = rawget(G, 'BO3CustomsPCUtilityTries') or 0\n"
    "  if tries >= 3 then return end\n"
    "  rawset(G, 'BO3CustomsPCUtilityTries', tries + 1)\n"
    "  if type(util) ~= 'table' and type(rawget(G, 'PCUtil')) == 'table' then rawset(cod, 'PCUtil', rawget(G, 'PCUtil')) end\n"
    "  pcall(function() package.loaded['ui.t7.utility.pcutility'] = nil end)\n"
    "  local ok, err = pcall(require, 'ui.t7.utility.pcutility')\n"
    "  util = rawget(cod, 'PCUtil')\n"
    "  local tabs = rawget(G, 'BO3CustomsMapTabs')\n"
    "  if type(tabs) == 'table' and type(tabs.logs) == 'table' then\n"
    "    tabs.logs[#tabs.logs + 1] = 'pcutility ' .. (ready(util) and 'loaded' or ('failed: ' .. tostring(err)))\n"
    "  end\n"
    "end\n"
    "if type(util) ~= 'table' then util = rawget(G, 'PCUtil') end\n"
    "if type(util) == 'table' then\n"
    "  rawset(G, 'PCUtil', util)\n"
    "  rawset(cod, 'PCUtil', util)\n"
    "end\n";

static const char k_pcUtilLua[] =
    "local cod = rawget(_G, 'CoD')\n"
    "local have = rawget(_G, 'PCUtil')\n"
    "if have ~= nil then\n"
    "  if type(cod) == 'table' and rawget(cod, 'PCUtil') == nil then rawset(cod, 'PCUtil', have) end\n"
    "  return\n"
    "end\n"
    "local util = {}\n"
    "setmetatable(util, { __index = function(t, k)\n"
    "  local made = {}\n"
    "  rawset(t, k, made)\n"
    "  return made\n"
    "end })\n"
    "rawset(_G, 'PCUtil', util)\n"
    "if type(cod) == 'table' then rawset(cod, 'PCUtil', util) end\n";

static const char k_globalAliasLua[] =
    "local G = _G\n"
    "local cod, lui, engine = rawget(G, 'CoD'), rawget(G, 'LUI'), rawget(G, 'Engine')\n"
    "local names = { 'OverlayUtility', 'OverlayTypes', 'DataSources', 'DataSourceHelpers', 'Menu',\n"
    "  'OptionsList', 'OptionInfo', 'CategoryFrame', 'ChooseDecal', 'Board', 'UIElement', 'UIImage',\n"
    "  'LUIButton', 'ListSetup', 'GoBack', 'CreateModel', 'GetModel', 'GetModelValue',\n"
    "  'GetModelForController', 'SetModelValue', 'SubscribeToModelAndUpdateState',\n"
    "  'UnsubscribeAndFreeModel', 'Localize', 'Dvar', 'RegisterImage', 'SetOptionValue',\n"
    "  'SendMenuResponse', 'OptionsUtility', 'ButtonPrompts', 'PlayerOptions' }\n"
    "local function pick(name)\n"
    "  local sources = { cod, lui, engine }\n"
    "  for i = 1, #sources do\n"
    "    if type(sources[i]) == 'table' then\n"
    "      local value = rawget(sources[i], name)\n"
    "      if value ~= nil then return value end\n"
    "    end\n"
    "  end\n"
    "  return nil\n"
    "end\n"
    "for i = 1, #names do\n"
    "  if rawget(G, names[i]) == nil then\n"
    "    local value = pick(names[i])\n"
    "    if value ~= nil then\n"
    "      rawset(G, names[i], value)\n"
    "    end\n"
    "  end\n"
    "end\n"
    "local stubs = { 'OverlayTypes', 'DataSources', 'DataSourceHelpers', 'OptionsList', 'OptionInfo',\n"
    "  'CategoryFrame', 'PlayerOptions', 'Overlays' }\n"
    "for i = 1, #stubs do\n"
    "  local name = stubs[i]\n"
    "  if rawget(G, name) == nil then\n"
    "    local shim = {}\n"
    "    setmetatable(shim, { __index = function(t, k)\n"
    "      local value = {}\n"
    "      rawset(t, k, value)\n"
    "      return value\n"
    "    end })\n"
    "    rawset(G, name, shim)\n"
    "  end\n"
    "end\n"
    "local function level()\n"
    "  local e = rawget(G, 'Engine')\n"
    "  if e == nil or e.GetCurrentMap == nil then return nil end\n"
    "  local ok, map = pcall(e.GetCurrentMap)\n"
    "  if ok and type(map) == 'string' and map ~= '' and map ~= 'core_frontend' then return map end\n"
    "  return nil\n"
    "end\n"
    "local function active()\n"
    "  local mod = rawget(G, 'BO3CustomsActiveMod')\n"
    "  if type(mod) == 'table' then return mod end\n"
    "  return nil\n"
    "end\n"
    "local function modName(key)\n"
    "  return function()\n"
    "    local mod = active()\n"
    "    if mod ~= nil and type(mod[key]) == 'string' and mod[key] ~= '' then return mod[key] end\n"
    "    if level() ~= nil then return 'usermaps' end\n"
    "    return ''\n"
    "  end\n"
    "end\n"
    "local function chosen()\n"
    "  local name = rawget(G, 'BO3CustomsChosenMod')\n"
    "  return type(name) == 'string' and name ~= ''\n"
    "end\n"
    "local mods = { Mods_IsUsingMods = function() return active() ~= nil or level() ~= nil or chosen() end,\n"
    "  Mods_UsingModsUgcName = modName('ugc'),\n"
    "  Mods_UsingModsInternalName = modName('internal'),\n"
    "  Mods_UsingModsVersion = function() return 0 end,\n"
    "  Mods_IsUsingUsermap = function() return level() ~= nil end,\n"
    "  Mods_UsingUsermapUgcName = function() return level() or '' end }\n"
    "for name, fn in pairs(mods) do\n"
    "  if rawget(G, name) == nil then rawset(G, name, fn) end\n"
    "end\n";

static const char k_zombieSeedLua[] =
    "local cod = rawget(_G, 'CoD')\n"
    "local zombie = nil\n"
    "if type(cod) == 'table' then zombie = rawget(cod, 'Zombie') end\n"
    "if type(zombie) ~= 'table' then return end\n"
    "if zombie.ZM_SUPPORT_BOARDS == nil then zombie.ZM_SUPPORT_BOARDS = { 'white' } end\n"
    "if zombie.ZM_SUPPORT_BOARDS_INDEX == nil then zombie.ZM_SUPPORT_BOARDS_INDEX = 1 end\n";

static const char k_lobbyParityLua[] =
    "local G = _G\n"
    "if rawget(G, 'BO3CustomsLobbyParity') ~= nil then return end\n"
    "local original = rawget(G, 'require')\n"
    "if type(original) ~= 'function' then return end\n"
    "rawset(G, 'BO3CustomsLobbyParity', true)\n"
    "local function inner(t)\n"
    "  if type(t) ~= 'table' then return nil end\n"
    "  local original = rawget(t, '_originalTable')\n"
    "  if type(original) == 'table' then return original end\n"
    "  return t\n"
    "end\n"
    "local function level()\n"
    "  local e = rawget(G, 'Engine')\n"
    "  if e == nil or e.GetCurrentMap == nil then return nil end\n"
    "  local ok, map = pcall(e.GetCurrentMap)\n"
    "  if ok and type(map) == 'string' and map ~= '' and map ~= 'core_frontend' then return map end\n"
    "  return nil\n"
    "end\n"
    "local function active()\n"
    "  local mod = rawget(G, 'BO3CustomsActiveMod')\n"
    "  if type(mod) == 'table' then return mod end\n"
    "  return nil\n"
    "end\n"
    "local function modField(key)\n"
    "  return function()\n"
    "    local mod = active()\n"
    "    if mod ~= nil and type(mod[key]) == 'string' and mod[key] ~= '' then return mod[key] end\n"
    "    if level() ~= nil then return 'usermaps' end\n"
    "    return ''\n"
    "  end\n"
    "end\n"
    "local function yes() return true end\n"
    "local function no() return false end\n"
    "local function nothing() end\n"
    "local utils = {\n"
    "  Mods_Enabled = yes,\n"
    "  Mods_IsModLoaded = function() return active() ~= nil end,\n"
    "  Mods_IsUGC = function() return active() ~= nil end,\n"
    "  Mods_IsUsingMods = function() return active() ~= nil or level() ~= nil end,\n"
    "  Mods_IsUsingUsermap = function() return level() ~= nil end,\n"
    "  Mods_Lists_IsInstalled = yes,\n"
    "  Mods_LoadMod = nothing,\n"
    "  Mods_UsingModsInternalName = modField('internal'),\n"
    "  Mods_UsingModsUgcName = modField('ugc'),\n"
    "  Mods_UsingModsVersion = function() return 0 end,\n"
    "  Mods_UsingUsermapUgcName = function() return level() or '' end,\n"
    "  IsStarterPack = no,\n"
    "  IsStarterPackParty = no,\n"
    "  IsCustomGameTarget = no,\n"
    "  IsPartyMemberMissingRequiredChunk = no,\n"
    "  isHost = yes,\n"
    "  isOnlineGame = no,\n"
    "  isRankedGame = no,\n"
    "  QuitGame = nothing,\n"
    "  SetNetworkMode = nothing,\n"
    "  ShowPasswordInput = nothing,\n"
    "  HandleSteamClientPasswordComplete = nothing,\n"
    "  GetSteamServerPasswordModel = nothing\n"
    "}\n"
    "local utilsDone = false\n"
    "local function patchUtils()\n"
    "  if utilsDone then return end\n"
    "  local lu = inner(rawget(G, 'LuaUtils'))\n"
    "  if lu == nil then return end\n"
    "  for name, fn in pairs(utils) do\n"
    "    if rawget(lu, name) == nil then rawset(lu, name, fn) end\n"
    "  end\n"
    "  utilsDone = true\n"
    "end\n"
    "local pcChildren = {\n"
    "  buttonprompt = { 'keyshortcut' },\n"
    "  ChooseCharacterLoadout_ButtonFooter_BackOfCard = { 'clickableButton' },\n"
    "  ChooseCharacterLoadout_ButtonFooter_Personalize = { 'clickableButton' },\n"
    "  ChooseCharacterLoadout_CardBack_FrontOfCardFooter = { 'clickableButton' },\n"
    "  ChooseCharacterLoadout_CardBack_Transmission = { 'audioPromptPC' },\n"
    "  ChooseTaunts_TauntOptionInfo = { 'clickButton' },\n"
    "  CraftActionButton = { 'icon' },\n"
    "  CraftActionSlider = { 'icon' },\n"
    "  DemoControlsSpectatingBar = { 'leftBumper', 'rightBumper' },\n"
    "  DemoPlaybackControlsButtons = { 'alignment1', 'alignment2', 'alignment3' },\n"
    "  EmblemLayerContainer = { 'FETitleNumBrdr1' },\n"
    "  FaceButtonThatHidesWithLabel = { 'clickButton' },\n"
    "  fe_FooterContainer = { 'StarterPackWatermark' },\n"
    "  fe_FooterContainer_NOTLobby = { 'StarterPackWatermark' },\n"
    "  FE_List1ButtonLarge_PH = { 'DownloadingImage', 'FEVialBreadcrumb', 'RestrictedImage', 'UpgradeImage' },\n"
    "  FE_ListAdditonal = { 'SplitscreenLobbyButtonPC' },\n"
    "  fe_RightContainer = { 'Image000', 'ServerBrowserPrompt' },\n"
    "  FooterButtonDoublePrompts = { 'BumperButtonWithKeyMouseLeft', 'BumperButtonWithKeyMouseRight' },\n"
    "  FR_DpadWidget = { 'RespawnButtonPromptPC', 'RestartTrackButtonPromptPC' },\n"
    "  LobbyMember = { 'LobbyMemberMP45' },\n"
    "  LobbyMemberTeamSwitcher = { 'BumperButtonWithKeyMouseLeft', 'BumperButtonWithKeyMouseRight' },\n"
    "  MegaChewFactoryBuyDivinium = { 'clickButton' },\n"
    "  OptionDropdown = { 'frameOutline' },\n"
    "  PaintjobSelectorItem = { 'UnavailableText' },\n"
    "  PaintshopChoosePaintSide = { 'leftButton', 'rightButton' },\n"
    "  PlaylistDLC1CRM = { 'Keyshortcut' },\n"
    "  StartMenu_Options_PC_GamepadControls = { 'gamepadText', 'splitscreenText' },\n"
    "  StartMenu_Options_PC_Graphics_Game = { 'StartMenuOptionsGraphicsColorBlindMinimapPreview0' },\n"
    "  WeaponCustomizationSelect = { 'XCamMouseControl' },\n"
    "  WeaponPreviewPane = { 'XCamMouseControl' },\n"
    "  XpBarContainer = { 'starterPackXPNotice' },\n"
    "}\n"
    "local aliases = {}\n"
    "local rebuild = {}\n"
    "rebuild.matchSettingsInfo = function(element, menu, controller)\n"
    "  local old = element.Description\n"
    "  if old == nil or old.textBox ~= nil then return end\n"
    "  local cod = rawget(G, 'CoD')\n"
    "  local boxClass = rawget(cod, 'verticalScrollingTextBox')\n"
    "  if type(boxClass) ~= 'table' or type(boxClass.new) ~= 'function' then\n"
    "    old.textBox = old\n"
    "    return\n"
    "  end\n"
    "  local box = boxClass.new(menu, controller)\n"
    "  box:setLeftRight(true, false, 4, 465)\n"
    "  box:setTopBottom(true, false, 383, 563)\n"
    "  box.textBox:setTTF('fonts/RefrigeratorDeluxe-Regular.ttf')\n"
    "  box.textBox:setAlignment(Enum.LUIAlignment.LUI_ALIGNMENT_LEFT)\n"
    "  box:linkToElementModel(element, 'description', true, function(model)\n"
    "    local value = Engine.GetModelValue(model)\n"
    "    if value then box.textBox:setText(Engine.Localize(value)) end\n"
    "  end)\n"
    "  element:addElement(box)\n"
    "  old:setAlpha(0)\n"
    "  element.Description = box\n"
    "end\n"
    "for name in pairs(rebuild) do\n"
    "  if pcChildren[name] == nil then pcChildren[name] = {} end\n"
    "end\n"
    "local wrapped = {}\n"
    "local function standIn(widget, name)\n"
    "  local lui = rawget(G, 'LUI')\n"
    "  local holder = lui.UIElement.new()\n"
    "  holder:setAlpha(0)\n"
    "  local label = lui.UIText.new()\n"
    "  label:setAlpha(0)\n"
    "  holder:addElement(label)\n"
    "  holder.label = label\n"
    "  widget:addElement(holder)\n"
    "  widget[name] = holder\n"
    "end\n"
    "local function patchWidgets()\n"
    "  local cod = rawget(G, 'CoD')\n"
    "  if type(cod) ~= 'table' or rawget(G, 'LUI') == nil then return end\n"
    "  for name, kids in pairs(pcChildren) do\n"
    "    local widget = rawget(cod, name)\n"
    "    local make = type(widget) == 'table' and rawget(widget, 'new') or nil\n"
    "    if type(make) == 'function' and wrapped[name] ~= make then\n"
    "      local built = function(...)\n"
    "        local element = make(...)\n"
    "        if element ~= nil then\n"
    "          if rebuild[name] ~= nil then pcall(rebuild[name], element, ...) end\n"
    "          local alias = aliases[name]\n"
    "          if alias ~= nil then\n"
    "            for child, field in pairs(alias) do\n"
    "              pcall(function()\n"
    "                local owner = element[child]\n"
    "                if owner ~= nil and owner[field] == nil then owner[field] = owner end\n"
    "              end)\n"
    "            end\n"
    "          end\n"
    "          for i = 1, #kids do\n"
    "            local ok, have = pcall(function() return element[kids[i]] end)\n"
    "            if ok and have == nil then pcall(standIn, element, kids[i]) end\n"
    "          end\n"
    "        end\n"
    "        return element\n"
    "      end\n"
    "      rawset(widget, 'new', built)\n"
    "      wrapped[name] = built\n"
    "    end\n"
    "  end\n"
    "end\n"
    "local native = {\n"
    "  UIElement = { 'setAlpha', 'setRGB', 'setScale', 'setXRot', 'setYRot', 'setZRot', 'setImage', 'setTTF',\n"
    "    'setupReticle', 'setLeftRight', 'setTopBottom', 'setMaterial', 'setPriority', 'setZoom', 'setText' },\n"
    "  UIText = { 'setText', 'setTTF', 'setRGB', 'setAlpha', 'setScale' },\n"
    "  UIImage = { 'setImage', 'setMaterial', 'setRGB', 'setAlpha', 'setScale' }\n"
    "}\n"
    "local nativeDone = {}\n"
    "local function patchLui()\n"
    "  local lui = rawget(G, 'LUI')\n"
    "  if type(lui) ~= 'table' then return end\n"
    "  for class, names in pairs(native) do\n"
    "    local cls = rawget(lui, class)\n"
    "    if not nativeDone[class] and type(cls) == 'table' and type(rawget(cls, 'new')) == 'function' then\n"
    "      nativeDone[class] = true\n"
    "      local ok, probe = pcall(cls.new)\n"
    "      if ok and probe ~= nil then\n"
    "        for i = 1, #names do\n"
    "          local name = names[i]\n"
    "          local found, fn = pcall(function() return probe[name] end)\n"
    "          if found and type(fn) == 'function' and rawget(cls, name) == nil then rawset(cls, name, fn) end\n"
    "        end\n"
    "      end\n"
    "    end\n"
    "  end\n"
    "end\n"
    "local watched = {}\n"
    "local function patchWatch()\n"
    "  local engine = rawget(G, 'Engine')\n"
    "  if type(engine) == 'table' and not watched.maps then\n"
    "    watched.maps = true\n"
    "    local function internal(name)\n"
    "      if type(name) ~= 'string' or not string.find(name, '^%d+$') then return name end\n"
    "      local maps = rawget(G, 'BO3CustomMaps')\n"
    "      if type(maps) == 'table' then\n"
    "        for i = 1, #maps do\n"
    "          if maps[i].ugc == name then return maps[i].id end\n"
    "        end\n"
    "      end\n"
    "      return name\n"
    "    end\n"
    "    for _, key in ipairs({ 'GameLobbySetMap', 'IsMapValid' }) do\n"
    "      local original = engine[key]\n"
    "      if type(original) == 'function' then\n"
    "        rawset(engine, key, function(map, ...)\n"
    "          return original(internal(map), ...)\n"
    "        end)\n"
    "      end\n"
    "    end\n"
    "  end\n"
    "end\n"
    "local function patch()\n"
    "  pcall(patchWatch)\n"
    "  pcall(patchUtils)\n"
    "  pcall(patchWidgets)\n"
    "  pcall(patchLui)\n"
    "  local data = inner(rawget(G, 'LobbyData'))\n"
    "  if data == nil then return end\n"
    "  local targets = inner(rawget(data, 'UITargets'))\n"
    "  if type(targets) ~= 'table' or rawget(targets, 'UI_MPLOBBYONLINEMODGAME') ~= nil then return end\n"
    "  local custom = rawget(targets, 'UI_MPLOBBYONLINECUSTOMGAME')\n"
    "  if type(custom) ~= 'table' then return end\n"
    "  local target = {}\n"
    "  for key, value in pairs(custom) do target[key] = value end\n"
    "  target.id = 320\n"
    "  target.name = 'MPLobbyOnlineModGame'\n"
    "  target.title = 'MENU_CUSTOMGAMES_CAPS'\n"
    "  target.kicker = ''\n"
    "  target.room = 'mp_custom'\n"
    "  rawset(targets, 'UI_MPLOBBYONLINEMODGAME', target)\n"
    "end\n"
    "local function modOwned(name)\n"
    "  local files = rawget(G, 'BO3CustomsModFiles')\n"
    "  if type(files) ~= 'table' or type(name) ~= 'string' then return false end\n"
    "  return files[string.lower(string.gsub(name, '%.', '/')) .. '.lua'] ~= nil\n"
    "end\n"
    "rawset(G, 'require', function(name, ...)\n"
    "  pcall(patch)\n"
    "  local cod = rawget(G, 'CoD')\n"
    "  local was = type(cod) == 'table' and rawget(cod, 'isPC') or nil\n"
    "  local function note(text)\n"
    "    local tabs = rawget(G, 'BO3CustomsMapTabs')\n"
    "    if type(tabs) == 'table' and type(tabs.logs) == 'table' then tabs.logs[#tabs.logs + 1] = text end\n"
    "  end\n"
    "  if rawget(G, 'BO3CustomsPCScope') then\n"
    "    local ok, result = pcall(original, name, ...)\n"
    "    if not ok then note('PC-only file ' .. tostring(name) .. ' did not load: ' .. tostring(result)) end\n"
    "    pcall(patch)\n"
    "    return result\n"
    "  end\n"
    "  if type(cod) ~= 'table' or was == true or not modOwned(name) then\n"
    "    local result = original(name, ...)\n"
    "    pcall(patch)\n"
    "    return result\n"
    "  end\n"
    "  rawset(cod, 'isPC', true)\n"
    "  rawset(G, 'BO3CustomsPCScope', true)\n"
    "  local ok, result = pcall(original, name, ...)\n"
    "  rawset(G, 'BO3CustomsPCScope', nil)\n"
    "  rawset(cod, 'isPC', was)\n"
    "  note('mod file ' .. name .. ' loaded as on PC' .. (ok and '' or (': ' .. tostring(result))))\n"
    "  pcall(patch)\n"
    "  if not ok then error(result, 0) end\n"
    "  return result\n"
    "end)\n";

static const char k_dllStandInLua[] =
    "local G = _G\n"
    "local state = rawget(G, 'BO3CustomsDll')\n"
    "if type(state) ~= 'table' then\n"
    "  state = {}\n"
    "  rawset(G, 'BO3CustomsDll', state)\n"
    "end\n"
    "local function nothing() end\n"
    "if not state.libs then\n"
    "  state.libs = true\n"
    "  local slash = string.char(92)\n"
    "  local function prefix(text, start)\n"
    "    if type(start) ~= 'string' then return text end\n"
    "    start = string.gsub(start, '[/' .. slash .. ']+$', '')\n"
    "    if start ~= '' and string.lower(string.sub(text, 1, #start)) == string.lower(start) then\n"
    "      return string.sub(text, #start + 1)\n"
    "    end\n"
    "    return text\n"
    "  end\n"
    "  local function dvar(name)\n"
    "    local engine = rawget(G, 'Engine')\n"
    "    if type(engine) ~= 'table' or type(engine.GetDvarString) ~= 'function' then return nil end\n"
    "    local ok, value = pcall(engine.GetDvarString, name)\n"
    "    if ok then return value end\n"
    "    return nil\n"
    "  end\n"
    "  local function place(name)\n"
    "    local folder = rawget(G, 'BO3CustomsDllSave')\n"
    "    if type(name) ~= 'string' or type(folder) ~= 'string' or folder == '' then return name end\n"
    "    if string.sub(name, 1, #folder) == folder or string.sub(name, 1, 18) == '/data/BO3-Customs/' then return name end\n"
    "    name = prefix(prefix(name, dvar('fs_homepath')), dvar('fs_basepath'))\n"
    "    name = string.gsub(name, '^[/' .. slash .. ']+', '')\n"
    "    local flat = string.gsub(name, '^%.[/' .. slash .. ']+', '')\n"
    "    flat = string.gsub(flat, '[/' .. slash .. ':]', '_')\n"
    "    return folder .. flat\n"
    "  end\n"
    "  local function fixIo(lib)\n"
    "    if type(lib) ~= 'table' then lib = {} end\n"
    "    if rawget(lib, 'BO3CustomsOpen') ~= nil then return lib end\n"
    "    local open = rawget(lib, 'open')\n"
    "    local lines = rawget(lib, 'lines')\n"
    "    if type(open) ~= 'function' then open = nothing end\n"
    "    pcall(rawset, lib, 'BO3CustomsOpen', open)\n"
    "    pcall(rawset, lib, 'open', function(name, mode) return open(place(name), mode) end)\n"
    "    if type(lines) == 'function' then\n"
    "      pcall(rawset, lib, 'lines', function(name)\n"
    "        if name == nil then return lines() end\n"
    "        return lines(place(name))\n"
    "      end)\n"
    "    end\n"
    "    return lib\n"
    "  end\n"
    "  local function openFile(name, mode)\n"
    "    local lib = state.io or rawget(G, 'io')\n"
    "    if type(lib) ~= 'table' then return nil end\n"
    "    return rawget(fixIo(lib), 'BO3CustomsOpen')(place(name), mode)\n"
    "  end\n"
    "  local function quiet(known)\n"
    "    return setmetatable(known, { __index = function() return nothing end })\n"
    "  end\n"
    "  local function stand(name, known)\n"
    "    if rawget(G, name) == nil then rawset(G, name, quiet(known)) end\n"
    "  end\n"
    "  local dll = rawget(G, 'BO3CustomsDllMod') == true\n"
    "  if dll then\n"
    "  stand('T7Overcharged', {})\n"
    "  stand('UIErrorHash', {})\n"
    "  stand('DiscordRPC', {})\n"
    "  stand('HotReload', {})\n"
    "  stand('Console', {})\n"
    "  stand('Axios', {})\n"
    "  stand('FileIO', {\n"
    "    FileExists = function(name)\n"
    "      local file = openFile(name, 'rb')\n"
    "      if file == nil then return false end\n"
    "      file:close()\n"
    "      return true\n"
    "    end,\n"
    "    ReadFile = function(name)\n"
    "      local file = openFile(name, 'rb')\n"
    "      if file == nil then return nil end\n"
    "      local text = file:read('*a')\n"
    "      file:close()\n"
    "      return text\n"
    "    end,\n"
    "    WriteFile = function(name, text, append)\n"
    "      local file = openFile(name, (append == true or append == 'true') and 'ab' or 'wb')\n"
    "      if file == nil then return false end\n"
    "      file:write(tostring(text or ''))\n"
    "      file:close()\n"
    "      return true\n"
    "    end })\n"
    "  stand('MainLUA', { Revision = function() return rawget(G, 'DLLREVISION') or 0 end,\n"
    "    GetHardwareID = function() return '' end, GetHardwareCPU = function() return '' end })\n"
    "  end\n"
    "  local function loadlib() return nothing end\n"
    "  local function fixPackage(lib)\n"
    "    if type(lib) ~= 'table' then lib = {} end\n"
    "    pcall(rawset, lib, 'loadlib', loadlib)\n"
    "    return lib\n"
    "  end\n"
    "  local global = rawget(G, 'io')\n"
    "  if type(global) == 'table' then fixIo(global) end\n"
    "  local real = rawget(G, 'require')\n"
    "  if type(real) == 'function' then\n"
    "    rawset(G, 'require', function(name, ...)\n"
    "      if name == 'package' or name == 'io' then\n"
    "        local ok, lib = pcall(real, name)\n"
    "        if not ok or type(lib) ~= 'table' then lib = rawget(G, name) end\n"
    "        if name == 'package' then return dll and fixPackage(lib) or lib end\n"
    "        lib = fixIo(lib)\n"
    "        state.io = lib\n"
    "        return lib\n"
    "      end\n"
    "      return real(name, ...)\n"
    "    end)\n"
    "  end\n"
    "end\n"
    "local engine = rawget(G, 'Engine')\n"
    "if not state.engine and type(engine) == 'table' then\n"
    "  state.engine = true\n"
    "  rawset(engine, 'UsingModsUgcName', function()\n"
    "    local mod = rawget(G, 'BO3CustomsActiveMod')\n"
    "    if type(mod) == 'table' and type(mod.ugc) == 'string' and mod.ugc ~= '' then return mod.ugc end\n"
    "    return rawget(G, 'BO3CustomsDllUgc') or ''\n"
    "  end)\n"
    "end\n";

static const char k_pcParityLua[] =
    "local G = _G\n"
    "local function define(name, fn)\n"
    "  if rawget(G, name) == nil then rawset(G, name, fn) end\n"
    "end\n"
    "local function yes() return true end\n"
    "local function no() return false end\n"
    "local function nothing() end\n"
    "local function same(value) return value end\n"
    "local function each(names, fn)\n"
    "  for i = 1, #names do define(names[i], fn) end\n"
    "end\n"
    "define('MapImageToModPreview', function(id)\n"
    "  local maps = rawget(G, 'BO3CustomMaps')\n"
    "  if type(maps) == 'table' then\n"
    "    for i = 1, #maps do\n"
    "      if (maps[i].id == id or maps[i].ugc == id) and maps[i].preview ~= nil then return maps[i].preview end\n"
    "    end\n"
    "  end\n"
    "  return id\n"
    "end)\n"
    "each({\n"
    "  'GamepadsConnectedAny', 'GamepadsConnectedIsActive', 'IsGameModeOwned',\n"
    "  'IsStarterPackWatermarkHidden', 'PaintshopEditAvailable'\n"
    "}, yes)\n"
    "each({\n"
    "  'AllowGuestSplitScreenOnline', 'IsChunkDownloading', 'IsChunksRestrictedButtonByParty',\n"
    "  'IsFilterActive', 'IsRestrictedButtonByParty', 'IsServerBrowserEnabled',\n"
    "  'IsSplitscreenLobbyRoomAvailable', 'IsSplitscreenPlayAvailable', 'IsSplitscreenPlayerSignedIn',\n"
    "  'IsStarterPackMaxLevel', 'IsStarterPackNotAvailableButton', 'IsStarterPackRestrictedButton',\n"
    "  'IsStarterPackRestrictedButtonByParty', 'IsSteamServerBrowserEmpty', 'IsSteamServerBrowserUpdating',\n"
    "  'PartyMemberMissingContent', 'SteamServerIsCurrentServerTypeFavorites',\n"
    "  'SteamServerIsCurrentServerTypeHistory', 'SteamServerSortMatchesHeaderAscending',\n"
    "  'SteamServerSortMatchesHeaderDescending'\n"
    "}, no)\n"
    "local function lobbyAction(name)\n"
    "  define(name, function(self, element, controller, param, menu)\n"
    "    local cod = rawget(G, 'CoD')\n"
    "    local base = type(cod) == 'table' and rawget(cod, 'LobbyBase') or nil\n"
    "    local fn = type(base) == 'table' and base[name] or nil\n"
    "    if type(fn) == 'function' then return fn(self, controller) end\n"
    "  end)\n"
    "end\n"
    "lobbyAction('OpenServerSettings')\n"
    "lobbyAction('OpenBotSettings')\n"
    "each({\n"
    "  'CheckNavRestrictions', 'DisableMouseButtonOnElement', 'DisableMouseOnElement',\n"
    "  'DisableMouseOnMenuElement', 'EnableMouseOnElement', 'EnableMouseOnMenuElement', 'HideMouseCursor',\n"
    "  'JoinServerBrowser', 'LobbySplitscreenToggle', 'OpenServerBrowser', 'OpenServerBrowserFilters',\n"
    "  'RefreshLobbyServerBrowser', 'ServerBrowserCancelRequest',\n"
    "  'ServerBrowserQuickRefresh', 'ServerBrowserRefreshServer', 'ServerBrowserRequestPlayersList',\n"
    "  'ServerBrowserSetFavorite', 'ServerBrowserToggleFilter', 'ServerFiltersEditKeywords',\n"
    "  'ServerFiltersHandleKeyboardComplete', 'ServerSettingsEditDescription', 'ServerSettingsEditName',\n"
    "  'ServerSettingsEditPassword', 'ServerSettingsHandleKeyboardComplete',\n"
    "  'SetCompetitiveAttachmentSettingsTab', 'SetCompetitiveItemSettingsFilter', 'SetGameSettingsTab',\n"
    "  'SetNetworkMode', 'SetPregameVoteFilter', 'ShouldUnloadMod', 'ShowMouseCursor',\n"
    "  'StarterParckPurchase', 'StoreButtonOpenSteamStore', 'UIKeyboardCancel', 'UIKeyboardComplete'\n"
    "}, nothing)\n"
    "each({\n"
    "  'GetAARXPNextLevel', 'GetAARXPStarterPackNotice', 'GetAARXpEarned', 'GetGameTypeDisplayString',\n"
    "  'SecondsAsTimePlayedStringShort', 'TruncateString', 'TruncateTo32Chars', 'TruncateTo64Chars'\n"
    "}, same)\n"
    "local enums = rawget(G, 'LuaEnums')\n"
    "if type(enums) == 'table' then\n"
    "  if enums.MODS_BASE_PATH == nil then pcall(rawset, enums, 'MODS_BASE_PATH', 'mods') end\n"
    "  if enums.USERMAP_BASE_PATH == nil then pcall(rawset, enums, 'USERMAP_BASE_PATH', 'usermaps') end\n"
    "end\n"
    "local cod = rawget(G, 'CoD')\n"
    "if type(cod) == 'table' then\n"
    "  local function zero() return 0 end\n"
    "  local function field(owner, name, fn)\n"
    "    local at = rawget(cod, owner)\n"
    "    if type(at) == 'table' and rawget(at, name) == nil then pcall(rawset, at, name, fn) end\n"
    "  end\n"
    "  field('AARUtility', 'EstimateEarnedXP', zero)\n"
    "  field('LobbyBase', 'LobbyQuitOrLeaveParty', nothing)\n"
    "  field('LobbyBase', 'OpenServerSettings', function(self, controller)\n"
    "    local base = rawget(cod, 'LobbyBase')\n"
    "    local lui = rawget(G, 'LUI')\n"
    "    if type(lui) ~= 'table' or type(lui.createMenu) ~= 'table' then return nil end\n"
    "    if lui.createMenu.ServerSettings == nil then pcall(require, 'ui.uieditor.menus.PC.ServerSettings') end\n"
    "    if type(lui.createMenu.ServerSettings) ~= 'function' or type(rawget(G, 'OpenOverlay')) ~= 'function' then return nil end\n"
    "    local activity = type(base.LeaderActivity) == 'table' and base.LeaderActivity.EDITING_GAME_RULES or nil\n"
    "    if activity ~= nil and type(base.SetLeaderActivity) == 'function' then pcall(base.SetLeaderActivity, controller, activity) end\n"
    "    local menu = OpenOverlay(self, 'ServerSettings', controller)\n"
    "    if menu ~= nil and type(base.ResetLeaderActivity) == 'function' and type(lui.OverrideFunction_CallOriginalFirst) == 'function' then\n"
    "      lui.OverrideFunction_CallOriginalFirst(menu, 'close', function() base.ResetLeaderActivity(controller) end)\n"
    "    end\n"
    "    return menu\n"
    "  end)\n"
    "  field('LobbyBase', 'SplitscreenLobbyRoomAvailable', no)\n"
    "  field('LobbyBase', 'SplitscreenPlayAvailable', no)\n"
    "  field('LobbyBase', 'UGCActivity', nothing)\n"
    "  field('LobbyMenus', 'MPButtonsModGame', nothing)\n"
    "  field('LobbyUtility', 'BitsToComplainAbout', zero)\n"
    "  field('LobbyUtility', 'RequiredDLCMaskForPlaylist', zero)\n"
    "  field('PrestigeUtility', 'ResetForStarterPack', nothing)\n"
    "  local numbers = { CONTENT_MODS_INDEX = 999, GAMEMODE_FREERUN = 4, GAMEMODE_MANUAL_PLAYLIST = 6 }\n"
    "  for key, value in pairs(numbers) do\n"
    "    if rawget(cod, key) == nil then pcall(rawset, cod, key, value) end\n"
    "  end\n"
    "end\n"
    "local engine = rawget(G, 'Engine')\n"
    "if type(engine) ~= 'table' then return end\n"
    "local function entries(path)\n"
    "  local list, count = {}, 0\n"
    "  if path == 'mods' then\n"
    "    local mods = rawget(G, 'BO3CustomMods')\n"
    "    if type(mods) == 'table' then\n"
    "      for i = 1, #mods do\n"
    "        local mod = mods[i]\n"
    "        list[count] = { name = mod.title, internalName = mod.internal, ugcName = mod.ugc, description = '', folder = mod.name }\n"
    "        count = count + 1\n"
    "      end\n"
    "    end\n"
    "  else\n"
    "    local maps = rawget(G, 'BO3CustomMaps')\n"
    "    if type(maps) == 'table' then\n"
    "      for i = 1, #maps do\n"
    "        local map = maps[i]\n"
    "        list[count] = { name = map.name, internalName = map.id, ugcName = map.ugc or map.id, description = map.desc or '' }\n"
    "        count = count + 1\n"
    "      end\n"
    "    end\n"
    "  end\n"
    "  return list, count\n"
    "end\n"
    "local function native(name, fn)\n"
    "  if rawget(engine, name) == nil then rawset(engine, name, fn) end\n"
    "end\n"
    "native('IsZmOwned', yes)\n"
    "native('IsMpOwned', yes)\n"
    "native('IsCpOwned', yes)\n"
    "native('Mods_Lists_GetInfoEntriesCount', function(path)\n"
    "  local list, count = entries(path)\n"
    "  return count\n"
    "end)\n"
    "native('Mods_Lists_GetInfoEntries', function(path)\n"
    "  local list = entries(path)\n"
    "  return list\n"
    "end)\n"
    "if rawget(G, 'BO3CustomsQuit') == nil and type(rawget(engine, 'Exec')) == 'function' then\n"
    "  local run = rawget(engine, 'Exec')\n"
    "  rawset(G, 'BO3CustomsQuit', true)\n"
    "  rawset(engine, 'Exec', function(controller, command, ...)\n"
    "    if type(command) == 'string' and string.match(string.lower(command), '^%s*quit%s*;?%s*$') then\n"
    "      return engine.SetDvar('bo3customs_quit', 1)\n"
    "    end\n"
    "    return run(controller, command, ...)\n"
    "  end)\n"
    "end\n"
    "local enum = rawget(G, 'Enum')\n"
    "local function plain(t)\n"
    "  if type(t) ~= 'table' then return nil end\n"
    "  local original = rawget(t, '_originalTable')\n"
    "  if type(original) == 'table' then return original end\n"
    "  return t\n"
    "end\n"
    "local kinds = plain(type(enum) == 'table' and enum.KeyboardType or nil)\n"
    "local lui = rawget(G, 'LUI')\n"
    "local element = type(lui) == 'table' and rawget(lui, 'UIElement') or nil\n"
    "if type(kinds) ~= 'table' or type(element) ~= 'table' or rawget(G, 'BO3CustomsKeyboards') ~= nil then return end\n"
    "local stand = kinds.KEYBOARD_TYPE_GROUP_SEARCH_NAME or kinds.KEYBOARD_TYPE_TEXT_MESSAGE\n"
    "local exec = rawget(engine, 'Exec')\n"
    "local register = rawget(element, 'registerEventHandler')\n"
    "if type(stand) ~= 'number' or type(exec) ~= 'function' or type(register) ~= 'function' then return end\n"
    "local keyboards = { pc = {}, pending = nil, next = 1000 }\n"
    "rawset(G, 'BO3CustomsKeyboards', keyboards)\n"
    "local function add(name)\n"
    "  if rawget(kinds, name) ~= nil then return rawget(kinds, name) end\n"
    "  local value = keyboards.next\n"
    "  keyboards.next = value + 1\n"
    "  if pcall(rawset, kinds, name, value) then keyboards.pc[value] = name end\n"
    "  return rawget(kinds, name)\n"
    "end\n"
    "for _, name in ipairs({ 'KEYBOARD_TYPE_SERVER_NAME', 'KEYBOARD_TYPE_SERVER_DESCRIPTION', 'KEYBOARD_TYPE_SERVER_PASSWORD',\n"
    "  'KEYBOARD_TYPE_SERVER_CLIENT_PASSWORD', 'KEYBOARD_TYPE_SERVER_FILTER_KEYWORDS' }) do add(name) end\n"
    "pcall(setmetatable, kinds, { __index = function(t, key)\n"
    "  if type(key) == 'string' and string.sub(key, 1, 14) == 'KEYBOARD_TYPE_' then return add(key) end\n"
    "  return nil\n"
    "end })\n"
    "rawset(engine, 'Exec', function(controller, command, ...)\n"
    "  if type(command) == 'string' then\n"
    "    local kind = tonumber(string.match(command, '^%s*ui_keyboard_new%s+(%d+)'))\n"
    "    if kind ~= nil and keyboards.pc[kind] ~= nil then\n"
    "      keyboards.pending = kind\n"
    "      command = 'ui_keyboard_new ' .. stand\n"
    "    elseif kind ~= nil then\n"
    "      keyboards.pending = nil\n"
    "    end\n"
    "  end\n"
    "  return exec(controller, command, ...)\n"
    "end)\n"
    "rawset(element, 'registerEventHandler', function(self, name, handler, ...)\n"
    "  if name == 'ui_keyboard_input' and type(handler) == 'function' then\n"
    "    local original = handler\n"
    "    handler = function(target, event, ...)\n"
    "      if type(event) == 'table' and keyboards.pending ~= nil and event.type == stand then\n"
    "        event.type = keyboards.pending\n"
    "      end\n"
    "      return original(target, event, ...)\n"
    "    end\n"
    "  end\n"
    "  return register(self, name, handler, ...)\n"
    "end)\n";

static uintptr_t g_base = 0;
static uint32_t  g_ticks = 0;
static bool      g_forceInject = false;
static uintptr_t g_pcUtilState = 0;
static uintptr_t g_mouseState = 0;

static Detour g_addMenuDetour{};
static void*  g_addMenuOriginal = nullptr;
static Detour g_requireDetour{};
static void*  g_requireOriginal = nullptr;
static Detour g_findAssetDetour{};
static void*  g_findAssetOriginal = nullptr;

using Passthrough_t = uint64_t (*)(uint64_t, uint64_t, uint64_t, uint64_t, uint64_t, uint64_t,
                                   double, double, double, double, double, double, double, double);

extern "C" uint64_t T7Lua_ThreadPointer();

static uintptr_t FindDvar(const char* name)
{
    return ((uintptr_t (*)(const char*))(g_base + kDvarFind))(name);
}

static void SetDvar(const char* name, const char* value)
{
    ((void (*)(const char*, const char*, uint32_t, uint32_t))(g_base + kDvarSetByName))(name, value, 0, 0);
}

static const char* DvarText(uintptr_t dvar)
{
    return ((const char* (*)(uintptr_t))(g_base + kDvarString))(dvar);
}

static bool DvarOn(const char* name)
{
    const uintptr_t dvar = FindDvar(name);
    const char* const text = dvar ? DvarText(dvar) : nullptr;
    return text && atoi(text) != 0;
}

static bool UiTryLock()
{
    const uint64_t tls = T7Lua_ThreadPointer();
    int32_t* const depth = (int32_t*)(tls - 576 + 4 * (uint64_t)kUiLock);

    if (*depth > 0)
    {
        ++(*depth);
        return true;
    }

    volatile int32_t* const word = (volatile int32_t*)(g_base + kLockWords + 4 * (uintptr_t)kUiLock);

    if (!__sync_bool_compare_and_swap(word, 0, 1))
        return false;

    *depth = 1;
    return true;
}

static void UiUnlock()
{
    const uint64_t tls = T7Lua_ThreadPointer();
    int32_t* const depth = (int32_t*)(tls - 576 + 4 * (uint64_t)kUiLock);

    if (--(*depth) == 0)
        *(volatile int32_t*)(g_base + kLockWords + 4 * (uintptr_t)kUiLock) = 0;
}

static bool UiUp()
{
    return (*(const uint8_t*)(g_base + kUiReady) & 1) != 0 && *(const uintptr_t*)(g_base + kLuaState) != 0;
}

struct HksBuffer
{
    const char* data;
    uint64_t    size;
};

static void DropFailure(uintptr_t L, uint64_t topOffset)
{
    *(uint64_t*)(L + kStateTop) = *(const uint64_t*)(L + kStateStack) + topOffset;
}

static bool InMenus()
{
    return !T7Maps_InLevel();
}

static bool RunLuaOn(uintptr_t L, const char* source, const char* chunk)
{
    const uintptr_t global = L ? *(const uintptr_t*)(L + kStateGlobal) : 0;

    if (!L || !global || !source)
        return false;

    if (*(const uint64_t*)(L + kStateTop) + 32 > *(const uint64_t*)(L + kStateStackLast))
        ((void (*)(uintptr_t, uintptr_t, uint64_t))(g_base + kLuaGrowStack))(L + 24, L, 2);

    const uint64_t topOffset = *(const uint64_t*)(L + kStateTop) - *(const uint64_t*)(L + kStateStack);

    HksBuffer buffer = { source, strlen(source) };
    int32_t* const sharing = (int32_t*)(global + kGlobalSharing);
    const int32_t sharingWas = *sharing;

    *sharing = kSharingOn;

    using Load_t = int32_t (*)(uintptr_t, uintptr_t, uintptr_t, HksBuffer*, const char*);
    const int32_t loaded = ((Load_t)(g_base + kHksLoad))(L, global + kGlobalCompiler, g_base + kHksBufferReader,
                                                         &buffer, chunk);

    *sharing = sharingWas;

    if (loaded != 0)
    {
        T7Log_LuaFailure(L, topOffset, chunk, "compile");
        DropFailure(L, topOffset);
        return false;
    }

    using Pcall_t = int32_t (*)(uintptr_t, int32_t, int32_t, int32_t);

    if (((Pcall_t)(g_base + kLuaPcall))(L, 0, 0, 0) != 0)
    {
        T7Log_LuaFailure(L, topOffset, chunk, "run");
        DropFailure(L, topOffset);
        return false;
    }

    return true;
}

static bool RunLua(const char* source, const char* chunk)
{
    return RunLuaOn(*(const uintptr_t*)(g_base + kLuaState), source, chunk);
}

static char* ReadScript(const char* name)
{
    char relative[256];
    const int length = snprintf(relative, sizeof(relative), "ui_scripts/%s", name);
    return length > 0 && (size_t)length < sizeof(relative)
        ? T7LuaFiles::Read(relative, 256 * 1024) : nullptr;
}

static bool AppendLuaFormat(char* out, size_t size, size_t* used, const char* format, ...)
{
    if (*used >= size)
        return false;
    va_list args;
    va_start(args, format);
    const int n = vsnprintf(out + *used, size - *used, format, args);
    va_end(args);
    if (n < 0 || (size_t)n >= size - *used)
    {
        *used = size;
        return false;
    }
    *used += (size_t)n;
    return true;
}

static void AppendQuoted(char* out, size_t size, size_t* used, const char* text)
{
    if (!text)
        text = "";
    if (*used >= size || size - *used <= 3)
        return;

    out[(*used)++] = '"';

    const unsigned char* c = (const unsigned char*)text;
    for (; *c && size - *used > 4; ++c)
    {
        if (*c == '"' || *c == '\\')
            out[(*used)++] = '\\';

        if (*c >= 0x20)
            out[(*used)++] = (char)*c;
    }
    if (*c)
    {
        // A partial quoted value is not a valid Lua argument. Mark this
        // entire generated command as truncated so its caller skips RunLua.
        out[size - 1] = 0;
        *used = size;
        return;
    }

    out[(*used)++] = '"';
    out[*used] = 0;
}

static void ReportUninstall(const char* map, bool removed, const char* message)
{
    char lua[1024];
    size_t used = 0;
    AppendLuaFormat(lua, sizeof(lua), &used, "%s",
                                   "local tabs = rawget(_G, 'BO3CustomsMapTabs')\n"
                                   "if tabs ~= nil and tabs.Uninstalled ~= nil then tabs.Uninstalled(");
    AppendQuoted(lua, sizeof(lua), &used, map);
    AppendLuaFormat(lua, sizeof(lua), &used, ", %s, ", removed ? "true" : "false");
    AppendQuoted(lua, sizeof(lua), &used, message);
    AppendLuaFormat(lua, sizeof(lua), &used, ") end\n");
    if (used < sizeof(lua))
        RunLua(lua, "=bo3customs_uninstalled");
}

static void HandleUninstall()
{
    char finished[64];
    char message[192];
    const int result = T7Maps_UninstallResult(finished, sizeof(finished), message, sizeof(message));

    if (result >= 0)
    {
        if (T7Maps_Refresh())
            RunLua(T7Maps_CustomMapsLua(), "=bo3customs_maps");

        ReportUninstall(finished, result == 1, message);
        RunLua(k_refreshedLua, "=bo3customs_refreshed");
    }

    const uintptr_t dvar = FindDvar("bo3customs_uninstall");
    const char* const wanted = dvar ? DvarText(dvar) : nullptr;

    if (wanted && wanted[0] && strcmp(wanted, "0") != 0)
    {
        char map[64];
        snprintf(map, sizeof(map), "%s", wanted);
        SetDvar("bo3customs_uninstall", "");

        if (!T7Maps_Uninstall(map, message, sizeof(message)))
            ReportUninstall(map, false, message);
    }
}

static void ReportModChoice(const char* name, int state, const char* message)
{
    char lua[1536];
    size_t used = 0;
    AppendLuaFormat(lua, sizeof(lua), &used, "%s",
                                   "local ui = rawget(_G, 'BO3CustomsModsUI')\n"
                                   "if ui ~= nil and ui.Applied ~= nil then ui.Applied(");
    AppendQuoted(lua, sizeof(lua), &used, name);
    AppendLuaFormat(lua, sizeof(lua), &used, ", %d, ", state);
    AppendQuoted(lua, sizeof(lua), &used, message);
    AppendLuaFormat(lua, sizeof(lua), &used, ") end\n");
    if (used < sizeof(lua))
        RunLua(lua, "=bo3customs_mod_applied");
}

static void HandleModChoice()
{
    const uintptr_t dvar = FindDvar("bo3customs_mod");
    const char* const wanted = dvar ? DvarText(dvar) : nullptr;

    if (!wanted || !wanted[0] || strcmp(wanted, "0") == 0)
        return;

    char name[512];
    char message[320];
    snprintf(name, sizeof(name), "%s", wanted);
    SetDvar("bo3customs_mod", "");

    const int state = T7Maps_SelectMod(name, message, sizeof(message));

    if (state >= 0)
        T7Maps_RequestPrefetch();

    RunLua(T7Maps_ModsLua(), "=bo3customs_mods");
    ReportModChoice(name, state, message);
}

static void HandleModApply()
{
    if (!DvarOn("bo3customs_mods_apply"))
        return;

    SetDvar("bo3customs_mods_apply", "0");
    T7Maps_RequestApply();
}

static void HandleQuit()
{
    if (!DvarOn("bo3customs_quit"))
        return;

    SetDvar("bo3customs_quit", "0");
    T7Log_Write("[Cmd] quit: closing the game");
    sceSystemServiceLoadExec("exit", nullptr);
}

#ifdef _DEBUG
static void FlushDvarLog(const char* name, const char* source)
{
    const uintptr_t dvar = FindDvar(name);
    const char* const text = dvar ? DvarText(dvar) : nullptr;
    if (!text || !text[0])
        return;

    char lines[1024];
    snprintf(lines, sizeof(lines), "%s", text);
    SetDvar(name, "");

    char* rest = lines;
    while (rest && *rest)
    {
        char* const end = strchr(rest, '\n');
        if (end) *end = 0;
        if (*rest) T7Log_Write("[%s] %s", source, rest);
        rest = end ? end + 1 : nullptr;
    }
}

static void FlushLuaLog()
{
    // Preserve a pending native/GSC message before the UI queue reuses its dvar.
    FlushDvarLog("bo3customs_log", "MapSelect");
    if (RunLua(k_logFlushLua, "=bo3customs_logflush"))
        FlushDvarLog("bo3customs_log", "MapSelect");
}
#endif

static void ShowShare()
{
    static bool shown = false;

    T7ShareStatus status;
    const bool visible = T7Share_Status(&status);

    if ((!visible && !shown) || !UiUp() || !UiTryLock())
        return;

    if (UiUp())
    {
        if (DvarOn("bo3customs_share_cancel"))
        {
            SetDvar("bo3customs_share_cancel", "0");
            T7Share_Cancel();
        }

        if (!visible)
        {
            RunLua(k_shareHideLua, "=bo3customs_share_hide");
            shown = false;
        }
        else
        {
            RunLua(k_shareProbeLua, "=bo3customs_share_probe");

            if (DvarOn("bo3customs_share_pending"))
            {
                SetDvar("bo3customs_share_pending", "0");
                RunLua(k_shareUiLua, "=bo3customs_share_ui");
            }

            char lua[1792];
            size_t used = 0;
    AppendLuaFormat(lua, sizeof(lua), &used, "%s",
                                           "local share = rawget(_G, 'BO3CustomsShare')\n"
                                           "if share ~= nil and share.Show ~= nil then share.Show(");

            for (const char* const text : { status.title, status.name, status.detail, status.file })
            {
                AppendQuoted(lua, sizeof(lua), &used, text);
                AppendLuaFormat(lua, sizeof(lua), &used, ", ");
            }

            AppendLuaFormat(lua, sizeof(lua), &used, "%d / 1000, ", (int)(status.fraction * 1000.0f));
            AppendQuoted(lua, sizeof(lua), &used, status.failed ? "" : "Press Circle to cancel and leave the lobby");
            AppendLuaFormat(lua, sizeof(lua), &used, ", ");
            AppendQuoted(lua, sizeof(lua), &used, status.speeds);
            AppendLuaFormat(lua, sizeof(lua), &used, ") end\n");
            if (used < sizeof(lua))
                shown = RunLua(lua, "=bo3customs_share_show") || shown;
        }
    }

    UiUnlock();
}

static void InjectScripts()
{
    if (!UiUp() || !UiTryLock())
        return;

    if (UiUp())
    {
        RunLua(k_probeLua, "=bo3customs_probe");

        if (DvarOn("bo3customs_pending"))
        {
            SetDvar("bo3customs_pending", "0");

            char* const script = ReadScript("mapselect.lua");

            if (script)
            {
                RunLua(T7Maps_CustomMapsLua(), "=bo3customs_maps");
                RunLua(script, "@ui_scripts/mapselect.lua");
                free(script);
            }
        }

        if (DvarOn("bo3customs_refresh"))
        {
            SetDvar("bo3customs_refresh", "0");

            if (T7Maps_Refresh())
            {
                RunLua(T7Maps_CustomMapsLua(), "=bo3customs_maps");
                RunLua(k_refreshedLua, "=bo3customs_refreshed");
            }
        }

        if (T7Share_TakeMapsChanged())
        {
            RunLua(T7Maps_CustomMapsLua(), "=bo3customs_maps");
            RunLua(k_refreshedLua, "=bo3customs_refreshed");
        }

        HandleUninstall();
        HandleModChoice();
        HandleModApply();
        HandleQuit();
        RunLua(k_tickLua, "=bo3customs_tick");
#ifdef _DEBUG
        FlushLuaLog();
#endif
    }

    UiUnlock();
}

struct LuiFile
{
    const char* asset;
    char*       data;
    int32_t     length;
    bool        tried;
};

struct RawFileAsset
{
    const char* name;
    int32_t     length;
    int32_t     padding;
    const char* buffer;
};

static const char* const k_graphicsDvars[] = { "r_vsync",
                                               "com_maxfps",
                                               "cg_drawFPS",
                                               "cg_fov",
                                               "r_modelLodLimit",
                                               "r_lightingSunShadowDisableDynamicDraw",
                                               "r_sssblurEnable",
                                               "r_volumetric_lighting_enabled",
                                               "r_aaTechnique",
                                               "r_ssaoTechnique",
                                               "r_motionBlurMode",
                                               kSplitScreenProxy };
constexpr int kGraphicsCount = sizeof(k_graphicsDvars) / sizeof(k_graphicsDvars[0]);

static char g_graphics[kGraphicsCount][32] = {};
static bool g_graphicsLoaded = false;

static const char* GraphicsName(const char* line)
{
    while (*line == ' ' || *line == '\t')
        ++line;

    if ((line[0] == 's' || line[0] == 'S') && (line[1] == 'e' || line[1] == 'E') && (line[2] == 't' || line[2] == 'T') &&
        (line[3] == ' ' || line[3] == '\t'))
    {
        line += 4;

        while (*line == ' ' || *line == '\t')
            ++line;
    }

    return line;
}

static bool GraphicsOwns(const char* line)
{
    const char* const name = GraphicsName(line);

    for (int i = 0; i < kGraphicsCount; ++i)
    {
        const size_t length = strlen(k_graphicsDvars[i]);

        if (strncmp(name, k_graphicsDvars[i], length) == 0 &&
            (name[length] == ' ' || name[length] == '\t' || !name[length]))
            return true;
    }

    return false;
}

static void LoadGraphics()
{
    g_graphicsLoaded = true;

    static char text[32768];

    if (!T7Config_Read(text, sizeof(text)))
        return;

    for (char* line = text; line && *line;)
    {
        char* next = strchr(line, '\n');

        if (next)
            *next++ = 0;

        char* const name = (char*)GraphicsName(line);
        char* const space = strchr(name, ' ');

        if (space && name[0] != '/')
        {
            *space = 0;
            char* const value = space + 1;
            size_t n = strlen(value);

            while (n > 0 && (value[n - 1] == '\r' || value[n - 1] == ' '))
                value[--n] = 0;

            for (int i = 0; i < kGraphicsCount; ++i)
            {
                if (strcmp(name, k_graphicsDvars[i]) == 0)
                    snprintf(g_graphics[i], sizeof(g_graphics[i]), "%s", value);
            }
        }

        line = next;
    }
}

static void SaveGraphics()
{
    char text[2048];
    int n = 0;
    text[0] = 0;

    for (int i = 0; i < kGraphicsCount && n >= 0 && (size_t)n < sizeof(text); ++i)
    {
        if (g_graphics[i][0])
            n += snprintf(text + n, sizeof(text) - (size_t)n, "set %s %s\n", k_graphicsDvars[i], g_graphics[i]);
    }

    if (n <= 0 || (size_t)n >= sizeof(text))
        return;

    T7Config_Save("graphics", GraphicsOwns, text);
}

static const uint8_t k_frameLimitSkip[] = { 0x84, 0xC0, 0x74, 0x20, 0xB8, 0xE8, 0x03, 0x00, 0x00 };

static void PatchFrameLimit()
{
    const uintptr_t at = g_base + kFrameLimitSkip;
    const size_t size = sizeof(k_frameLimitSkip);

    if (!RangeReadable(at, size))
        return;

    const uint8_t* const code = (const uint8_t*)at;
    const bool around = memcmp(code, k_frameLimitSkip, 2) == 0 && memcmp(code + 4, k_frameLimitSkip + 4, size - 4) == 0;

    if (!around || code[2] != 0x74 || code[3] != 0x20)
        return;

    const uintptr_t page = (at + 2) & ~0x3FFFull;

    if (sceKernelMprotect((const void*)page, ((at + 3) & ~0x3FFFull) - page + 0x4000, 7) < 0)
        return;

    *(volatile uint16_t*)(at + 2) = 0x9090;
}

static const uint8_t k_splitNarrow[] = { 0x83, 0xF8, 0x01, 0x75, 0x28, 0xC7, 0x05, 0xEB,
                                         0xBE, 0xF0, 0x00, 0xCD, 0xCC, 0xCC, 0x3D };

static void FillSplitRow(float* row, float y, float h)
{
    if (row[1] == y && row[3] == h && row[2] > 0.0f)
    {
        row[0] = 0.0f;
        row[2] = 1.0f;
    }
}

static void PatchSplitScreen()
{
    const uintptr_t at = g_base + kSplitNarrow;
    const size_t size = sizeof(k_splitNarrow);

    if (!RangeReadable(at, size) || memcmp((const void*)at, k_splitNarrow, size) != 0)
        return;

    const uintptr_t page = (at + 3) & ~0x3FFFull;

    if (sceKernelMprotect((const void*)page, 0x4000, 7) < 0)
        return;

    *(volatile uint8_t*)(at + 3) = 0xEB;

    float* const wide = (float*)(g_base + kSplitLayouts) + 64;

    FillSplitRow(wide + 16, 0.0f, 0.5f);
    FillSplitRow(wide + 20, 0.5f, 0.5f);
    FillSplitRow(wide + 40, 0.5f, 0.5f);
}

static const uint8_t k_viewValues[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55,
                                        0x41, 0x54, 0x53, 0x48, 0x81, 0xEC, 0xB8, 0x01, 0x00, 0x00 };
static const uint8_t k_importStub[] = { 0xFF, 0x25 };

static Detour g_viewDetour{};
static void*  g_viewOriginal = nullptr;
static float  g_viewWiden = 0.0f;
static float  g_viewWidenFor = -1.0f;

using ViewValues_t = uint64_t (*)(uint64_t, uint64_t, uint64_t, uint64_t, uint64_t, uint64_t,
                                  double, double, double, double, double, double, double, double);
using Float_t = float (*)(float);

static float Tan(float x)
{
    return ((Float_t)(g_base + kTanf))(x);
}

static float Atan(float x)
{
    return ((Float_t)(g_base + kAtanf))(x);
}

static float ParseNumber(const char* text)
{
    float value = 0.0f;
    float step = 0.0f;

    for (const char* at = text; *at; ++at)
    {
        if (*at >= '0' && *at <= '9')
        {
            if (step == 0.0f)
            {
                value = value * 10.0f + (float)(*at - '0');
            }
            else
            {
                value += (float)(*at - '0') * step;
                step *= 0.1f;
            }
        }
        else if (*at == '.' && step == 0.0f)
        {
            step = 0.1f;
        }
        else
        {
            break;
        }
    }

    return value;
}

static bool InUiLevel()
{
    return strcmp((const char*)(g_base + kCurrentMap), "core_frontend") == 0;
}

static void WidenView(int32_t localClient)
{
    const float widen = g_viewWiden;

    if (widen <= 0.0f || localClient < 0 || localClient >= *(const int32_t*)(g_base + kLocalClients))
        return;

    const uintptr_t clients = *(const uintptr_t*)(g_base + kCgArray);

    if (!clients || InUiLevel())
        return;

    const uintptr_t cg = clients + (uintptr_t)localClient * kCgSize;

    if (*(const float*)(cg + kCgAnimLens) > 0.0f)
        return;

    float ads = *(const float*)(cg + kCgAdsFraction);

    if (!(ads >= 0.0f))
        ads = 0.0f;
    else if (ads > 1.0f)
        ads = 1.0f;

    const float scale = 1.0f + (widen - 1.0f) * (1.0f - ads);

    *(float*)(cg + kCgTanHalfFovX) *= scale;
    *(float*)(cg + kCgTanHalfFovY) *= scale;
    *(float*)(cg + kCgMainTanFovY) *= scale;

    float* const fov = (float*)(cg + kCgFovX);
    *fov = 2.0f * Atan(Tan(*fov * 0.5f * kDegToRad) * scale) / kDegToRad;
}

static uint64_t ViewValues(uint64_t localClient, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                           double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    const uint64_t result =
        ((ViewValues_t)g_viewOriginal)(localClient, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);

    float fov;
    memcpy(&fov, &x0, sizeof(fov));

    if (fov < 0.0f)
        WidenView((int32_t)localClient);

    return result;
}

static void HookView()
{
    const uintptr_t at = g_base + kViewValues;

    const bool matches = RangeReadable(at, sizeof(k_viewValues)) &&
                         memcmp((const void*)at, k_viewValues, sizeof(k_viewValues)) == 0 &&
                         RangeReadable(g_base + kTanf, sizeof(k_importStub)) &&
                         memcmp((const void*)(g_base + kTanf), k_importStub, sizeof(k_importStub)) == 0 &&
                         RangeReadable(g_base + kAtanf, sizeof(k_importStub)) &&
                         memcmp((const void*)(g_base + kAtanf), k_importStub, sizeof(k_importStub)) == 0;

    if (matches)
        Detour_Attach(&g_viewDetour, (uint64_t)at, (void*)ViewValues, &g_viewOriginal);
}

static void UpdateViewWiden()
{
    const float fov = g_graphics[3][0] ? ParseNumber(g_graphics[3]) : 0.0f;

    if (fov == g_viewWidenFor)
        return;

    g_viewWidenFor = fov;

    if (!g_viewOriginal || fov < 10.0f || fov > 160.0f || fov == kStockFov)
    {
        g_viewWiden = 0.0f;
        return;
    }

    g_viewWiden = Tan(fov * 0.5f * kDegToRad) / Tan(kStockFov * 0.5f * kDegToRad);
}

static bool NameListed(const char* list, const char* name)
{
    const size_t length = strlen(name);

    for (const char* at = list; *at;)
    {
        while (*at == ' ')
            ++at;

        const char* end = at;

        while (*end && *end != ' ')
            ++end;

        if ((size_t)(end - at) == length && strncmp(at, name, length) == 0)
            return true;

        at = end;
    }

    return false;
}

static void ApplySplitScreen()
{
    const uintptr_t proxy = FindDvar(kSplitScreenProxy);
    const uintptr_t dvar = FindDvar(kSplitScreenDvar);

    if (!proxy || !dvar || *(const int32_t*)(dvar + kDvarType) != kDvarTypeBool)
        return;

    const char* const text = DvarText(proxy);
    const uint8_t wanted = text && atoi(text) != 0 ? 1 : 0;

    if (*(const uint8_t*)(dvar + kDvarValue) == wanted)
        return;

    SetDvar(kSplitScreenDvar, wanted ? "1" : "0");
    *(volatile uint8_t*)(dvar + kDvarValue) = wanted;
}

static void PollGraphics()
{
    if (!g_graphicsLoaded)
        LoadGraphics();

    UpdateViewWiden();

    const uintptr_t save = FindDvar("bo3customs_graphics_save");
    const char* const pending = save ? DvarText(save) : nullptr;

    if (pending && *pending && strcmp(pending, "0") != 0)
    {
        char names[512];
        snprintf(names, sizeof(names), "%s", pending);
        SetDvar("bo3customs_graphics_save", "0");

        const bool all = atoi(names) != 0;

        for (int i = 0; i < kGraphicsCount; ++i)
        {
            if (!all && !NameListed(names, k_graphicsDvars[i]))
                continue;

            const uintptr_t dvar = FindDvar(k_graphicsDvars[i]);
            const char* const text = dvar ? DvarText(dvar) : nullptr;

            if (text && *text)
                snprintf(g_graphics[i], sizeof(g_graphics[i]), "%s", text);
        }

        SaveGraphics();
        UpdateViewWiden();
        ApplySplitScreen();
        return;
    }

    for (int i = 0; i < kGraphicsCount; ++i)
    {
        if (!g_graphics[i][0])
            continue;

        const uintptr_t dvar = FindDvar(k_graphicsDvars[i]);
        const char* const text = dvar ? DvarText(dvar) : nullptr;

        if (!dvar && strncmp(k_graphicsDvars[i], "bo3customs_", 11) == 0)
            SetDvar(k_graphicsDvars[i], g_graphics[i]);
        else if (text && strcmp(text, g_graphics[i]) != 0)
            SetDvar(k_graphicsDvars[i], g_graphics[i]);
    }

    ApplySplitScreen();
}

static char* ReadLuiCopy(const char* asset, size_t* length)
{
    char lower[160];
    size_t n = 0;
    int folders = 0;
    size_t cut = 0;

    for (; asset[n] && n < sizeof(lower) - 1; ++n)
    {
        const char c = asset[n];
        lower[n] = (c >= 'A' && c <= 'Z') ? (char)(c + 32) : (c == '\\' ? '/' : c);

        if (lower[n] == '/' && ++folders == kLuiMaxFolders)
            cut = n;
    }

    lower[n] = 0;

    if (asset[n] || !T7LuaFiles::SafePath(lower, false))
        return nullptr;

    const char* const slash = strrchr(lower, '/');

    if (folders > kLuiMaxFolders && slash)
        memmove(lower + cut, slash, strlen(slash) + 1);

    char relative[256];
    const int count = snprintf(relative, sizeof(relative), "lui/%s", lower);
    return count > 0 && (size_t)count < sizeof(relative)
        ? T7LuaFiles::Read(relative, 1024 * 1024, length, false) : nullptr;
}

static LuiFile g_luiFiles[] =
{
    { "ui/t7/utility/pcutility.lua", nullptr, 0, false },
    { "ui/uieditor/menus/mods/menumodsload.lua", nullptr, 0, false },
    { "ui/uieditor/widgets/mods/widgetmodsload.lua", nullptr, 0, false },
    { "ui/uieditor/widgets/mods/entrymodsload.lua", nullptr, 0, false },
};

static RawFileAsset g_luiAssets[sizeof(g_luiFiles) / sizeof(g_luiFiles[0])] = {};

constexpr size_t kLuiFileCount = sizeof(g_luiFiles) / sizeof(g_luiFiles[0]);

static const RawFileAsset* LuiFileFor(const char* name)
{
    for (size_t i = 0; i < kLuiFileCount; ++i)
    {
        LuiFile& file = g_luiFiles[i];
        size_t at = 0;

        while (name[at] && file.asset[at] &&
               (name[at] == file.asset[at] || ((name[at] | 0x20) == file.asset[at] && name[at] >= 'A' && name[at] <= 'Z')))
            ++at;

        if (name[at] != 0 || file.asset[at] != 0)
            continue;

        if (!file.tried)
        {
            file.tried = true;

            size_t used = 0;
            char* const body = ReadLuiCopy(file.asset, &used);
            if (!body)
                return nullptr;

            file.data = body;
            file.length = (int32_t)used;
            g_luiAssets[i].name = file.asset;
            g_luiAssets[i].length = (int32_t)used;
            g_luiAssets[i].padding = 0;
            g_luiAssets[i].buffer = body;
        }

        return file.data != nullptr ? &g_luiAssets[i] : nullptr;
    }

    return nullptr;
}

struct LuiExtra
{
    char         name[160];
    RawFileAsset asset;
    bool         found;
};

constexpr int kLuiExtraMax = 96;

static LuiExtra          g_luiExtra[kLuiExtraMax] = {};
static int               g_luiExtraCount = 0;
static std::atomic<bool> g_luiExtraLock{false};

static bool LuaMissing(uint64_t result)
{
    if (!result || !RangeReadable(result, sizeof(RawFileAsset)))
        return true;

    const RawFileAsset* const raw = (const RawFileAsset*)result;
    return !raw->buffer || raw->length <= 0;
}

static const RawFileAsset* LuiExtraFor(const char* name)
{
    char lower[160];
    size_t n = 0;

    for (; name[n] && n < sizeof(lower) - 1; ++n)
    {
        const char c = name[n];
        lower[n] = (c >= 'A' && c <= 'Z') ? (char)(c + 32) : (c == '\\' ? '/' : c);
    }

    lower[n] = 0;

    if (name[n] || strstr(lower, ".."))
        return nullptr;

    while (g_luiExtraLock.exchange(true, std::memory_order_acquire))
        sceKernelUsleep(50);

    const RawFileAsset* served = nullptr;
    bool known = false;

    for (int i = 0; i < g_luiExtraCount && !known; ++i)
    {
        if (strcmp(g_luiExtra[i].name, lower) == 0)
        {
            known = true;
            served = g_luiExtra[i].found ? &g_luiExtra[i].asset : nullptr;
        }
    }

    if (!known && g_luiExtraCount < kLuiExtraMax)
    {
        LuiExtra& extra = g_luiExtra[g_luiExtraCount];
        snprintf(extra.name, sizeof(extra.name), "%s", lower);
        extra.found = false;

        size_t used = 0;
        char* const body = ReadLuiCopy(lower, &used);
        if (body)
        {
            extra.asset.name = extra.name;
            extra.asset.length = (int32_t)used;
            extra.asset.padding = 0;
            extra.asset.buffer = body;
            extra.found = true;
        }

        if (extra.found)
            T7Log_Write("[Lua] %s is not in the PS4 game; using the PC copy in lui", lower);
        else
            T7Log_Write("[Lua] %s is not in the PS4 game and no complete copy was readable in the Lua roots", lower);
        served = extra.found ? &extra.asset : nullptr;

        ++g_luiExtraCount;
    }

    g_luiExtraLock.store(false, std::memory_order_release);
    return served;
}

static uint64_t FindXAsset(uint64_t type, uint64_t name, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                           double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    char asset[192];
    bool lua = false;

    if ((int32_t)type == kRawFileType && name != 0 && RangeReadable(name, 16))
    {
        const char* const text = (const char*)name;
        size_t length = 0;

        while (length < sizeof(asset) - 1 && text[length] != 0)
            ++length;

        if (length > 4 && memcmp(text + length - 4, ".lua", 4) == 0)
        {
            memcpy(asset, text, length);
            asset[length] = 0;
            lua = true;
            T7Log_LuiFile(asset);

            if (const RawFileAsset* const ours = LuiFileFor(asset))
            {
                T7Log_LuiBuffer(asset, (uintptr_t)ours);
                return (uint64_t)ours;
            }
        }
    }

    const uint64_t result =
        ((Passthrough_t)g_findAssetOriginal)(type, name, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);

    if (!result && (int32_t)type == kRawFileType && name != 0 &&
        (uintptr_t)__builtin_return_address(0) == g_base + kExecFindReturn)
    {
        static char empty[1] = { 0 };
        static RawFileAsset blank = {};
        blank.name = "(missing cfg)";
        blank.length = 0;
        blank.padding = 0;
        blank.buffer = empty;
        T7Log_Write("[Cmd] exec of a missing file \"%s\" does nothing, as on PC", RangeReadable(name, 4) ? (const char*)name : "?");
        return (uint64_t)&blank;
    }

    if (lua && LuaMissing(result))
    {
        if (const RawFileAsset* const extra = LuiExtraFor(asset))
        {
            T7Log_LuiBuffer(asset, (uintptr_t)extra);
            return (uint64_t)extra;
        }
    }

    if (lua)
        T7Log_LuiBuffer(asset, (uintptr_t)result);

    return result;
}

static void AddPcUtil();

static void CheckMenus()
{
    if (!UiUp() || !UiTryLock())
        return;

    bool broken = false;

    if (UiUp())
    {
        SetDvar("bo3customs_menus", "0");
        RunLua(k_menusProbeLua, "=bo3customs_menus_probe");

        const uintptr_t dvar = FindDvar("bo3customs_menus");
        const char* const text = dvar ? DvarText(dvar) : nullptr;
        broken = text && atoi(text) == 2;
    }

    UiUnlock();

    if (broken)
        T7Maps_MenusBroken();
}

static uint64_t Require_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                          double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    const uintptr_t L = (uintptr_t)a1;

    if (L && L == *(const uintptr_t*)(g_base + kLuaState) && T7Maps_UsingMods() && UiTryLock())
    {
        RunLuaOn(L, T7Maps_ModFilesLua(), "=bo3customs_mod_files");
        RunLuaOn(L, k_lobbyParityLua, "=bo3customs_lobby");
        RunLuaOn(L, k_pcParityLua, "=bo3customs_parity_early");
        UiUnlock();
    }

    if (L && L == *(const uintptr_t*)(g_base + kLuaState) && T7Maps_UsingMods() && UiTryLock())
    {
        const char* const module = (const char*)a2;
        const bool dll = T7Maps_DllModLoaded();

        if (dll && module && RangeReadable(a2, 11) && strcmp(module, "ui.LUI.LUI") == 0)
            T7Log_Write("[Lua] a new UI state starts; stand-ins for the T7Overcharged DLL go in first");

        char ugc[96];
        char save[192];
        T7Maps_FirstModUgc(ugc, sizeof(ugc));
        T7Maps_ModSave(save, sizeof(save));

        char lua[512];
        size_t used = 0;
    AppendLuaFormat(lua, sizeof(lua), &used, "rawset(_G, 'BO3CustomsDllUgc', ");
        AppendQuoted(lua, sizeof(lua), &used, ugc);
        AppendLuaFormat(lua, sizeof(lua), &used, ")\nrawset(_G, 'BO3CustomsDllSave', ");
        AppendQuoted(lua, sizeof(lua), &used, save);
        AppendLuaFormat(lua, sizeof(lua), &used, ")\nrawset(_G, 'BO3CustomsDllMod', %s)\n", dll ? "true" : "false");

        if (used < sizeof(lua))
            RunLuaOn(L, lua, "=bo3customs_dll_ugc");
        RunLuaOn(L, k_dllStandInLua, "=bo3customs_dll");
        UiUnlock();
    }

    return ((Passthrough_t)g_requireOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
}

static void HookRequire()
{
    static const uint8_t k_require[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54,
                                         0x53, 0x48, 0x83, 0xEC, 0x18 };

    if (!g_base || !RangeReadable(g_base + kLuaRequire, sizeof(k_require)) ||
        memcmp((const void*)(g_base + kLuaRequire), k_require, sizeof(k_require)) != 0)
        return;

    Detour_Attach(&g_requireDetour, (uint64_t)(g_base + kLuaRequire), (void*)Require_h, &g_requireOriginal);
}

static uint64_t AddMenu_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                          double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    const char* const menu = (const char*)a2;

    if (menu && RangeReadable(a2, 5) && strcmp(menu, "main") == 0 && InMenus())
    {
        T7Maps_MenusBuilt();
        g_forceInject = true;
        AddPcUtil();
        InjectScripts();
        g_forceInject = false;
        CheckMenus();
    }

    return ((Passthrough_t)g_addMenuOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
}

static void HookAddMenu()
{
    static const uint8_t k_addMenu[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x54, 0x53, 0x48,
                                         0x83, 0xEC, 0x70 };

    if (!g_base || !RangeReadable(g_base + kLuiAddMenu, sizeof(k_addMenu)) ||
        memcmp((const void*)(g_base + kLuiAddMenu), k_addMenu, sizeof(k_addMenu)) != 0)
        return;

    Detour_Attach(&g_addMenuDetour, (uint64_t)(g_base + kLuiAddMenu), (void*)AddMenu_h, &g_addMenuOriginal);
}

static void HookFindXAsset()
{
    static const uint8_t k_findXAsset[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54,
                                            0x53, 0x48, 0x83, 0xEC, 0x68, 0x48, 0x8B, 0x05 };

    if (!g_base || !RangeReadable(g_base + kFindXAsset, sizeof(k_findXAsset)) ||
        memcmp((const void*)(g_base + kFindXAsset), k_findXAsset, sizeof(k_findXAsset)) != 0)
        return;

    Detour_Attach(&g_findAssetDetour, (uint64_t)(g_base + kFindXAsset), (void*)FindXAsset, &g_findAssetOriginal);
}

static void TraceStateLocked(const char* reason)
{
#ifdef _DEBUG
    // Caller owns the UI lock. Keep this helper nonblocking and lock-free.
    static uintptr_t lastState = 0;
    static uint32_t observedGeneration = 0;
    const uintptr_t state = *(const uintptr_t*)(g_base + kLuaState);
    if (state != lastState)
    {
        ++observedGeneration;
        lastState = state;
    }
    T7Log_Write("[Transition] UI snapshot (%s): observed-generation=%u state=0x%llX "
                "utility-state=0x%llX utility-matches-current=%d mouse-state=0x%llX "
                "mouse-matches-current=%d ready=%d force-inject=%d",
                reason, observedGeneration, (unsigned long long)state, (unsigned long long)g_pcUtilState,
                state != 0 && state == g_pcUtilState ? 1 : 0, (unsigned long long)g_mouseState,
                state != 0 && state == g_mouseState ? 1 : 0, UiUp() ? 1 : 0, g_forceInject ? 1 : 0);
#else
    (void)reason;
#endif
}

static void AddPcUtil()
{
    const uintptr_t L = g_base != 0 ? *(const uintptr_t*)(g_base + kLuaState) : 0;
    const bool fresh = L != g_pcUtilState;

    // Reinject UI compatibility helpers less frequently during gameplay.
    // Their Lua compilation/dispatch is unnecessary every six rendered frames;
    // freshly created UI states and explicit injection requests still run now.
    const uint32_t interval = T7Maps_InLevel() ? 30u : 6u;
    if (L == 0 || (!fresh && !g_forceInject && (g_ticks % interval) != 0) || !UiUp() || !UiTryLock())
        return;

    if (UiUp())
    {
        if (fresh)
        {
            T7Log_Write("[Lua] new UI state for %s", (const char*)(g_base + kCurrentMap));
            TraceStateLocked("new UI state");
        }

        RunLua(k_menuWatchLua, "=bo3customs_menuwatch");
        RunLua(k_isPcLua, "=bo3customs_ispc");
        RunLua(k_pcUtilityLua, "=bo3customs_pcutility");
        RunLua(k_pcUtilLua, "=bo3customs_pcutil");
        RunLua(k_globalAliasLua, "=bo3customs_alias");
        RunLua(k_pcParityLua, "=bo3customs_parity");
        RunLua(k_lobbyMapsLua, "=bo3customs_lobbymaps");
        RunLua(k_zombieSeedLua, "=bo3customs_seed");
        g_pcUtilState = L;

        RunLua(k_graphicsProbeLua, "=bo3customs_graphics_probe");

        if (DvarOn("bo3customs_graphics_pending"))
        {
            SetDvar("bo3customs_graphics_pending", "0");

            char* const script = ReadScript("graphics.lua");

            if (script)
            {
                RunLua(script, "@ui_scripts/graphics.lua");
                free(script);
            }
        }

        RunLua(k_restartProbeLua, "=bo3customs_restart_probe");

        if (DvarOn("bo3customs_restart_pending"))
        {
            SetDvar("bo3customs_restart_pending", "0");

            char* const script = ReadScript("restart.lua");

            if (script)
            {
                RunLua(script, "@ui_scripts/restart.lua");
                free(script);
            }
        }

        RunLua(k_mouseProbeLua, "=bo3customs_mouse_probe");

        if (DvarOn("bo3customs_mouse_pending"))
        {
            SetDvar("bo3customs_mouse_pending", "0");

            char* const strings = ReadScript("kbm_strings.lua");

            if (strings)
            {
                RunLua(strings, "@ui_scripts/kbm_strings.lua");
                free(strings);
            }

            char* const script = ReadScript("mouse.lua");

            if (script)
            {
                if (RunLua(script, "@ui_scripts/mouse.lua"))
                    g_mouseState = L;

                free(script);
            }

            T7Log_Write("[Lua] our scripts went into the UI state for %s", (const char*)(g_base + kCurrentMap));
        }

        RunLua(k_modsDataProbeLua, "=bo3customs_mods_data_probe");

        if (DvarOn("bo3customs_mods_data") || T7Maps_ModsChanged())
        {
            SetDvar("bo3customs_mods_data", "0");
            RunLua(T7Maps_ModsLua(), "=bo3customs_mods");
        }

        RunLua(k_modsProbeLua, "=bo3customs_mods_probe");

        if (DvarOn("bo3customs_mods_pending"))
        {
            SetDvar("bo3customs_mods_pending", "0");

            char* const script = ReadScript("mods.lua");

            if (script)
            {
                RunLua(script, "@ui_scripts/mods.lua");
                free(script);
            }
        }
    }

    UiUnlock();
}
}

__asm__(
    ".text\n"
    ".globl T7Lua_ThreadPointer\n"
    "T7Lua_ThreadPointer:\n"
    "    movq %fs:0, %rax\n"
    "    retq\n");

void T7Lua_Install(uintptr_t base)
{
    using namespace T7Lua;

    static bool installed = false;

    if (installed || !base)
        return;

    installed = true;
    g_base = base;


    HookFindXAsset();
    HookAddMenu();
    HookRequire();
    PatchFrameLimit();
    PatchSplitScreen();
    HookView();
}

void T7Lua_Tick()
{
    using namespace T7Lua;

    if (!g_base)
        return;

    ++g_ticks;

#ifdef _DEBUG
    // Dvar-to-disk diagnostics have no frame-critical ordering requirement.
    // Keep menu log feedback responsive; batch during active matches to
    // avoid synchronously scanning/formatting logs ten times per second.
    const uint32_t logInterval = T7Maps_InLevel() ? 30u : 6u;
    if ((g_ticks % logInterval) == 0)
    {
        FlushDvarLog("bo3customs_gsc_log", "ScriptLog");
        if (!InMenus())
            FlushDvarLog("bo3customs_log", "ScriptLog");
    }
#endif

    AddPcUtil();

    if ((g_ticks % 15) == 0)
        ShowShare();

    if ((g_ticks % 30) != 0)
        return;

    PollGraphics();

    if (InMenus())
    {
        InjectScripts();
    }
    else if (!T7Maps_InLevel() && UiTryLock())
    {
        // Lobby housekeeping does not need to run while the game is active.
        if (UiUp())
            RunLua(k_lobbyTickLua, "=bo3customs_lobbytick");

        UiUnlock();
    }
}

bool T7Lua_MouseReady()
{
    using namespace T7Lua;

    if (!g_base || !UiUp())
        return false;

    const uintptr_t L = *(const uintptr_t*)(g_base + kLuaState);
    return L != 0 && L == g_mouseState;
}

bool T7Lua_DvarString(const char* name, char* out, size_t size)
{
    using namespace T7Lua;

    if (!out || !size)
        return false;

    out[0] = 0;

    if (!g_base || !name)
        return false;

    const uintptr_t dvar = FindDvar(name);
    const char* const text = dvar ? DvarText(dvar) : nullptr;

    if (!text)
        return false;

    snprintf(out, size, "%s", text);
    return true;
}

void T7Lua_TraceState(const char* reason)
{
#ifdef _DEBUG
    using namespace T7Lua;
    if (!g_base) return;
    if (!UiTryLock())
    {
        T7Log_Write("[Transition] UI snapshot (%s): busy; no Lua inspected", reason);
        return;
    }
    TraceStateLocked(reason);
    UiUnlock();
#else
    (void)reason;
#endif
}

bool T7Lua_IsUiState(uintptr_t L)
{
    return T7Lua::g_base && L == *(const uintptr_t*)(T7Lua::g_base + T7Lua::kLuaState);
}

void T7Lua_RunShareVm(uintptr_t L)
{
    using namespace T7Lua;

    if (g_base && L)
        RunLuaOn(L, k_shareVmLua, "=bo3customs_share_vm");
}

bool T7Lua_RunOnTop(uintptr_t L, const char* source, const char* chunk)
{
    using namespace T7Lua;

    const uintptr_t global = L ? *(const uintptr_t*)(L + kStateGlobal) : 0;

    if (!g_base || !L || !global || !source)
        return false;

    if (*(const uint64_t*)(L + kStateTop) + 48 > *(const uint64_t*)(L + kStateStackLast))
        ((void (*)(uintptr_t, uintptr_t, uint64_t))(g_base + kLuaGrowStack))(L + 24, L, 3);

    const uint64_t topOffset = *(const uint64_t*)(L + kStateTop) - *(const uint64_t*)(L + kStateStack);

    if (topOffset < 16)
        return false;

    HksBuffer buffer = { source, strlen(source) };
    int32_t* const sharing = (int32_t*)(global + kGlobalSharing);
    const int32_t sharingWas = *sharing;

    *sharing = kSharingOn;

    using Load_t = int32_t (*)(uintptr_t, uintptr_t, uintptr_t, HksBuffer*, const char*);
    const int32_t loaded = ((Load_t)(g_base + kHksLoad))(L, global + kGlobalCompiler, g_base + kHksBufferReader,
                                                         &buffer, chunk);

    *sharing = sharingWas;

    if (loaded != 0)
    {
        T7Log_LuaFailure(L, topOffset, chunk, "compile");
        DropFailure(L, topOffset);
        return false;
    }

    const uint64_t top = *(const uint64_t*)(L + kStateTop);
    memcpy((void*)top, (const void*)(top - 32), 16);
    *(uint64_t*)(L + kStateTop) = top + 16;

    using Pcall_t = int32_t (*)(uintptr_t, int32_t, int32_t, int32_t);

    if (((Pcall_t)(g_base + kLuaPcall))(L, 1, 0, 0) != 0)
    {
        T7Log_LuaFailure(L, topOffset, chunk, "run");
        DropFailure(L, topOffset);
        return false;
    }

    return true;
}
