if rawget(_G, "BO3CustomsModsUI") ~= nil then
  return
end

local cod = rawget(_G, "CoD")
local engine = rawget(_G, "Engine")
local lobbyMenus = type(cod) == "table" and rawget(cod, "LobbyMenus") or nil
local stockAdd = type(lobbyMenus) == "table" and rawget(lobbyMenus, "AddButtonsForTarget") or nil

if type(stockAdd) ~= "function" or engine == nil then
  return
end

local UI = { logged = {}, pageSize = 6, rows = {} }
rawset(_G, "BO3CustomsModsUI", UI)

local ButtonId = "btnModsLoad"
local ModeIds = { btnCP = true, btnMP = true, btnZM = true, btnCPZM = true, btnBonusModes = true }

local function Log(text)
  local tabs = rawget(_G, "BO3CustomsMapTabs")
  if type(tabs) == "table" and type(tabs.logs) == "table" and #tabs.logs < 200 then
    tabs.logs[#tabs.logs + 1] = string.sub("mods: " .. tostring(text), 1, 160)
  end
end

local function LogOnce(key, text)
  if UI.logged[key] == nil then
    UI.logged[key] = true
    Log(text)
  end
end

local function IsElement(value)
  local kind = type(value)
  return kind == "userdata" or kind == "table"
end

local function Mods()
  local list = rawget(_G, "BO3CustomMods")
  if type(list) ~= "table" then
    return {}
  end
  return list
end

local function ChosenList()
  local session = rawget(_G, "BO3CustomsSessionMods")
  if type(session) == "table" then
    return session
  end
  local list = rawget(_G, "BO3CustomsChosenMods")
  if type(list) == "table" then
    return list
  end
  local name = rawget(_G, "BO3CustomsChosenMod")
  if type(name) == "string" and name ~= "" then
    return { name }
  end
  return {}
end

local function ActiveList()
  local list = rawget(_G, "BO3CustomsActiveMods")
  if type(list) == "table" then
    return list
  end
  local mod = rawget(_G, "BO3CustomsActiveMod")
  if type(mod) == "table" then
    return { mod }
  end
  return {}
end

local function Chosen()
  return ChosenList()[1] or ""
end

local function Active()
  return ActiveList()[1]
end

local function Same(a, b)
  return type(a) == "string" and type(b) == "string" and a ~= "" and string.lower(a) == string.lower(b)
end

local function Position(name)
  for index, chosen in ipairs(ChosenList()) do
    if Same(chosen, name) then
      return index
    end
  end
  return nil
end

local function ActiveEntry(name)
  for _, mod in ipairs(ActiveList()) do
    if type(mod) == "table" and Same(mod.name, name) then
      return mod
    end
  end
  return nil
end

local function LoadingList(active)
  local list = {}
  for _, mod in ipairs(active) do
    if type(mod) == "table" and mod.loading == true then
      table.insert(list, mod)
    end
  end
  return list
end

local function FindMod(name)
  for _, mod in ipairs(Mods()) do
    if type(mod) == "table" and Same(mod.name, name) then
      return mod
    end
  end
  return nil
end

local function TitleOf(mod)
  if type(mod) == "table" and type(mod.title) == "string" and mod.title ~= "" then
    return mod.title
  end
  if type(mod) == "table" and type(mod.name) == "string" then
    return mod.name
  end
  return "?"
end

local function Short(text, size)
  if string.len(text) <= size then
    return text
  end
  local cut = size - 3
  while cut > 1 do
    local byte = string.byte(text, cut + 1)
    if byte == nil or byte < 128 or byte >= 192 then
      break
    end
    cut = cut - 1
  end
  return string.sub(text, 1, cut) .. "..."
end

local function Titles(list)
  local out = {}
  for _, item in ipairs(list) do
    local mod = type(item) == "table" and item or FindMod(item) or { name = tostring(item) }
    table.insert(out, Short(TitleOf(mod), 32))
  end
  return table.concat(out, ", ")
end

local function NameOf(item)
  if type(item) == "table" then
    return item.name
  end
  return item
end

local function SameNames(a, b)
  if #a ~= #b then
    return false
  end
  for index = 1, #a do
    if not Same(NameOf(a[index]), NameOf(b[index])) then
      return false
    end
  end
  return true
end

local function ModLabel(mod)
  local text = Short(TitleOf(mod), 36)
  local position = Position(mod.name)
  if position ~= nil then
    text = "[" .. position .. "] " .. text
  end
  local entry = ActiveEntry(mod.name)
  if entry ~= nil and entry.loading == true then
    return text .. "  (loading)"
  end
  if entry ~= nil then
    return text .. "  (loaded)"
  end
  return text
end

local function Describe()
  local lines = {}
  local active = ActiveList()
  if #active > 0 then
    table.insert(lines, "Loaded: " .. Titles(active))
  else
    table.insert(lines, "No mod is loaded.")
  end
  if #Mods() == 0 then
    table.insert(lines, "No mods are installed. Copy a mod folder made by FF Porter into BO3-Customs/mods/ and restart the game.")
  else
    table.insert(lines, "Tick the mods to load. The first ticked mod wins when two change the same thing.")
  end
  return table.concat(lines, "\n")
end

local function Controller(controller)
  if type(controller) == "number" then
    return controller
  end
  local primary = rawget(engine, "GetPrimaryController")
  if type(primary) == "function" then
    local ok, value = pcall(primary)
    if ok and type(value) == "number" then
      return value
    end
  end
  return 0
end

local function StatusText()
  local active = ActiveList()
  local chosen = ChosenList()
  if type(rawget(_G, "BO3CustomsSessionMods")) == "table" then
    if #chosen == 0 then
      return "The host is using no mods. Your own selection returns when you leave."
    end
    if SameNames(chosen, active) and #LoadingList(active) == 0 then
      return "Host mods loaded: " .. Titles(active)
    end
    return "Host mods selected: " .. Titles(chosen) .. ". They load with the host's game."
  end
  local notice = rawget(_G, "BO3CustomsModsNotice")
  if type(notice) == "string" and notice ~= "" and SameNames(chosen, active) and #LoadingList(active) == 0 then
    return notice
  end
  if #chosen == 0 and #active == 0 then
    if #Mods() == 0 then
      return "No mods installed. Copy mod folders made by FF Porter into BO3-Customs/mods/, then press Refresh."
    end
    return "No mod is loaded. Tick mods to load them; the first ticked mod wins when two change the same thing."
  end
  if SameNames(chosen, active) then
    local loading = LoadingList(active)
    if #loading > 0 then
      return "Loading: " .. Titles(loading) .. "..."
    end
    if rawget(_G, "BO3CustomsModsRefresh") == true then
      return "Loaded: " .. Titles(active) .. ". Close this menu to refresh the menus."
    end
    return "Loaded: " .. Titles(active)
  end
  if #active == 0 then
    return "Close this menu to load: " .. Titles(chosen)
  end
  if #chosen == 0 then
    return "Close this menu to unload: " .. Titles(active)
  end
  return "Loaded: " .. Titles(active) .. ". Close this menu to switch to: " .. Titles(chosen)
end

function UI.SetStatus(text)
  if IsElement(UI.status) then
    pcall(function()
      UI.status:setText(text)
    end)
    return true
  end
  return false
end

local function OverlayType(name)
  local util = cod.OverlayUtility
  local types = type(util) == "table" and util.OverlayTypes or nil
  if type(types) ~= "table" then
    return nil
  end
  if types[name] ~= nil then
    return types[name]
  end
  return types.Warning
end

local function ShowChoice(menu, controller, title, text, choices, kind)
  local util = cod.OverlayUtility
  if type(util) ~= "table" or type(util.CreateOverlay) ~= "function" or type(util.Overlays) ~= "table" then
    Log("no overlay utility")
    return false
  end
  if type(DataSources) ~= "table" or type(DataSourceHelpers) ~= "table" then
    Log("no data sources")
    return false
  end
  local listName = "BO3CustomsMods_List"
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
                  local ok, err = pcall(choice.run, actionController or controller)
                  if not ok then
                    Log("choice failed: " .. tostring(err))
                  end
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
  local category = OverlayType(kind)
  if category ~= nil then
    overlay.categoryType = category
  end
  if util.GoBackPropertyName ~= nil and type(util.DefaultGoBack) == "function" then
    overlay[util.GoBackPropertyName] = util.DefaultGoBack
  end
  util.Overlays.BO3CustomsMods = overlay
  local ok, err = pcall(util.CreateOverlay, Controller(controller), menu, "BO3CustomsMods")
  if not ok then
    Log("popup failed (controller " .. type(controller) .. "): " .. tostring(err))
  end
  return ok
end

local function UpdateModel()
  local ok, model = pcall(engine.CreateModel, engine.GetGlobalModel(), "ModsGlobal.update")
  if ok then
    return model
  end
  return nil
end

local function ModelText(element, name)
  if not IsElement(element) then
    return nil
  end
  local ok, value = pcall(function()
    local model = element:getModel()
    if model == nil then
      return nil
    end
    return engine.GetModelValue(engine.GetModel(model, name))
  end)
  if ok and type(value) == "string" and value ~= "" then
    return value
  end
  return nil
end

local function TickOf(folder)
  local position = type(folder) == "string" and Position(folder) or nil
  if position ~= nil then
    return tostring(position)
  end
  return ""
end

local function ShowTick(entry, tick)
  local on = type(tick) == "string" and tick ~= ""
  pcall(function()
    if IsElement(entry.bo3Check) then
      entry.bo3Check:setAlpha(on and 1 or 0)
    end
  end)
end

local function UpdateCheck(entry, folder)
  ShowTick(entry, TickOf(folder))
end

local function ForEachRow(fn)
  local seen = {}
  for entry in pairs(UI.rows) do
    seen[entry] = true
    fn(entry)
  end
  local menu = UI.menu
  local widget = IsElement(menu) and menu.WidgetModsLoad0 or nil
  local list = IsElement(widget) and widget.ListMods or nil
  if not IsElement(list) then
    return
  end
  local function walk(element, depth)
    if depth > 3 or not IsElement(element) then
      return
    end
    local child = element:getFirstChild()
    while IsElement(child) do
      if child.bo3Check ~= nil and not seen[child] then
        seen[child] = true
        fn(child)
      end
      walk(child, depth + 1)
      child = child:getNextSibling()
    end
  end
  local ok, err = pcall(walk, list, 0)
  if not ok then
    LogOnce("walk " .. tostring(err), "row walk failed: " .. tostring(err))
  end
end

function UI.RefreshChecks()
  local rows = 0
  ForEachRow(function(entry)
    rows = rows + 1
    UpdateCheck(entry, ModelText(entry, "folder"))
  end)
  local menu = UI.menu
  local widget = IsElement(menu) and menu.WidgetModsLoad0 or nil
  local list = IsElement(widget) and widget.ListMods or nil
  local refreshed = false
  if IsElement(list) then
    refreshed = pcall(function()
      if type(UpdateDataSource) == "function" then
        UpdateDataSource(menu, list, UI.controller or 0)
      else
        list:updateDataSource()
      end
    end)
  end
  Log("refresh: " .. rows .. " row(s) updated, list " .. (IsElement(list) and (refreshed and "refreshed" or "refresh failed") or "not found"))
end

function UI.RefreshStates()
  local menu = UI.menu
  if IsElement(menu) then
    pcall(function()
      menu:processEvent({ name = "update_state", menu = menu })
    end)
  end
end

local function RefreshLobby()
  pcall(function()
    local model = engine.GetModel(engine.GetGlobalModel(), "lobbyRoot.lobbyButtonUpdate")
    if model ~= nil then
      engine.ForceNotifyModelSubscriptions(model)
    end
  end)
end

function UI.DataChanged()
  if not UI.buttonAdded then
    RefreshLobby()
  end
  local model = UpdateModel()
  if model ~= nil then
    engine.SetModelValue(model, (engine.GetModelValue(model) or 0) + 1)
  end
  UI.RefreshChecks()
  UI.RefreshStates()
  UI.SetStatus(StatusText())
end

function UI.Choose(menu, controller, name)
  if type(rawget(_G, "BO3CustomsSessionMods")) == "table" then
    UI.SetStatus(StatusText())
    return
  end
  UI.controller = Controller(controller)
  UI.pending = { menu = menu, controller = Controller(controller) }
  Log("chose " .. tostring(name) .. " (controller " .. type(controller) .. ")")
  engine.SetDvar("bo3customs_mod", name)
end

function UI.SetChosen(list)
  rawset(_G, "BO3CustomsChosenMods", list)
  rawset(_G, "BO3CustomsChosenMod", list[1] or "")
  UI.RefreshChecks()
  UI.RefreshStates()
end

function UI.Toggle(menu, controller, folder)
  if type(rawget(_G, "BO3CustomsSessionMods")) == "table" then
    UI.SetStatus(StatusText())
    return
  end
  local list = {}
  local removed = false
  for _, name in ipairs(ChosenList()) do
    if Same(name, folder) then
      removed = true
    else
      table.insert(list, name)
    end
  end
  if not removed then
    table.insert(list, folder)
  end
  UI.Choose(menu, controller, #list > 0 and table.concat(list, ",") or "!off")
  UI.SetChosen(list)
end

function UI.Unload(menu, controller)
  if type(rawget(_G, "BO3CustomsSessionMods")) == "table" then
    UI.SetStatus(StatusText())
    return
  end
  -- Queue recovery before refreshing widgets supplied by the active mod.
  UI.Choose(menu, controller, "!off")
  UI.SetChosen({})
end

function UI.Open(menu, controller, page)
  local mods = Mods()
  local size = UI.pageSize
  local pages = math.max(1, math.ceil(#mods / size))
  page = page or 1
  if page > pages then
    page = 1
  end
  local choices = {}
  local first = (page - 1) * size + 1
  local active = Active()
  for index = first, math.min(#mods, first + size - 1) do
    local mod = mods[index]
    if type(mod) == "table" and type(mod.name) == "string" then
      table.insert(choices, {
        label = ModLabel(mod),
        focus = active ~= nil and Same(active.name, mod.name),
        run = function(chosenController)
          UI.Toggle(menu, chosenController, mod.name)
        end,
      })
    end
  end
  if pages > 1 then
    table.insert(choices, {
      label = "More mods (" .. page .. "/" .. pages .. ")",
      run = function(chosenController)
        UI.Open(menu, chosenController, page + 1)
      end,
    })
  end
  if Chosen() ~= "" or active ~= nil then
    table.insert(choices, {
      label = "Turn mods off",
      run = function(chosenController)
        UI.Unload(menu, chosenController)
      end,
    })
  end
  if #choices == 0 then
    table.insert(choices, { label = "OK", focus = true })
  end
  local focused = false
  for _, choice in ipairs(choices) do
    focused = focused or choice.focus == true
  end
  if not focused then
    choices[1].focus = true
  end
  Log("opened page " .. page .. "/" .. pages .. " with " .. #mods .. " mods")
  return ShowChoice(menu, controller, "MODS", Describe(), choices, "Settings")
end

function UI.OpenMenu(menu, controller)
  if UI.pcMenu and type(OpenPopup) == "function" then
    local ok, err = pcall(OpenPopup, menu, "MenuModsLoad", controller)
    if ok then
      return true
    end
    Log("mods menu failed: " .. tostring(err))
  end
  return UI.Open(menu, controller, 1)
end

function UI.Applied(name, state, message)
  local pending = UI.pending
  UI.pending = nil
  Log("applied " .. tostring(name) .. ": state " .. tostring(state) .. ", " .. tostring(message))
  if UI.SetStatus(tostring(message)) or state ~= -1 or pending == nil or not IsElement(pending.menu) or
      pending.menu ~= UI.menu then
    return
  end
  ShowChoice(pending.menu, pending.controller, "MODS", tostring(message), { { label = "OK", focus = true } },
    state == -1 and "Error" or "Info")
end

local ownedActions = {}
local function Define(name, fn)
  if name == "Mods_LoadMod" or name == "Mods_Unload" then
    ownedActions[name] = fn
    return
  end
  if rawget(_G, name) == nil then
    rawset(_G, name, fn)
  end
end

Define("Mods_Enabled", function()
  return type(rawget(_G, "BO3CustomMods")) == "table"
end)

Define("Mods_OpenLoadMenu", function(self, element, controller, param, menu)
  local target = menu
  if not IsElement(target) then
    target = self
  end
  local ok, err = pcall(UI.OpenMenu, target, controller)
  if not ok then
    Log("open failed: " .. tostring(err))
  end
end)

Define("Mods_LoadMod", function(self, element, controller)
  local folder = ModelText(element, "folder")
  if folder == nil then
    Log("load: the focused entry has no mod")
    return
  end
  UI.Toggle(UI.menu or self, controller, folder)
end)

Define("Mods_Unload", function(self, element, controller)
  UI.Unload(UI.menu or self, controller)
end)

Define("Mods_RefreshListMods", function(self, element, controller)
  Log("refresh")
  UI.SetStatus("Looking for mods...")
  engine.SetDvar("bo3customs_refresh", 1)
end)

if rawget(engine, "Mods_Lists_UpdateMods") == nil then
  rawset(engine, "Mods_Lists_UpdateMods", function() end)
end

local updateModel = UpdateModel()
if updateModel ~= nil and engine.GetModelValue(updateModel) == nil then
  engine.SetModelValue(updateModel, 0)
end

if type(DataSources) == "table" and type(DataSourceHelpers) == "table" then
  DataSources.ModsLoadEntry = DataSourceHelpers.ListSetup("ModsLoadEntry", function(controller)
    local items = {}
    for _, mod in ipairs(Mods()) do
      if type(mod) == "table" and type(mod.name) == "string" then
        table.insert(items, {
          models = {
            name = Short(TitleOf(mod), 64),
            ugcName = mod.ugc or mod.name,
            internalName = mod.internal or mod.name,
            folder = mod.name,
            tick = TickOf(mod.name),
            description = mod.description or "",
            image = mod.image or "",
          },
        })
      end
    end
    return items
  end)
end

local function Restyle(entry)
  for _, holder in ipairs({ "FocusWidgetBG1", "FocusWidgetBG2" }) do
    local background = entry[holder]
    if IsElement(background) then
      for _, part in ipairs({ "LeftHightLigh", "Centerhighligh", "LeftHightLigh0" }) do
        local image = background[part]
        if IsElement(image) then
          image:setRGB(0.5, 0.5, 0.5)
          image:setAlpha(0.35)
        end
      end
    end
  end
end

local function Square(parent, left, top, size, r, g, b, alpha)
  local image = LUI.UIImage.new()
  image:setLeftRight(true, false, left, left + size)
  image:setTopBottom(true, false, top, top + size)
  image:setRGB(r, g, b)
  image:setAlpha(alpha)
  parent:addElement(image)
  return image
end

local function CheckColor()
  local colors = rawget(_G, "ColorSet")
  local orange = type(colors) == "table" and colors.Orange or nil
  if type(orange) == "table" and type(orange.r) == "number" then
    return orange.r, orange.g, orange.b
  end
  return 1, 0.5, 0.1
end

local function AddCheckbox(entry)
  if IsElement(entry.ModName) then
    entry.ModName:setLeftRight(true, false, 34, 590)
  end
  Square(entry, 9, 4, 16, 0.85, 0.85, 0.85, 0.9)
  Square(entry, 11, 6, 12, 0.06, 0.06, 0.06, 1)
  local r, g, b = CheckColor()
  entry.bo3Check = Square(entry, 11, 6, 12, r, g, b, 0)
  UI.rows[entry] = true
  entry:linkToElementModel(entry, "tick", true, function(model)
    ShowTick(entry, engine.GetModelValue(model))
  end)
end

local Details = { left = 775, right = 1205, top = 125, bottom = 604, title = 26, imageHeight = 242, gap = 10, line = 20,
  speed = 24, hold = 3000, stickSpeed = 260, deadzone = 0.25, resume = 3000 }
Details.width = Details.right - Details.left

local function Now()
  local ok, value = pcall(engine.milliseconds)
  if ok and type(value) == "number" then
    return value
  end
  return 0
end

local function ScrollAt(state)
  if state.animTo == nil or state.animDuration == nil or state.animDuration <= 0 then
    return state.offset or 0
  end
  local done = math.min(1, math.max(0, (Now() - state.animStart) / state.animDuration))
  return state.animFrom + (state.animTo - state.animFrom) * done
end

local function ScrollAnimate(state, duration, offset)
  state.animation = state.generation
  state.animFrom = ScrollAt(state)
  state.animTo = offset
  state.animStart = Now()
  state.animDuration = math.max(1, math.floor(duration))
  state.offset = offset
  state.text:beginAnimation("bo3_scroll", state.animDuration, false, false, CoD.TweenType.Linear)
  state.text:setTopBottom(true, false, -offset, Details.line - offset)
end

local function ScrollMove(state, offset)
  state.offset = offset
  state.animTo = nil
  state.text:beginAnimation("bo3_manual", 1, false, false, CoD.TweenType.Linear)
  state.text:setTopBottom(true, false, -offset, Details.line - offset)
end

local function StickY(state)
  if state.stick == nil then
    local stick = engine.GetModel(engine.GetModelForController(Controller(UI.controller)), "RightStick")
    state.stick = stick and engine.GetModel(stick, "Y") or false
  end
  if not state.stick then
    return 0
  end
  local value = engine.GetModelValue(state.stick)
  return type(value) == "number" and value or 0
end

local function ScrollStick(state, event)
  if state.distance <= 1 then
    return
  end
  local y = StickY(state)
  local now = Now()
  if math.abs(y) > Details.deadzone then
    if not state.manual then
      state.manual = true
      state.offset = ScrollAt(state)
      state.generation = state.generation + 1
    end
    local elapsed = type(event) == "table" and tonumber(event.timeElapsed) or 16
    local offset = state.offset - y * Details.stickSpeed * math.min(elapsed, 100) / 1000
    ScrollMove(state, math.max(0, math.min(state.distance, offset)))
    state.touched = now
    return
  end
  if state.manual and now - (state.touched or 0) >= Details.resume then
    state.manual = false
    state.generation = state.generation + 1
    if state.offset >= state.distance - 1 then
      state.phase = "bottom"
      ScrollAnimate(state, Details.hold, state.distance)
    else
      state.phase = "down"
      ScrollAnimate(state, (state.distance - state.offset) / Details.speed * 1000, state.distance)
    end
  end
end

local function ScrollReset(state, visible)
  state.generation = state.generation + 1
  local text = state.text
  if type(text.completeAnimation) == "function" then
    pcall(text.completeAnimation, text)
  end
  state.offset = 0
  state.animTo = nil
  state.manual = false
  text:setTopBottom(true, false, 0, Details.line)
  local ok, height = pcall(text.getTextHeightForWidth, text, Details.width)
  state.distance = 0
  if ok and type(height) == "number" then
    state.distance = math.max(0, height - visible + 6)
  end
  state.phase = "top"
  if state.distance > 1 then
    ScrollAnimate(state, Details.hold, 0)
  end
end

local function ScrollStep(state)
  if state.animation ~= state.generation or state.distance <= 1 then
    return
  end
  if state.phase == "top" then
    state.phase = "down"
    ScrollAnimate(state, state.distance / Details.speed * 1000, state.distance)
  elseif state.phase == "down" then
    state.phase = "bottom"
    ScrollAnimate(state, Details.hold, state.distance)
  else
    state.phase = "top"
    state.text:setTopBottom(true, false, 0, Details.line)
    state.offset = 0
    state.animTo = nil
    ScrollAnimate(state, Details.hold, 0)
  end
end

local function ModList(menu)
  local widget = IsElement(menu) and menu.WidgetModsLoad0 or nil
  local list = IsElement(widget) and widget.ListMods or nil
  if IsElement(list) then
    return list
  end
  return nil
end

local function FocusedFolder(menu)
  local list = ModList(menu)
  local active = list ~= nil and list.activeWidget or nil
  if IsElement(active) then
    local folder = ModelText(active, "folder")
    if folder ~= nil then
      return folder
    end
  end
  for entry in pairs(UI.rows) do
    local ok, focused = pcall(function()
      return entry:isInFocus()
    end)
    if ok and focused then
      return ModelText(entry, "folder")
    end
  end
  return nil
end

local function ShowDetails(panel, folder)
  local mod = FindMod(folder)
  panel.folder = folder
  LogOnce("details " .. tostring(folder), "details for " .. tostring(folder) .. ": " ..
    (mod == nil and "no such mod" or ("image " .. tostring(mod.image ~= nil) .. ", description " .. tostring(mod.description ~= nil))))
  if mod == nil then
    panel:setAlpha(0)
    return
  end
  panel:setAlpha(1)
  panel.title:setText(Short(TitleOf(mod), 48))
  local top = Details.top + Details.title + Details.gap
  local register = rawget(_G, "RegisterImage") or rawget(engine, "RegisterImage")
  local picture = type(mod.image) == "string" and mod.image ~= "" and type(register) == "function"
  if picture then
    picture = pcall(function()
      panel.image:setImage(register(mod.image))
    end)
  end
  panel.image:setAlpha(picture and 1 or 0)
  if picture then
    top = top + Details.imageHeight + Details.gap
  end
  panel.box:setTopBottom(true, false, top, Details.bottom)
  local text = type(mod.description) == "string" and mod.description ~= "" and mod.description or "No description."
  panel.text:setText(text)
  ScrollReset(panel.scroll, Details.bottom - top)
end

local function AddDetails(menu)
  for _, name in ipairs({ "VersionText", "ModNameText" }) do
    if IsElement(menu[name]) then
      menu[name]:setAlpha(0)
    end
  end
  local panel = LUI.UIElement.new()
  panel:setLeftRight(true, true, 0, 0)
  panel:setTopBottom(true, true, 0, 0)
  panel:setAlpha(0)
  local title = LUI.UIText.new()
  title:setLeftRight(true, false, Details.left, Details.right)
  title:setTopBottom(true, false, Details.top, Details.top + Details.title)
  title:setTTF("fonts/RefrigeratorDeluxe-Regular.ttf")
  if Enum ~= nil and Enum.LUIAlignment ~= nil then
    title:setAlignment(Enum.LUIAlignment.LUI_ALIGNMENT_LEFT)
  end
  panel:addElement(title)
  local imageTop = Details.top + Details.title + Details.gap
  local image = LUI.UIImage.new()
  image:setLeftRight(true, false, Details.left, Details.right)
  image:setTopBottom(true, false, imageTop, imageTop + Details.imageHeight)
  image:setAlpha(0)
  panel:addElement(image)
  local box = LUI.UIElement.new()
  box:setLeftRight(true, false, Details.left, Details.right)
  box:setTopBottom(true, false, imageTop, Details.bottom)
  box:setUseStencil(true)
  local text = LUI.UIText.new()
  text:setLeftRight(true, false, 0, Details.width)
  text:setTopBottom(true, false, 0, Details.line)
  text:setTTF("fonts/default.ttf")
  if Enum ~= nil and Enum.LUIAlignment ~= nil then
    text:setAlignment(Enum.LUIAlignment.LUI_ALIGNMENT_LEFT)
  end
  text:setRGB(0.75, 0.75, 0.75)
  box:addElement(text)
  panel:addElement(box)
  panel.title = title
  panel.image = image
  panel.box = box
  panel.text = text
  panel.scroll = { text = text, generation = 0, animation = -1, distance = 0, offset = 0, phase = "top" }
  text:registerEventHandler("transition_complete_bo3_scroll", function(element, event)
    if type(event) == "table" and event.interrupted then
      return
    end
    local ok, err = pcall(ScrollStep, panel.scroll)
    if not ok then
      LogOnce("scroll " .. tostring(err), "details scroll failed: " .. tostring(err))
    end
  end)
  local function Update(event, stick)
    local ok, err = pcall(function()
      local folder = FocusedFolder(menu) or panel.folder
      if folder ~= panel.folder then
        ShowDetails(panel, folder)
      end
      if stick then
        ScrollStick(panel.scroll, event)
      end
    end)
    if not ok then
      LogOnce("details " .. tostring(err), "details update failed: " .. tostring(err))
    end
  end
  local list = ModList(menu)
  if list ~= nil then
    local handlers = list.m_eventHandlers
    local stock = type(handlers) == "table" and handlers.list_item_gain_focus or nil
    list:registerEventHandler("list_item_gain_focus", function(element, event)
      local result = nil
      if type(stock) == "function" then
        result = stock(element, event)
      end
      Update(event, false)
      return result
    end)
  end
  panel:addElement(LUI.UITimer.newElementTimer(16, false, function(event)
    Update(event, true)
  end))
  menu:addElement(panel)
  Log("details panel ready (list " .. (list ~= nil and "found" or "not found") .. ")")
  Update(nil, false)
  return panel
end

local loaded, problem = pcall(require, "ui.uieditor.menus.Mods.MenuModsLoad")
local create = LUI ~= nil and LUI.createMenu ~= nil and LUI.createMenu.MenuModsLoad or nil
UI.pcMenu = loaded and type(create) == "function"
if UI.pcMenu then
  local entryClass = rawget(cod, "EntryModsLoad")
  if type(entryClass) == "table" and type(entryClass.new) == "function" and not UI.entryWrapped then
    UI.entryWrapped = true
    local makeEntry = entryClass.new
    entryClass.new = function(...)
      local entry = makeEntry(...)
      local ok, err = pcall(Restyle, entry)
      if not ok then
        LogOnce("restyle " .. tostring(err), "entry restyle failed: " .. tostring(err))
      end
      ok, err = pcall(AddCheckbox, entry)
      if not ok then
        LogOnce("checkbox " .. tostring(err), "entry checkbox failed: " .. tostring(err))
      end
      return entry
    end
  end
  local menuClass = rawget(cod, "Menu")
  local setLabel = type(menuClass) == "table" and rawget(menuClass, "SetButtonLabel") or nil
  if type(setLabel) == "function" and not UI.labelWrapped then
    UI.labelWrapped = true
    menuClass.SetButtonLabel = function(menu, button, label, ...)
      if label == "MENU_LOAD" and (UI.creating or (UI.menu ~= nil and menu == UI.menu)) then
        label = "MENU_SELECT"
      end
      return setLabel(menu, button, label, ...)
    end
  end
  LUI.createMenu.MenuModsLoad = function(controller, ...)
    UI.creating = true
    local made, menu = pcall(create, controller, ...)
    UI.creating = false
    if not made then
      error(menu, 0)
    end
    UI.menu = menu
    UI.status = nil
    if IsElement(menu) then
      local shown, err = pcall(AddDetails, menu)
      if not shown then
        Log("details panel failed: " .. tostring(err))
      end
      if LUI.OverrideFunction_CallOriginalSecond ~= nil then
        pcall(LUI.OverrideFunction_CallOriginalSecond, menu, "close", function()
          if UI.menu == menu then
            UI.menu = nil
            UI.status = nil
            UI.rows = {}
          end
          Log("menu closed: applying the mod changes")
          pcall(engine.SetDvar, "bo3customs_mods_apply", 1)
        end)
      end
    end
    return menu
  end
  Log("pc mods menu ready")
else
  Log("pc mods menu unavailable: " .. tostring(problem))
end

local function AddModsButton(controller, buttons)
  if type(buttons) ~= "table" or type(rawget(_G, "BO3CustomMods")) ~= "table" then
    return
  end
  local modes = false
  for _, entry in ipairs(buttons) do
    if type(entry) == "table" then
      if entry.customId == ButtonId then
        return
      end
      modes = modes or ModeIds[entry.customId] == true
    end
  end
  if not modes then
    return
  end
  local selected = false
  pcall(function()
    selected = cod.LobbyMenus.History[LobbyData.GetLobbyNav()] == ButtonId
  end)
  table.insert(buttons, {
    optionDisplay = "MENU_MODS_CAPS",
    action = rawget(_G, "Mods_OpenLoadMenu"),
    param = nil,
    customId = ButtonId,
    isLargeButton = false,
    isLastButtonInGroup = false,
    disabled = false,
    selected = selected,
    isBreadcrumbNew = false,
    warning = false,
  })
  UI.buttonAdded = true
  LogOnce("added", "MODS button added (menu script v5)")
end

local function HideOnline(buttons)
  if type(buttons) ~= "table" then
    return
  end
  for index = #buttons, 1, -1 do
    local entry = buttons[index]
    if type(entry) == "table" and entry.optionDisplay == "XBOXLIVE_PLAY_ONLINE_CAPS" then
      local previous = buttons[index - 1]
      if entry.isLastButtonInGroup and type(previous) == "table" then
        previous.isLastButtonInGroup = true
      end
      table.remove(buttons, index)
      LogOnce("hide online", "PLAY ONLINE hidden")
    end
  end
end

lobbyMenus.AddButtonsForTarget = function(controller, id, ...)
  local buttons = stockAdd(controller, id, ...)
  pcall(HideOnline, buttons)
  local ok, err = pcall(AddModsButton, controller, buttons)
  if not ok then
    LogOnce("add " .. tostring(err), "button failed: " .. tostring(err))
  end
  return buttons
end

-- The PC utility or a mod may already define these actions. Its handler still
-- owns unrelated menus; our Mods menu must use the console loader's selection.
for name, action in pairs(ownedActions) do
  local original = rawget(_G, name)
  rawset(_G, name, function(self, element, controller, param, menu, ...)
    if UI.creating or (UI.menu ~= nil and (self == UI.menu or menu == UI.menu)) or type(original) ~= "function" then
      return action(self, element, controller, param, menu, ...)
    end
    return original(self, element, controller, param, menu, ...)
  end)
end

RefreshLobby()
Log("installed")
