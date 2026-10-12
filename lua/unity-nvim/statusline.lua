-- Statusline component: "Unity ✓", "Unity ⠹ compiling", "Unity ✗ 3", "Unity ▶".
--   lualine:   sections = { lualine_x = { require("unity-nvim.statusline").lualine() } }
--   others:    %{%v:lua.require'unity-nvim.statusline'.text()%}
local M = {}

local uv = vim.uv or vim.loop
local spinner = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" }
local timer ---@type uv.uv_timer_t?

local function state()
  return require("unity-nvim.bridge").state
end

local function busy()
  local s = state().status
  return s == "compiling" or s == "refreshing" or s == "reloading"
end

function M.visible()
  return state().root ~= nil
end

function M.text()
  local s = state()
  if not s.root then
    return ""
  end
  if s.status == "disconnected" then
    return "Unity ○"
  end
  if busy() then
    local frame = spinner[math.floor(uv.hrtime() / 1e8) % #spinner + 1]
    return "Unity " .. frame .. " " .. s.status
  end
  local errors = s.last and s.last.errors or 0
  local play = s.unity.playing and (s.unity.paused and " ⏸" or " ▶") or ""
  return (errors > 0 and ("Unity ✗ " .. errors) or "Unity ✓") .. play
end

function M.highlight()
  local s = state()
  if s.status == "disconnected" then
    return "Comment"
  elseif busy() then
    return "DiagnosticWarn"
  elseif s.last and (s.last.errors or 0) > 0 then
    return "DiagnosticError"
  end
  return "DiagnosticOk"
end

local function redraw()
  local ok, lualine = pcall(require, "lualine")
  if ok then
    pcall(lualine.refresh, { place = { "statusline" } })
  else
    vim.cmd.redrawstatus()
  end
end

--- Called by the bridge on any change; animates the spinner only while Unity is busy.
function M.changed()
  vim.schedule(redraw)
  if busy() and not timer then
    timer = uv.new_timer()
    timer:start(100, 100, vim.schedule_wrap(function()
      redraw()
      if not busy() and timer then
        timer:stop()
        timer:close()
        timer = nil
        redraw()
      end
    end))
  end
end

--- lualine component spec.
function M.lualine()
  return {
    M.text,
    cond = M.visible,
    color = function()
      local hl = vim.api.nvim_get_hl(0, { name = M.highlight(), link = false })
      return hl.fg and { fg = ("#%06x"):format(hl.fg) } or nil
    end,
    on_click = function()
      require("unity-nvim.bridge").show_status()
    end,
  }
end

return M
