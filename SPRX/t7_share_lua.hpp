#pragma once

static const char k_shareVmLua[] = R"LUA(
local G = _G
local vm = rawget(G, 'LobbyVM')
local engine = rawget(G, 'Engine')
if type(vm) ~= 'table' or engine == nil then return end
local ask = engine.GetDLCBitForMapName
if type(ask) ~= 'function' then return end
local launch = rawget(vm, 'ClientLaunch')
if type(launch) ~= 'function' then
  pcall(ask, 'bo3customs:plain')
  return
end
if launch == rawget(G, 'BO3CustomsShareLaunch') then
  pcall(ask, 'bo3customs:wrapped')
  return
end
local enum = rawget(G, 'Enum')
local states = enum ~= nil and enum.LaunchGameState or nil
local first = states ~= nil and states.LAUNCH_GAME_STATE_START or 0
local pump = states ~= nil and states.LAUNCH_GAME_STATE_PUMP or 1
local wrapped = function(event, ...)
  local known, stage = pcall(function() return event.stage end)
  if known and (stage == first or stage == pump) then
    local answered, held = pcall(ask, 'bo3customs:hold')
    if answered and held == 1 then return false end
  end
  return launch(event, ...)
end
rawset(G, 'BO3CustomsShareLaunch', wrapped)
rawset(vm, 'ClientLaunch', wrapped)
pcall(ask, 'bo3customs:wrapped')
)LUA";

static const char k_shareProbeLua[] =
    "local engine = rawget(_G, 'Engine')\n"
    "if engine ~= nil and rawget(_G, 'BO3CustomsShare') == nil then engine.SetDvar('bo3customs_share_pending', 1) end\n";

static const char k_shareHideLua[] =
    "local share = rawget(_G, 'BO3CustomsShare')\n"
    "if share ~= nil and share.Hide ~= nil then share.Hide() end\n";

static const char k_shareUiLua[] = R"LUA(
local G = _G
if rawget(G, 'BO3CustomsShare') ~= nil then return end
local lui = rawget(G, 'LUI')
local engine = rawget(G, 'Engine')
local enum = rawget(G, 'Enum')
if type(lui) ~= 'table' or engine == nil then return end
if type(lui.UIElement) ~= 'table' or type(lui.UIImage) ~= 'table' or type(lui.UIText) ~= 'table' then return end

local Share = { panels = {}, visible = false, since = 0, cancel = false }
rawset(G, 'BO3CustomsShare', Share)

local half = 360

local function Now()
  local ok, ms = pcall(engine.milliseconds)
  if ok and type(ms) == 'number' then return ms end
  return 0
end

local function Fill(parent, left, right, top, bottom, r, g, b, alpha)
  local image = lui.UIImage.new()
  image:setLeftRight(false, false, left, right)
  image:setTopBottom(false, false, top, bottom)
  image:setRGB(r, g, b)
  image:setAlpha(alpha)
  parent:addElement(image)
  return image
end

local function Text(parent, top, size, r, g, b)
  local text = lui.UIText.new()
  text:setLeftRight(false, false, -600, 600)
  text:setTopBottom(false, false, top, top + size)
  text:setTTF('fonts/default.ttf')
  if enum ~= nil and enum.LUIAlignment ~= nil and enum.LUIAlignment.LUI_ALIGNMENT_CENTER ~= nil then
    text:setAlignment(enum.LUIAlignment.LUI_ALIGNMENT_CENTER)
  end
  text:setRGB(r, g, b)
  text:setText('')
  parent:addElement(text)
  return text
end

local function Build(root)
  local panel = lui.UIElement.new()
  panel:setLeftRight(true, true, 0, 0)
  panel:setTopBottom(true, true, 0, 0)
  panel:setPriority(900)
  panel:setAlpha(0)
  local shade = lui.UIImage.new()
  shade:setLeftRight(true, true, 0, 0)
  shade:setTopBottom(true, true, 0, 0)
  shade:setRGB(0, 0, 0)
  shade:setAlpha(0.94)
  panel:addElement(shade)
  local made = { panel = panel }
  made.title = Text(panel, -130, 34, 1, 1, 1)
  made.name = Text(panel, -80, 26, 1, 0.5, 0.1)
  Fill(panel, -half - 2, half + 2, -24, -6, 0.7, 0.7, 0.7, 1)
  Fill(panel, -half, half, -22, -8, 0.05, 0.05, 0.05, 1)
  made.bar = Fill(panel, -half, -half, -22, -8, 1, 0.5, 0.1, 1)
  made.detail = Text(panel, 12, 22, 0.9, 0.9, 0.9)
  made.file = Text(panel, 46, 18, 0.6, 0.6, 0.6)
  made.speeds = Text(panel, 72, 18, 0.6, 0.6, 0.6)
  made.hint = Text(panel, 110, 18, 0.6, 0.6, 0.6)
  root:addElement(panel)
  return made
end

local function Watch(made)
  if enum == nil or enum.LUIButton == nil or enum.LUIButton.LUI_KEY_XBB_PSCIRCLE == nil then return end
  local controller = engine.GetPrimaryController()
  local owner = engine.GetModelForController(controller)
  if owner == nil then return end
  local model = engine.CreateModel(owner, 'ButtonBits.' .. enum.LUIButton.LUI_KEY_XBB_PSCIRCLE)
  if model == nil then return end
  made.panel:subscribeToModel(model, function(changed)
    local value = engine.GetModelValue(changed)
    if Share.visible and Share.cancel and type(value) == 'number' and value ~= 0 and Now() - Share.since > 1500 then
      engine.SetDvar('bo3customs_share_cancel', 1)
    end
  end, false)
end

local function Root()
  local roots = rawget(lui, 'roots')
  if type(roots) ~= 'table' then return nil end
  local full = roots.UIRootFull
  if full ~= nil then return full end
  for _, root in pairs(roots) do
    return root
  end
  return nil
end

local function Panel()
  local root = Root()
  if root == nil then return nil end
  local made = Share.panels[root]
  if made == nil then
    local ok, built = pcall(Build, root)
    if not ok or built == nil then return nil end
    made = built
    Share.panels[root] = made
    pcall(Watch, made)
  end
  return made
end

Share.Show = function(title, name, detail, file, fraction, hint, speeds)
  local made = Panel()
  if made == nil then return end
  if not Share.visible then
    Share.visible = true
    Share.since = Now()
  end
  Share.cancel = type(hint) == 'string' and hint ~= ''
  local part = tonumber(fraction) or 0
  if part < 0 then part = 0 end
  if part > 1 then part = 1 end
  for _, other in pairs(Share.panels) do
    if other ~= made then
      pcall(function() other.panel:setAlpha(0) end)
    end
  end
  pcall(function()
    made.title:setText(title or '')
    made.name:setText(name or '')
    made.detail:setText(detail or '')
    made.file:setText(file or '')
    made.hint:setText(hint or '')
    made.speeds:setText(speeds or '')
    made.bar:setLeftRight(false, false, -half, -half + 2 * half * part)
    made.panel:setAlpha(1)
  end)
end

Share.Hide = function()
  Share.visible = false
  Share.cancel = false
  for _, made in pairs(Share.panels) do
    pcall(function() made.panel:setAlpha(0) end)
  end
end
)LUA";
