if rawget(_G, "BO3CustomsMapTabs") ~= nil then
  return
end

local Tabs = {
  current = {},
  lists = {},
  menuWrapped = false,
  squareMenus = setmetatable({}, { __mode = "k" }),
  logs = {},
  logged = {},
}
rawset(_G, "BO3CustomsMapTabs", Tabs)

local function Log(text)
  if #Tabs.logs < 200 then
    Tabs.logs[#Tabs.logs + 1] = string.sub(tostring(text), 1, 160)
  end
end

local function LogOnce(key, text)
  if Tabs.logged[key] == nil then
    Tabs.logged[key] = true
    Log(text)
  end
end

local function Try(what, fn, ...)
  local ok, err = pcall(fn, ...)
  if not ok then
    LogOnce("error " .. what .. tostring(err), what .. " failed: " .. tostring(err))
  end
  return ok, err
end

local function IsElement(value)
  local kind = type(value)
  return kind == "userdata" or kind == "table"
end

local TabOrder = {
  { id = "standard", label = "STANDARD" },
  { id = "custom", label = "CUSTOM" },
}

local CustomsCategory = 10100

local Marquee = { gap = 4, hold = 1500, step = 170, poll = 400 }

local function Zombies()
  return Enum ~= nil and Enum.eModes ~= nil and Enum.eModes.MODE_ZOMBIES or 0
end

local function Multiplayer()
  return Enum ~= nil and Enum.eModes ~= nil and Enum.eModes.MODE_MULTIPLAYER or 1
end

local function SquareButton()
  return Enum.LUIButton.LUI_KEY_XBX_PSSQUARE
end

local function AllCustom()
  local list = rawget(_G, "BO3CustomMaps")
  if type(list) ~= "table" then
    return {}
  end
  return list
end

local function ModeOf(map)
  if map.mode == "zm" or map.mode == "mp" then
    return map.mode
  end
  if type(map.id) == "string" and string.sub(map.id, 1, 3) == "mp_" then
    return "mp"
  end
  return "zm"
end

local function CustomList(mode)
  local only = {}
  for _, map in ipairs(AllCustom()) do
    if type(map) == "table" and ModeOf(map) == mode then
      table.insert(only, map)
    end
  end
  return only
end

local function CustomById(id)
  if type(id) ~= "string" then
    return nil
  end
  for _, map in ipairs(AllCustom()) do
    if type(map) == "table" and map.id == id then
      return map
    end
  end
  return nil
end

local function TitleOf(map)
  if type(map.name) == "string" and map.name ~= "" then
    return map.name
  end
  return map.id
end

local function IsCustom(info)
  return type(info) == "table" and info.bo3_custom == true
end

local function Template(maps, mode)
  local preferred = mode == "mp" and maps.mp_sector or maps.zm_zod
  if type(preferred) == "table" then
    return preferred
  end
  local sessionMode = mode == "mp" and Multiplayer() or Zombies()
  for _, info in pairs(maps) do
    if type(info) == "table" and info.session_mode == sessionMode and not IsCustom(info) and info.isFreeRunMap ~= true then
      return info
    end
  end
  return nil
end

local function EnsureCustomEntries()
  local maps = CoD.mapsTable
  if type(maps) ~= "table" then
    return false
  end
  local templates = {}
  for index, map in ipairs(AllCustom()) do
    if type(map) == "table" and type(map.id) == "string" and maps[map.id] == nil then
      local mode = ModeOf(map)
      if templates[mode] == nil then
        templates[mode] = Template(maps, mode) or false
      end
      local template = templates[mode]
      if template then
        local info = {}
        for key, value in pairs(template) do
          info[key] = value
        end
        local title = map.name or map.id
        info.name = title
        info.mapName = title
        info.mapNameCaps = string.upper(title)
        info.mapDescription = map.desc or ""
        info.mapLocation = "CUSTOM MAP"
        info.previewImage = map.preview or map.loading or "$white"
        info.loadingImage = map.loading or map.preview or "$white"
        info.introMovie = map.movie
        info.dlc_pack = 0
        info.unique_id = 100000 + index
        info.bo3_custom = true
        maps[map.id] = info
      end
    end
  end
  return true
end

local function TitleSet()
  local set = {}
  for _, map in ipairs(AllCustom()) do
    if type(map) == "table" then
      local title = TitleOf(map)
      if type(title) == "string" and title ~= "" then
        set[title] = true
        set[string.upper(title)] = true
      end
    end
  end
  return set
end

local function Chars(text)
  local list = {}
  local index = 1
  local length = string.len(text)
  while index <= length do
    local lead = string.byte(text, index)
    local size = 1
    if lead >= 240 then
      size = 4
    elseif lead >= 224 then
      size = 3
    elseif lead >= 192 then
      size = 2
    end
    list[#list + 1] = string.sub(text, index, index + size - 1)
    index = index + size
  end
  return list
end

local MarkStart = "\21"
local MarkEnd = "\20"

local function SplitMarkers(text)
  local prefix = ""
  local suffix = ""
  local body = text
  if string.sub(body, 1, 1) == MarkStart then
    prefix = MarkStart
    body = string.sub(body, 2)
  end
  if string.sub(body, -1) == MarkEnd then
    suffix = MarkEnd
    body = string.sub(body, 1, -2)
  end
  return prefix, body, suffix
end

local function TextWidth(label)
  local ok, width = pcall(label.getTextWidth, label)
  if ok and type(width) == "number" then
    return width
  end
  return 0
end

local function InFocus(element)
  if type(element.isInFocus) ~= "function" then
    return true
  end
  local ok, focused = pcall(element.isInFocus, element)
  return not ok or focused == true
end

local function NoScale()
end

local function Quietly(fn, ...)
  local scale = rawget(_G, "ScaleWidgetToLabel")
  rawset(_G, "ScaleWidgetToLabel", NoScale)
  local ok, err = pcall(fn, ...)
  rawset(_G, "ScaleWidgetToLabel", scale)
  return ok, err
end

local function MarqueeWrite(state, text, keepWidth)
  state.writing = true
  if keepWidth then
    Quietly(state.set, state.label, text)
  else
    pcall(state.set, state.label, text)
  end
  if state.twin ~= nil then
    pcall(state.twinSet, state.twin, text)
  end
  state.writing = false
  state.shown = text
end

local function OneLine(label, room)
  if type(label.getTextHeightForWidth) ~= "function" then
    return true
  end
  local okRoom, inRoom = pcall(label.getTextHeightForWidth, label, room)
  local okWide, wide = pcall(label.getTextHeightForWidth, label, 4000)
  if okRoom and okWide and type(inRoom) == "number" and type(wide) == "number" and wide > 0 then
    return inRoom <= wide + 1
  end
  return true
end

local function MarqueeFits(state, text)
  state.writing = true
  Quietly(state.set, state.label, text)
  state.writing = false
  return TextWidth(state.label) <= state.room and OneLine(state.label, state.room)
end

local function Marked(state, body)
  return state.prefix .. body .. state.suffix
end

local function MarqueeFit(state, list, first, tail)
  local low = 1
  local high = #list - first + 1
  local best = 1
  while low <= high do
    local middle = math.floor((low + high) / 2)
    if MarqueeFits(state, Marked(state, table.concat(list, "", first, first + middle - 1) .. tail)) then
      best = middle
      low = middle + 1
    else
      high = middle - 1
    end
  end
  return best
end

local MarqueeStep

local function MarqueeSchedule(state, delay)
  local ticker = state.ticker
  if ticker == nil then
    ticker = LUI.UIElement.new()
    ticker:setLeftRight(true, false, 0, 1)
    ticker:setTopBottom(true, false, 0, 1)
    ticker:setAlpha(0)
    state.owner:addElement(ticker)
    ticker:registerEventHandler("transition_complete_bo3_marquee", function(element, event)
      if type(event) == "table" and event.interrupted then
        return
      end
      Try("marquee step", MarqueeStep, state)
    end)
    state.ticker = ticker
  end
  ticker:beginAnimation("bo3_marquee", delay, false, false, CoD.TweenType.Linear)
  ticker:setAlpha(0)
end

local function MarqueeWindow(state)
  local count = MarqueeFit(state, state.loop, state.position, "")
  return Marked(state, table.concat(state.loop, "", state.position, state.position + count - 1))
end

MarqueeStep = function(state)
  if state.full == nil then
    return
  end
  if not state.always and not InFocus(state.owner) then
    if state.scrolling then
      state.scrolling = false
      MarqueeWrite(state, state.idle)
    end
    MarqueeSchedule(state, Marquee.poll)
    return
  end
  if not state.scrolling then
    state.scrolling = true
    state.position = 1
    MarqueeWrite(state, MarqueeWindow(state))
    MarqueeSchedule(state, Marquee.hold)
    return
  end
  state.position = state.position + 1
  if state.position > state.cycle then
    state.position = 1
  end
  MarqueeWrite(state, MarqueeWindow(state), true)
  if state.position == 1 then
    MarqueeSchedule(state, Marquee.hold)
  else
    MarqueeSchedule(state, Marquee.step)
  end
end

local function MarqueeCheck(state, text)
  state.full = nil
  state.scrolling = false
  if type(text) ~= "string" or text == "" then
    return
  end
  local prefix, body, suffix = SplitMarkers(text)
  if not TitleSet()[body] then
    if string.len(body) > 24 then
      LogOnce("not custom " .. body, "marquee ignores '" .. body .. "'")
    end
    return
  end
  state.prefix = prefix
  state.suffix = suffix
  local fits = MarqueeFits(state, text)
  LogOnce("marquee " .. state.room .. body, "name '" .. body .. "' is " .. math.floor(TextWidth(state.label)) ..
    " wide, room " .. state.room .. (fits and ", fits" or ", scrolls"))
  if fits then
    return
  end
  local chars = Chars(body)
  if #chars < 2 then
    return
  end
  local loop = {}
  for index = 1, #chars do
    loop[#loop + 1] = chars[index]
  end
  for index = 1, Marquee.gap do
    loop[#loop + 1] = " "
  end
  for index = 1, #chars do
    loop[#loop + 1] = chars[index]
  end
  state.loop = loop
  state.cycle = #chars + Marquee.gap
  state.idle = Marked(state, table.concat(chars, "", 1, MarqueeFit(state, chars, 1, "...")) .. "...")
  state.full = text
  if state.always then
    state.scrolling = true
    state.position = 1
    MarqueeWrite(state, MarqueeWindow(state))
    MarqueeSchedule(state, Marquee.hold)
    return
  end
  MarqueeWrite(state, state.idle)
  MarqueeSchedule(state, Marquee.poll)
end

local function CurrentText(owner, label)
  if type(label.getText) == "function" then
    local ok, text = pcall(label.getText, label)
    if ok and type(text) == "string" and text ~= "" then
      return text
    end
  end
  if type(owner.getModel) ~= "function" then
    return nil
  end
  local ok, model = pcall(owner.getModel, owner)
  if not ok or model == nil then
    return nil
  end
  local okChild, child = pcall(Engine.GetModel, model, "displayText")
  if not okChild or child == nil then
    return nil
  end
  local okValue, value = pcall(Engine.GetModelValue, child)
  if not okValue or type(value) ~= "string" or value == "" then
    return nil
  end
  local okText, text = pcall(Engine.Localize, value)
  if okText and type(text) == "string" and text ~= "" then
    return text
  end
  return value
end

local function AttachMarquee(owner, label, twin, room, always)
  if not IsElement(owner) or not IsElement(label) or label.bo3Marquee ~= nil then
    return
  end
  if type(label.setText) ~= "function" or type(label.getTextWidth) ~= "function" then
    LogOnce("marquee methods", "marquee skipped: setText " .. type(label.setText) .. ", getTextWidth " ..
      type(label.getTextWidth))
    return
  end
  local state = { owner = owner, label = label, room = room, always = always == true, set = label.setText,
    prefix = "", suffix = "" }
  if IsElement(twin) and type(twin.setText) == "function" then
    state.twin = twin
    state.twinSet = twin.setText
  end
  label.bo3Marquee = state
  label.setText = function(self, text, ...)
    if state.writing then
      return state.set(self, text, ...)
    end
    local result = state.set(self, text, ...)
    if not Try("marquee check", MarqueeCheck, state, text) then
      state.full = nil
    end
    return result
  end
  if state.twin ~= nil then
    twin.setText = function(self, text, ...)
      if not state.writing and state.full ~= nil and text == state.full and state.shown ~= nil then
        return state.twinSet(self, state.shown, ...)
      end
      return state.twinSet(self, text, ...)
    end
  end
  local current = CurrentText(owner, label)
  if current ~= nil then
    Try("marquee check", MarqueeCheck, state, current)
  end
end

local ButtonRoom = 254
local TileRoom = 290
local Lack = { left = 288, right = 380, half = 8, every = 500 }
local AttachLack

local function WrapButton(class, name)
  if type(class) ~= "table" or type(class.new) ~= "function" then
    return false
  end
  if rawget(class, "bo3Marquee") ~= nil then
    return true
  end
  local new = class.new
  rawset(class, "bo3Marquee", true)
  class.new = function(menu, controller, ...)
    local element = new(menu, controller, ...)
    if IsElement(element) then
      Try("marquee attach", AttachMarquee, element, element.btnDisplayText, element.btnDisplayTextStroke, ButtonRoom, false)
      Try("lack attach", AttachLack, element)
    end
    return element
  end
  Log("hooked " .. name .. " buttons")
  return true
end

local ButtonModules = {
  { name = "List1ButtonLarge_ZM", module = "ui.uieditor.widgets.ZMInventory.ZMMapSelection.List1ButtonLarge_ZM" },
  { name = "List1Button_Playlist", module = "ui.uieditor.widgets.List1Button_Playlist" },
}

local function WrapButtons(load)
  for _, entry in ipairs(ButtonModules) do
    if load and rawget(CoD, entry.name) == nil and not entry.required then
      entry.required = true
      local ok, err = pcall(require, entry.module)
      if not ok then
        Log("require " .. entry.name .. " failed: " .. tostring(err))
      end
    end
    WrapButton(rawget(CoD, entry.name), entry.name)
  end
end

local function AttachButtons(element, depth)
  if not IsElement(element) or depth > 3 or type(element.getFirstChild) ~= "function" then
    return 0
  end
  local ok, child = pcall(element.getFirstChild, element)
  local count = 0
  local found = 0
  while ok and IsElement(child) and count < 64 do
    count = count + 1
    if IsElement(child.btnDisplayText) then
      found = found + 1
      if child.btnDisplayText.bo3Marquee == nil then
        Try("marquee attach", AttachMarquee, child, child.btnDisplayText, child.btnDisplayTextStroke, ButtonRoom, false)
      end
      Try("lack attach", AttachLack, child)
    else
      found = found + AttachButtons(child, depth + 1)
    end
    if type(child.getNextSibling) ~= "function" then
      break
    end
    ok, child = pcall(child.getNextSibling, child)
  end
  return found
end

local Description = { left = 469.38, right = 1169.34, top = 503.23, bottom = 675, line = 22, speed = 24, hold = 3000,
  stickSpeed = 260, deadzone = 0.25, resume = 3000 }
Description.width = Description.right - Description.left

local function Now()
  local ok, value = pcall(Engine.milliseconds)
  if ok and type(value) == "number" then
    return value
  end
  return 0
end

local function DescriptionAt(state)
  if state.animTo == nil or state.animDuration == nil or state.animDuration <= 0 then
    return state.offset or 0
  end
  local done = math.min(1, math.max(0, (Now() - state.animStart) / state.animDuration))
  return state.animFrom + (state.animTo - state.animFrom) * done
end

local function DescriptionAnimate(state, duration, offset)
  state.animation = state.generation
  state.animFrom = DescriptionAt(state)
  state.animTo = offset
  state.animStart = Now()
  state.animDuration = math.max(1, math.floor(duration))
  state.offset = offset
  state.text:beginAnimation("bo3_scroll", state.animDuration, false, false, CoD.TweenType.Linear)
  state.text:setTopBottom(true, false, -offset, Description.line - offset)
end

local function DescriptionMove(state, offset)
  state.offset = offset
  state.animTo = nil
  state.text:beginAnimation("bo3_manual", 1, false, false, CoD.TweenType.Linear)
  state.text:setTopBottom(true, false, -offset, Description.line - offset)
end

local function StickY(state)
  if state.stick == nil then
    local controller = Engine.GetPrimaryController()
    local stick = Engine.GetModel(Engine.GetModelForController(controller), "RightStick")
    state.stick = stick and Engine.GetModel(stick, "Y") or false
  end
  if not state.stick then
    return 0
  end
  local value = Engine.GetModelValue(state.stick)
  return type(value) == "number" and value or 0
end

local function DescriptionStick(state, event)
  if state.distance <= 1 then
    return
  end
  local y = StickY(state)
  local now = Now()
  if math.abs(y) > Description.deadzone then
    if not state.manual then
      state.manual = true
      state.offset = DescriptionAt(state)
      state.generation = state.generation + 1
    end
    local elapsed = type(event) == "table" and tonumber(event.timeElapsed) or 16
    local offset = state.offset - y * Description.stickSpeed * math.min(elapsed, 100) / 1000
    DescriptionMove(state, math.max(0, math.min(state.distance, offset)))
    state.touched = now
    return
  end
  if state.manual and now - (state.touched or 0) >= Description.resume then
    state.manual = false
    state.generation = state.generation + 1
    if state.offset >= state.distance - 1 then
      state.phase = "bottom"
      DescriptionAnimate(state, Description.hold, state.distance)
    else
      state.phase = "down"
      DescriptionAnimate(state, (state.distance - state.offset) / Description.speed * 1000, state.distance)
    end
  end
end

local function DescriptionReset(state)
  state.generation = state.generation + 1
  local text = state.text
  if type(text.completeAnimation) == "function" then
    pcall(text.completeAnimation, text)
  end
  state.offset = 0
  state.animTo = nil
  state.manual = false
  text:setTopBottom(true, false, 0, Description.line)
  local ok, height = pcall(text.getTextHeightForWidth, text, Description.width)
  local visible = Description.bottom - Description.top
  state.distance = 0
  if ok and type(height) == "number" then
    state.distance = math.max(0, height - visible + 6)
  end
  LogOnce("description " .. tostring(height), "description is " .. tostring(height) .. " tall, box " ..
    math.floor(visible) .. ", scrolls " .. math.floor(state.distance))
  state.phase = "top"
  if state.distance > 1 then
    DescriptionAnimate(state, Description.hold, 0)
  end
end

local function DescriptionStep(state)
  if state.animation ~= state.generation or state.distance <= 1 then
    return
  end
  if state.phase == "top" then
    state.phase = "down"
    DescriptionAnimate(state, state.distance / Description.speed * 1000, state.distance)
  elseif state.phase == "down" then
    state.phase = "bottom"
    DescriptionAnimate(state, Description.hold, state.distance)
  else
    state.phase = "top"
    state.text:setTopBottom(true, false, 0, Description.line)
    state.offset = 0
    state.animTo = nil
    DescriptionAnimate(state, Description.hold, 0)
  end
end

local function TuneDescription(menu)
  local brief = menu.MissionBriefText
  if not IsElement(brief) or brief.autoScrollStartDelay == nil then
    return false
  end
  brief.autoScrollStartDelay = 2500
  brief.autoScrollEndDelay = 2500
  brief.allowAutoScrolling = true
  brief.allowRightStickScrolling = true
  Log("description uses the stock scrolling box")
  return true
end

local function ScrollDescription(menu)
  local text = menu.MissionBriefText
  if not IsElement(text) then
    Log("description not set up: MissionBriefText is " .. type(text))
    return
  end
  if text.bo3Scroll ~= nil then
    return
  end
  if TuneDescription(menu) then
    return
  end
  if type(text.getTextHeightForWidth) ~= "function" or type(menu.removeElement) ~= "function" or type(text.setText) ~= "function" then
    Log("description cannot scroll: getTextHeightForWidth " .. type(text.getTextHeightForWidth) .. ", removeElement " .. type(menu.removeElement))
    return
  end
  local holder = LUI.UIElement.new()
  holder:setLeftRight(true, false, Description.left, Description.right)
  holder:setTopBottom(true, false, Description.top, Description.bottom)
  holder:setUseStencil(true)
  menu:removeElement(text)
  holder:addElement(text)
  menu:addElement(holder)
  text:setLeftRight(true, false, 0, Description.width)
  text:setTopBottom(true, false, 0, Description.line)
  menu.BO3DescriptionBox = holder
  local state = { text = text, generation = 0, animation = -1, distance = 0, offset = 0, phase = "top" }
  text.bo3Scroll = state
  local set = text.setText
  text.setText = function(self, value, ...)
    local result = set(self, value, ...)
    Try("description reset", DescriptionReset, state)
    return result
  end
  text:registerEventHandler("transition_complete_bo3_scroll", function(element, event)
    if type(event) == "table" and event.interrupted then
      return
    end
    Try("description step", DescriptionStep, state)
  end)
  if LUI.UITimer ~= nil and type(LUI.UITimer.newElementTimer) == "function" then
    holder:addElement(LUI.UITimer.newElementTimer(16, false, function(event)
      Try("description stick", DescriptionStick, state, event)
    end))
  end
  Log("description box set up")
  Try("description reset", DescriptionReset, state)
end

local function ItemMapId(element)
  if not IsElement(element) then
    return nil
  end
  local id = element.mapId
  if type(id) ~= "string" then
    id = element.mapName
  end
  local active = element.activeWidget
  if type(id) ~= "string" and IsElement(active) then
    id = active.mapId
    if type(id) ~= "string" then
      id = active.mapName
    end
  end
  if type(id) == "string" then
    return id
  end
  return nil
end

local function StockLacking(map)
  local lobby = rawget(_G, "BO3CustomsLobbyMaps")
  if type(lobby) ~= "table" or type(lobby.stockLacking) ~= "function" then
    return 0
  end
  local ok, count = pcall(lobby.stockLacking, map)
  if ok and type(count) == "number" then
    return count
  end
  return 0
end

local function LackCount(owner)
  local lobby = rawget(_G, "BO3CustomsLobbyMaps")
  if type(lobby) ~= "table" or type(lobby.ask) ~= "function" or type(lobby.hosting) ~= "function" or not lobby.hosting() then
    return 0
  end
  local id = ItemMapId(owner)
  if type(id) ~= "string" then
    return 0
  end
  if lobby.ask("stock:" .. id) == 1 then
    return StockLacking(id)
  end
  return math.floor(lobby.ask("lacking:" .. id) + 0.5)
end

local function LackUpdate(state)
  local count = LackCount(state.owner)
  if count == state.count then
    return
  end
  state.count = count
  if count > 0 then
    state.text:setText(tostring(count) .. " MISSING")
    state.text:setAlpha(1)
  else
    state.text:setAlpha(0)
  end
end

local function LackSchedule(state)
  state.ticker:beginAnimation("bo3_lack", Lack.every, false, false, CoD.TweenType.Linear)
  state.ticker:setAlpha(0)
end

AttachLack = function(owner)
  if not IsElement(owner) or owner.bo3Lack ~= nil or type(LUI.UIText) ~= "table" then
    return
  end
  local text = LUI.UIText.new()
  text:setLeftRight(true, false, Lack.left, Lack.right)
  text:setTopBottom(false, false, -Lack.half, Lack.half)
  text:setTTF("fonts/RefrigeratorDeluxe-Regular.ttf")
  if Enum ~= nil and Enum.LUIAlignment ~= nil and Enum.LUIAlignment.LUI_ALIGNMENT_LEFT ~= nil then
    text:setAlignment(Enum.LUIAlignment.LUI_ALIGNMENT_LEFT)
  end
  text:setRGB(1, 0.3, 0.25)
  text:setAlpha(0)
  text:setText("")
  owner:addElement(text)
  local ticker = LUI.UIElement.new()
  ticker:setLeftRight(true, false, 0, 1)
  ticker:setTopBottom(true, false, 0, 1)
  ticker:setAlpha(0)
  owner:addElement(ticker)
  local state = { owner = owner, text = text, ticker = ticker, count = 0 }
  owner.bo3Lack = state
  ticker:registerEventHandler("transition_complete_bo3_lack", function(element, event)
    Try("lack update", LackUpdate, state)
    LackSchedule(state)
  end)
  LackSchedule(state)
  Try("lack update", LackUpdate, state)
end

local function MapListOf(menu, kind)
  if not IsElement(menu) then
    return nil
  end
  if kind == "mp" then
    return menu.mapList
  end
  return menu.MapList
end

local function FocusedCustom(element, menu, kind, controller)
  local id = nil
  local from = nil
  if IsElement(element) and (type(element.mapId) == "string" or type(element.mapName) == "string") then
    id, from = ItemMapId(element), "item"
  end
  if id == nil then
    id, from = ItemMapId(MapListOf(menu, kind)), "list"
  end
  local watch = IsElement(menu) and Tabs.squareMenus[menu] or nil
  if id == nil and type(watch) == "table" and type(watch.focusedId) == "string" then
    id, from = watch.focusedId, "focus"
  end
  if id == nil and kind == "zm" and CoD.perController ~= nil then
    local perController = CoD.perController[controller or 0]
    if type(perController) == "table" and type(perController.zmLobbyEEMapRef) == "string" then
      id, from = perController.zmLobbyEEMapRef, "zmLobbyEEMapRef"
    end
  end
  LogOnce("focused " .. kind .. tostring(id) .. tostring(from),
    "focused " .. kind .. " map " .. tostring(id) .. " (from " .. tostring(from) .. ")")
  local map = CustomById(id)
  if map == nil then
    return nil
  end
  if kind == "mp" and type(IsSelfInState) == "function" then
    local ok, selecting = pcall(IsSelfInState, menu, "SelectingMap")
    if ok and selecting ~= true then
      return nil
    end
  end
  return map
end

local function ShowChoice(menu, controller, title, text, choices)
  local util = CoD.OverlayUtility
  if type(util) ~= "table" or type(util.CreateOverlay) ~= "function" or type(util.Overlays) ~= "table" then
    return false
  end
  if type(DataSources) ~= "table" or type(DataSourceHelpers) ~= "table" then
    return false
  end
  local listName = "BO3CustomsChoice_List"
  local overlay = {
    menuName = "SystemOverlay_Compact",
    title = title,
    description = text,
    listDatasource = function()
      DataSources[listName] = DataSourceHelpers.ListSetup(listName, function()
        local items = {}
        for _, choice in ipairs(choices) do
          table.insert(items, {
            models = { displayText = choice.label },
            properties = {
              selectIndex = choice.focus == true,
              action = function(self, element, actionController, param, owner)
                local target = owner
                if type(target) ~= "table" then
                  target = self
                end
                pcall(GoBack, target, actionController or controller)
                if choice.run ~= nil then
                  pcall(choice.run, actionController or controller)
                end
              end,
            },
          })
        end
        return items
      end, true)
      return listName
    end,
  }
  if type(util.OverlayTypes) == "table" and util.OverlayTypes.Warning ~= nil then
    overlay.categoryType = util.OverlayTypes.Warning
  end
  if util.GoBackPropertyName ~= nil and type(util.DefaultGoBack) == "function" then
    overlay[util.GoBackPropertyName] = util.DefaultGoBack
  end
  util.Overlays.BO3CustomsChoice = overlay
  local ok, err = pcall(util.CreateOverlay, controller, menu, "BO3CustomsChoice")
  if not ok then
    Log("confirm popup failed: " .. tostring(err))
  end
  return ok
end
Tabs.ShowChoice = ShowChoice

local function SquareLabel(map)
  if map == nil or Tabs.uninstalling ~= nil then
    return nil
  end
  if Tabs.confirm ~= nil and Tabs.confirm ~= map.id then
    Tabs.confirm = nil
  end
  if Tabs.confirm == map.id then
    return "Press again to uninstall"
  end
  return "Uninstall"
end

local function PromptState(name)
  local states = Enum ~= nil and Enum.LUIButtonPromptStates or nil
  if type(states) == "table" then
    return states[name]
  end
  return nil
end

local function ShowSquare(menu, watch, element)
  local map = FocusedCustom(element, menu, watch.kind, watch.controller)
  local label = SquareLabel(map)
  local button = SquareButton()
  if label ~= nil then
    CoD.Menu.SetButtonLabel(menu, button, label)
    local enable = PromptState("FLAG_ENABLE_PROMPTS")
    if enable ~= nil and type(menu.SetButtonPromptState) == "function" then
      menu:SetButtonPromptState(button, enable)
    end
    if watch.shown ~= label .. map.id then
      watch.shown = label .. map.id
      Log("square prompt '" .. label .. "' for " .. map.id)
    end
  elseif watch.shown ~= nil then
    watch.shown = nil
    CoD.Menu.UpdateButtonShownState(watch.element, menu, watch.controller, button)
  end
end

local function UpdateSquare(menu)
  local watch = Tabs.squareMenus[menu]
  if type(watch) == "table" then
    Try("square prompt", ShowSquare, menu, watch)
  end
end

local function StartUninstall(menu, controller, map, kind)
  Tabs.confirm = nil
  Tabs.pending = { menu = menu, controller = controller, kind = kind }
  Tabs.uninstalling = { id = map.id, name = TitleOf(map), kind = kind, controller = controller, menu = menu }
  Log("uninstalling " .. map.id)
  Engine.SetDvar("bo3customs_uninstall", map.id)
  local brief = IsElement(menu) and menu.MissionBriefText or nil
  if IsElement(brief) then
    local target = IsElement(brief.textBox) and brief.textBox or brief
    pcall(target.setText, target, "Uninstalling " .. TitleOf(map) .. "...")
  end
  UpdateSquare(menu)
end

local function Now()
  local clock = rawget(Engine, "milliseconds")
  if type(clock) ~= "function" then
    return nil
  end
  local ok, now = pcall(clock)
  if ok and type(now) == "number" then
    return now
  end
  return nil
end

local function AskUninstall(menu, controller, map, kind)
  if Tabs.uninstalling ~= nil then
    return
  end
  local now = Now()
  if now ~= nil and Tabs.askedAt ~= nil and now >= Tabs.askedAt and now - Tabs.askedAt < 400 then
    return
  end
  Tabs.askedAt = now
  local shown = ShowChoice(menu, controller, "UNINSTALL MAP",
    "Delete " .. TitleOf(map) .. " (" .. map.id .. ") from this console? Its files are removed and it leaves the map list. This cannot be undone.",
    {
      { label = "Uninstall", run = function(choiceController) StartUninstall(menu, choiceController, map, kind) end },
      { label = "Cancel", focus = true },
    })
  Log("uninstall asked for " .. map.id .. (shown and " (popup)" or " (press again)"))
  if shown then
    return
  end
  if Tabs.confirm == map.id then
    StartUninstall(menu, controller, map, kind)
  else
    Tabs.confirm = map.id
  end
  UpdateSquare(menu)
end

local function SquareFunctions(kind, stockAction, stockCondition)
  local button = SquareButton()
  local action = function(element, menu, controller, ...)
    local map = FocusedCustom(element, menu, kind, controller)
    if map ~= nil then
      AskUninstall(menu, controller, map, kind)
      return true
    end
    if type(stockAction) == "function" then
      return stockAction(element, menu, controller, ...)
    end
    return false
  end
  local condition = function(element, menu, controller, ...)
    local label = SquareLabel(FocusedCustom(element, menu, kind, controller))
    if label ~= nil then
      CoD.Menu.SetButtonLabel(menu, button, label)
      return true
    end
    if type(stockCondition) == "function" then
      return stockCondition(element, menu, controller, ...)
    end
    return false
  end
  return action, condition
end

local function HookListFocus(menu, list, watch)
  if not IsElement(list) or list.bo3FocusHook ~= nil or type(list.registerEventHandler) ~= "function" then
    return
  end
  local handlers = list.m_eventHandlers
  if type(handlers) ~= "table" then
    LogOnce("no handlers " .. watch.kind, watch.kind .. " list has no readable event handlers")
    return
  end
  local stock = handlers.list_item_gain_focus
  if stock == nil and watch.kind == "zm" then
    LogOnce("no focus handler", "zm list has no list_item_gain_focus handler")
    return
  end
  list.bo3FocusHook = true
  list:registerEventHandler("list_item_gain_focus", function(element, event)
    local result = nil
    if type(stock) == "function" then
      result = stock(element, event)
    end
    local id = ItemMapId(element)
    if id ~= nil then
      watch.focusedId = id
    end
    Try("square prompt", ShowSquare, menu, watch, element)
    return result
  end)
  Log("hooked focus on the " .. watch.kind .. " map list")
end

local function WatchSquare(menu, kind, element, controller)
  if not IsElement(menu) then
    Log("not watching the " .. kind .. " map menu: it is " .. type(menu))
    return
  end
  local watch = Tabs.squareMenus[menu]
  if watch == nil then
    watch = { kind = kind, element = element or menu, controller = controller }
    Tabs.squareMenus[menu] = watch
    pcall(LUI.OverrideFunction_CallOriginalSecond, menu, "close", function()
      Tabs.squareMenus[menu] = nil
    end)
    Log("watching the " .. kind .. " map menu")
  end
  Try("focus hook", HookListFocus, menu, MapListOf(menu, kind), watch)
  UpdateSquare(menu)
end

local function RefreshSquares()
  for menu, watch in pairs(Tabs.squareMenus) do
    Try("square prompt", ShowSquare, menu, watch)
    AttachButtons(MapListOf(menu, watch.kind), 0)
  end
end

local function SquareDown(model)
  local flags = Enum ~= nil and Enum.LUIButtonFlags or nil
  local bits = CoD.BitUtility
  if model == nil or type(flags) ~= "table" or flags.FLAG_DOWN == nil or type(bits) ~= "table" or
    type(bits.IsBitwiseAndNonZero) ~= "function" then
    return true
  end
  local ok, value = pcall(Engine.GetModelValue, model)
  if not ok or type(value) ~= "number" then
    return true
  end
  local okBit, down = pcall(bits.IsBitwiseAndNonZero, value, flags.FLAG_DOWN)
  return not okBit or down == true
end

local function SquarePress(menu, controller, model)
  local watch = Tabs.squareMenus[menu]
  if watch == nil or menu.occludedBy ~= nil or not SquareDown(model) then
    return false
  end
  local map = FocusedCustom(nil, menu, watch.kind, controller or watch.controller)
  if map == nil then
    return false
  end
  AskUninstall(menu, controller or watch.controller, map, watch.kind)
  return true
end

local function WrapButtonPress()
  local class = CoD.Menu
  if Tabs.pressWrapped or type(class) ~= "table" or type(class.HandleButtonPress) ~= "function" then
    return Tabs.pressWrapped == true
  end
  local handle = class.HandleButtonPress
  Tabs.pressWrapped = true
  class.HandleButtonPress = function(menu, controller, button, model, ...)
    if button == SquareButton() and IsElement(menu) and Tabs.squareMenus[menu] ~= nil then
      local ok, used = Try("square press", SquarePress, menu, controller, model)
      if ok and used then
        if model ~= nil then
          pcall(Engine.SetModelValue, model, 0)
        end
        return
      end
    end
    return handle(menu, controller, button, model, ...)
  end
  return true
end

local function AddUninstallButton(menu, controller, kind)
  if menu == nil or menu.BO3Uninstall ~= nil or type(menu.AddButtonCallbackFunction) ~= "function" then
    return
  end
  menu.BO3Uninstall = true
  local action, condition = SquareFunctions(kind, nil, nil)
  menu:AddButtonCallbackFunction(menu, controller, SquareButton(), nil, action, condition, false)
end

local function TabOf(info)
  if IsCustom(info) then
    return "custom"
  end
  return "standard"
end

local function TabTable(maps, tab)
  local only = {}
  for id, info in pairs(maps) do
    if type(info) == "table" and info.session_mode == Zombies() and TabOf(info) == tab then
      only[id] = info
    end
  end
  return only
end

local function Current(controller)
  return Tabs.current[controller or 0] or "standard"
end

local function StartingTab(controller)
  local mapName = Engine.DvarString(nil, "ui_mapname")
  local info = type(CoD.mapsTable) == "table" and CoD.mapsTable[mapName] or nil
  if type(info) == "table" and info.session_mode == Zombies() then
    return TabOf(info)
  end
  return Current(controller)
end

DataSources.BO3CustomsMapTabs = DataSourceHelpers.ListSetup("BO3CustomsMapTabs", function(controller)
  local list = {}
  local current = Current(controller)
  table.insert(list, {
    models = { tabIcon = CoD.buttonStrings.shoulderl },
    properties = { m_mouseDisabled = true },
  })
  for _, tab in ipairs(TabOrder) do
    table.insert(list, {
      models = { tabName = tab.label, tabIcon = "", bo3Tab = tab.id },
      properties = { tabId = tab.id, selectIndex = tab.id == current },
    })
  end
  table.insert(list, {
    models = { tabIcon = CoD.buttonStrings.shoulderr },
    properties = { m_mouseDisabled = true },
  })
  return list
end, true)

local function Choosing(controller)
  local perController = CoD.perController ~= nil and CoD.perController[controller] or nil
  return perController ~= nil and perController.choosingZMPlaylist == true
end

local ZMMenuFile = "ui/uieditor/menus/zm/zmmapselection.lua"

local function ModOwns(file)
  local files = rawget(_G, "BO3CustomsModFiles")
  return type(files) == "table" and files[file] ~= nil
end

local function StockMaps()
  local stock = {}
  for id, info in pairs(CoD.mapsTable) do
    if not IsCustom(info) then
      stock[id] = info
    end
  end
  return stock
end

local function BuildWith(maps, prepare, ...)
  local full = CoD.mapsTable
  CoD.mapsTable = maps
  local ok, a, b, c = pcall(prepare, ...)
  CoD.mapsTable = full
  if not ok then
    error(a)
  end
  return a, b, c
end

local function WrapMapsList()
  local source = DataSources.ZMMapsList
  if type(source) ~= "table" or type(source.prepare) ~= "function" or source.bo3Wrapped then
    return false
  end
  local prepare = source.prepare
  source.bo3Wrapped = true
  source.prepare = function(controller, ...)
    if type(CoD.mapsTable) == "table" and ModOwns(ZMMenuFile) then
      LogOnce("zm owned", "a loaded mod has its own zm map menu; custom maps are left to it")
      return BuildWith(StockMaps(), prepare, controller, ...)
    end
    if type(controller) ~= "number" or Choosing(controller) or type(CoD.mapsTable) ~= "table" then
      return prepare(controller, ...)
    end
    EnsureCustomEntries()
    return BuildWith(TabTable(CoD.mapsTable, Current(controller)), prepare, controller, ...)
  end
  return true
end

local function WrapCombatRecord()
  local source = DataSources.CombatRecordZMMapsList
  if type(source) ~= "table" or type(source.prepare) ~= "function" or source.bo3Wrapped then
    return
  end
  local prepare = source.prepare
  source.bo3Wrapped = true
  source.prepare = function(...)
    if type(CoD.mapsTable) ~= "table" then
      return prepare(...)
    end
    local stock = {}
    for id, info in pairs(CoD.mapsTable) do
      if not IsCustom(info) then
        stock[id] = info
      end
    end
    return BuildWith(stock, prepare, ...)
  end
end

local function AddTabs(menu, controller)
  if menu.BO3TabBar ~= nil or menu.MapList == nil or Choosing(controller) then
    return
  end
  local count = #CustomList("zm")
  if count == 0 then
    Tabs.current[controller] = "standard"
    return
  end
  if CoD.FE_TabBar == nil then
    pcall(require, "ui.uieditor.widgets.Lobby.Common.FE_TabBar")
  end
  if CoD.FE_TabBar == nil then
    return
  end
  EnsureCustomEntries()
  Tabs.current[controller] = StartingTab(controller)

  local bar = CoD.FE_TabBar.new(menu, controller)
  bar:setLeftRight(true, false, 0, 2497)
  bar:setTopBottom(true, false, 92, 133)
  bar.Tabs.grid:setHorizontalCount(5)
  bar.Tabs.grid:setDataSource("BO3CustomsMapTabs")
  menu:addElement(bar)
  menu.BO3TabBar = bar

  menu.MapList:setTopBottom(true, false, 142, 548)

  local watcher = LUI.UIElement.new()
  menu:addElement(watcher)
  watcher:linkToElementModel(bar.Tabs.grid, "bo3Tab", true, function(model)
    local tab = Engine.GetModelValue(model)
    if type(tab) ~= "string" or tab == "" or tab == Current(controller) then
      return
    end
    Tabs.current[controller] = tab
    menu.MapList:updateDataSource()
  end)

  menu.MapList:updateDataSource()
end

local function Refresh(menu, controller, kind)
  Tabs.pending = { menu = menu, controller = controller, kind = kind }
  Engine.SetDvar("bo3customs_refresh", 1)
end

local function RelinkPrompts(menu, controller)
  if menu.buttonModel == nil then
    return
  end
  for _, name in ipairs({ "MenuFrame", "frame" }) do
    local frame = menu[name]
    if frame ~= nil and type(frame.setModel) == "function" then
      frame:setModel(menu.buttonModel, controller)
    end
  end
end

local function AddRefreshButton(menu, controller, kind, relink)
  if menu == nil or menu.BO3Refresh ~= nil or type(menu.AddButtonCallbackFunction) ~= "function" then
    return
  end
  local button = Enum.LUIButton.LUI_KEY_XBY_PSTRIANGLE
  menu.BO3Refresh = true
  menu:AddButtonCallbackFunction(menu, controller, button, nil,
    function(element, owner, pressedBy)
      Refresh(menu, pressedBy or controller, kind)
      return true
    end,
    function(element, owner)
      CoD.Menu.SetButtonLabel(owner, button, "MENU_REFRESH")
      return true
    end,
    false)
  if relink then
    pcall(RelinkPrompts, menu, controller)
    pcall(CoD.Menu.UpdateButtonShownState, menu, menu, controller, button)
  end
end

local function CreateWithRefresh(create, controller, kind, ...)
  local class = CoD.Menu
  local add = type(class) == "table" and class.AddButtonCallbackFunction or nil
  if type(add) ~= "function" then
    return create(controller, ...)
  end
  local target = nil
  local square = false
  local count = 0
  local used = {}
  class.AddButtonCallbackFunction = function(menu, element, buttonController, button, keyShortcut, action, condition, ...)
    count = count + 1
    if button ~= nil then
      used[button] = true
    end
    if kind == "zm" and not square and button == SquareButton() then
      square = true
      action, condition = SquareFunctions(kind, action, condition)
      if IsElement(menu) then
        menu.BO3SquareElement = element
      end
    end
    local a, b, c = add(menu, element, buttonController, button, keyShortcut, action, condition, ...)
    if target == nil and menu ~= nil and kind ~= "mp" then
      target = menu
      Try("refresh button", AddRefreshButton, menu, controller, kind, false)
    end
    return a, b, c
  end
  local ok, menu = pcall(create, controller, ...)
  class.AddButtonCallbackFunction = add
  if not ok then
    local text = tostring(menu)
    Log(kind .. " map menu failed to build with the plugin's button hook; building it again without the hook")
    for part in string.gmatch(text, "[^\n]+") do
      Log("  " .. part)
    end
    return create(controller, ...), {}
  end
  LogOnce("create " .. kind .. count .. tostring(square), kind .. " map menu built: " .. count ..
    " button callbacks seen, stock square " .. (square and "wrapped" or "not seen"))
  return menu, used
end

local function HasCustoms(sessionMode)
  if type(CoD.mapsTable) ~= "table" then
    return false
  end
  for _, info in pairs(CoD.mapsTable) do
    if IsCustom(info) and info.session_mode == sessionMode then
      return true
    end
  end
  return false
end

local function RefreshZombies(menu, controller)
  if menu.BO3TabBar == nil then
    AddTabs(menu, controller)
    return
  end
  if #CustomList("zm") == 0 then
    Tabs.current[controller] = "standard"
  end
  menu.BO3TabBar.Tabs.grid:updateDataSource()
  menu.MapList:updateDataSource()
end

local function RefreshMultiplayer(menu, controller)
  local perController = CoD.perController ~= nil and CoD.perController[controller] or nil
  if type(perController) == "table" and perController.mapCategory == CustomsCategory and
    not HasCustoms(CoD.gameModeEnum) then
    perController.mapCategory = CoD.CONTENT_ORIGINAL_MAP_INDEX or 0
  end
  local lists = { Tabs.lists.ChangeMapCategories, menu.mapCategoriesList, Tabs.lists.ChangeMapMaps, menu.mapList }
  local done = {}
  for index = 1, 4 do
    local list = lists[index]
    if list ~= nil and not done[list] and type(list.updateDataSource) == "function" then
      done[list] = true
      pcall(list.updateDataSource, list)
    end
  end
end

function Tabs.Refreshed()
  local pending = Tabs.pending
  Tabs.pending = nil
  if type(Engine.GetGDTMapsTable) == "function" then
    local ok, maps = pcall(Engine.GetGDTMapsTable)
    if ok and type(maps) == "table" then
      CoD.mapsTable = maps
    end
  end
  EnsureCustomEntries()
  if type(pending) ~= "table" or pending.menu == nil then
    return
  end
  if pending.kind == "zm" then
    pcall(RefreshZombies, pending.menu, pending.controller)
  else
    pcall(RefreshMultiplayer, pending.menu, pending.controller)
  end
  UpdateSquare(pending.menu)
end

function Tabs.Uninstalled(id, removed, message)
  local job = Tabs.uninstalling
  Tabs.uninstalling = nil
  Tabs.confirm = nil
  if type(job) ~= "table" or job.id ~= id then
    job = { id = id, name = id, kind = string.sub(tostring(id), 1, 3) == "mp_" and "mp" or "zm", controller = 0 }
  end
  if removed then
    if Engine.DvarString(nil, "ui_mapname") == id and type(SetMap) == "function" then
      pcall(SetMap, job.controller or 0, job.kind == "mp" and "mp_sector" or "zm_zod")
    end
    return
  end
  if job.menu ~= nil then
    ShowChoice(job.menu, job.controller or 0, "UNINSTALL FAILED",
      job.name .. " could not be uninstalled: " .. tostring(message) .. ".", { { label = "OK", focus = true } })
  end
end

local function WrapMenu()
  if Tabs.menuWrapped or LUI.createMenu == nil or type(LUI.createMenu.ZMMapSelection) ~= "function" then
    return false
  end
  local create = LUI.createMenu.ZMMapSelection
  Tabs.menuWrapped = true
  LUI.createMenu.ZMMapSelection = function(controller, ...)
    if ModOwns(ZMMenuFile) then
      return create(controller, ...)
    end
    local choosing = Choosing(controller)
    Try("button hooks", WrapButtons, false)
    local menu = nil
    if choosing then
      menu = create(controller, ...)
    else
      menu = CreateWithRefresh(create, controller, "zm", ...)
    end
    if menu ~= nil then
      Try("tabs", AddTabs, menu, controller)
      if not choosing then
        Try("refresh button", AddRefreshButton, menu, controller, "zm", true)
        Try("description", ScrollDescription, menu)
        if IsElement(menu.ZMMissionTile) then
          Try("tile marquee", AttachMarquee, menu.ZMMissionTile, menu.ZMMissionTile.MapName, nil, TileRoom, true)
        end
        Log("zm map menu has " .. AttachButtons(menu.MapList, 0) .. " list buttons")
        Try("square watch", WatchSquare, menu, "zm", menu.BO3SquareElement or menu.MapList, controller)
      end
    end
    return menu
  end
  return true
end

local function WrapChangeMap()
  if Tabs.changeMapWrapped or LUI.createMenu == nil or type(LUI.createMenu.ChangeMap) ~= "function" then
    return false
  end
  local create = LUI.createMenu.ChangeMap
  Tabs.changeMapWrapped = true
  LUI.createMenu.ChangeMap = function(controller, ...)
    local campaign = CoD.isCampaign == true
    Try("button hooks", WrapButtons, false)
    local menu = nil
    local used = {}
    if campaign then
      menu = create(controller, ...)
    else
      menu, used = CreateWithRefresh(create, controller, "mp", ...)
      used = used or {}
    end
    if menu ~= nil and not campaign then
      if not used[SquareButton()] then
        Try("uninstall button", AddUninstallButton, menu, controller, "mp")
      else
        Log("mp map menu has its own square button; the uninstall button stays off")
      end
      if not used[Enum.LUIButton.LUI_KEY_XBY_PSTRIANGLE] then
        Try("refresh button", AddRefreshButton, menu, controller, "mp", true)
      else
        Log("mp map menu has its own triangle button; the refresh button stays off")
      end
      Log("mp map menu has " .. AttachButtons(menu.mapList, 0) .. " list buttons")
      if not used[SquareButton()] then
        Try("square watch", WatchSquare, menu, "mp", menu, controller)
      end
    end
    return menu
  end
  return true
end

local function AddCustomsCategory(items)
  local maps = CoD.mapsTable
  if type(items) ~= "table" or type(maps) ~= "table" or CoD.isCampaign == true then
    return
  end
  EnsureCustomEntries()
  local count = 0
  for _, info in pairs(maps) do
    if IsCustom(info) and info.session_mode == CoD.gameModeEnum then
      count = count + 1
    end
  end
  if count == 0 then
    return
  end
  local current = maps[Engine.DvarString(nil, "ui_mapname")]
  local onCustom = IsCustom(current) and current.session_mode == CoD.gameModeEnum
  if onCustom then
    for _, item in ipairs(items) do
      if type(item) == "table" and type(item.properties) == "table" then
        item.properties.selectIndex = false
      end
    end
  end
  table.insert(items, {
    models = {
      text = "CUSTOMS",
      buttonText = "CUSTOMS",
      image = "playlist_map",
      description = "Choose from the custom maps installed on this console.",
    },
    properties = { category = CustomsCategory, selectIndex = onCustom },
  })
end

local function PrepareCategories(prepare, controller, list)
  local items = prepare(controller, list)
  pcall(AddCustomsCategory, items)
  return items
end

local function PrepareMaps(prepare, controller, list)
  local maps = CoD.mapsTable
  local perController = CoD.perController ~= nil and CoD.perController[controller] or nil
  if type(maps) ~= "table" or type(perController) ~= "table" then
    return prepare(controller, list)
  end
  EnsureCustomEntries()
  local customs = perController.mapCategory == CustomsCategory
  local only = {}
  for id, info in pairs(maps) do
    if IsCustom(info) == customs then
      only[id] = info
    end
  end
  CoD.mapsTable = only
  if customs then
    perController.mapCategory = 0
  end
  local ok, result = pcall(prepare, controller, list)
  CoD.mapsTable = maps
  if customs then
    perController.mapCategory = CustomsCategory
  end
  if not ok then
    error(result)
  end
  return result
end

local function PrepareZMMaps(prepare, controller, list)
  if type(CoD.mapsTable) ~= "table" or not ModOwns(ZMMenuFile) then
    return prepare(controller, list)
  end
  LogOnce("zm owned", "a loaded mod has its own zm map menu; custom maps are left to it")
  return BuildWith(StockMaps(), prepare, controller, list)
end

local function RecoveryZMMaps(controller)
  -- Use the stock ZMMapsList row contract when a mod's list builder fails.
  -- Only engine-valid maps are selectable; this does not unlock missing DLC.
  local maps = type(CoD.mapsTable) == "table" and CoD.mapsTable or {}
  local ordered = {}
  for id, info in pairs(maps) do
    if type(id) == "string" and type(info) == "table" and info.session_mode == Zombies() and info.dlc_pack ~= -1 then
      ordered[#ordered + 1] = { id = id, info = info }
    end
  end
  table.sort(ordered, function(a, b)
    local left, right = tonumber(a.info.unique_id) or 0, tonumber(b.info.unique_id) or 0
    return left < right or (left == right and a.id < b.id)
  end)
  local rows = {}
  local function text(value, fallback)
    return type(value) == "string" and value or fallback
  end
  for _, entry in ipairs(ordered) do
    local ok, valid = false, false
    if type(Engine.IsMapValid) == "function" then
      ok, valid = pcall(Engine.IsMapValid, entry.id)
    end
    if ok and valid then
      local info = entry.info
      local title = text(info.mapNameCaps, text(info.mapName, entry.id))
      if type(Engine.Localize) == "function" then
        local localized, value = pcall(Engine.Localize, title)
        if localized and type(value) == "string" then title = value end
      end
      rows[#rows + 1] = {
        models = {
          displayText = title, Image = text(info.previewImage, "playlist_standard"),
          mapName = text(info.mapName, entry.id), mapLocation = text(info.mapLocation, ""),
          mapDescription = text(info.mapDescription, ""), playingCount = "", dlcIndex = tonumber(info.dlc_pack) or 0,
        },
        properties = { mapId = entry.id, purchasable = false },
      }
    end
    if #rows >= 512 then break end
  end
  return rows
end

local ListPrepares = {
  ChangeMapCategories = PrepareCategories,
  ChangeMapMaps = PrepareMaps,
  ZMMapsList = PrepareZMMaps,
}

local function WrapListPrepare()
  if Tabs.listPrepareWrapped then
    return true
  end
  local prepareList = rawget(_G, "ListHelper_Prepare")
  if type(prepareList) ~= "function" then
    return false
  end
  Tabs.listPrepareWrapped = true
  rawset(_G, "ListHelper_Prepare", function(list, controller, name, prepare, ...)
    local wrapper = name ~= nil and ListPrepares[name] or nil
    if wrapper ~= nil and type(prepare) == "function" then
      Tabs.lists[name] = list
      local stock = prepare
      prepare = function(prepareController, listElement)
        if name ~= "ZMMapsList" or Choosing(prepareController) then
          return wrapper(stock, prepareController, listElement)
        end
        local ok, items = pcall(wrapper, stock, prepareController, listElement)
        if ok and type(items) == "table" then
          return items
        end
        LogOnce("zm list recovery", "zm map list failed; using validated stock rows: " .. tostring(items))
        return RecoveryZMMaps(prepareController)
      end
    end
    return prepareList(list, controller, name, prepare, ...)
  end)
  return true
end

local function Install()
  WrapListPrepare()
  WrapMapsList()
  WrapCombatRecord()
  WrapMenu()
  WrapChangeMap()
  Try("press hook", WrapButtonPress)
  Try("button hooks", WrapButtons, true)
  EnsureCustomEntries()
  local lists = type(DataSources.ZMMapsList) == "table" and DataSources.ZMMapsList.bo3Wrapped == true
  local status = "zm menu " .. tostring(Tabs.menuWrapped) .. ", mp menu " .. tostring(Tabs.changeMapWrapped == true) ..
    ", map lists " .. tostring(lists) .. ", list prepare " .. tostring(Tabs.listPrepareWrapped == true) ..
    ", presses " .. tostring(Tabs.pressWrapped == true) ..
    ", zm buttons " .. tostring(type(CoD.List1ButtonLarge_ZM) == "table" and rawget(CoD.List1ButtonLarge_ZM, "bo3Marquee") ~= nil) ..
    ", mp buttons " .. tostring(type(CoD.List1Button_Playlist) == "table" and rawget(CoD.List1Button_Playlist, "bo3Marquee") ~= nil)
  LogOnce("install " .. status, "install: " .. status)
  return Tabs.listPrepareWrapped == true and Tabs.menuWrapped and Tabs.changeMapWrapped == true and lists and
    Tabs.pressWrapped == true
end

local function UnreadLogs()
  local logs = Tabs.logs
  if Tabs.bridge or #logs == 0 then
    return nil
  end
  if Tabs.mark == nil or logs[1] ~= Tabs.mark or #logs < Tabs.markCount then
    if Tabs.mark ~= nil then
      Tabs.bridge = true
    end
    return nil
  end
  local take = {}
  local size = 0
  while #logs > 0 and #take < 12 do
    local line = string.gsub(tostring(logs[1]), "%c", " ")
    if #take > 0 and size + string.len(line) > 900 then
      break
    end
    take[#take + 1] = line
    size = size + string.len(line) + 3
    table.remove(logs, 1)
  end
  return "[MapSelect] " .. table.concat(take, " | ")
end

local function MarkLogs()
  Tabs.mark = Tabs.logs[1]
  Tabs.markCount = #Tabs.logs
end

Tabs.installed = Install()

local tick = rawget(_G, "BO3CustomsTick")
rawset(_G, "BO3CustomsTick", function()
  local unread = UnreadLogs()
  if not Tabs.installed and Install() then
    Tabs.installed = true
  end
  Try("button hooks", WrapButtons, false)
  Try("square refresh", RefreshSquares)
  if tick ~= nil then
    tick()
  end
  MarkLogs()
  if unread ~= nil then
    error(unread, 0)
  end
end)
