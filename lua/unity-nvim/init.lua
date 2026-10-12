-- unity-nvim: Neovim side of the Unity bridge.
-- Detects Unity projects, listens on the per-project address (so Unity opens files here), connects to the
-- editor's bridge to refresh/compile on save, play/stop, and shows compiler errors and console logs.
local M = {}

---@class UnityNvimConfig
M.defaults = {
  --- connect to the Unity editor automatically when nvim is inside a Unity project
  auto_connect = true,
  --- listen on the project's address so Unity reuses this nvim to open files
  listen = true,
  --- set the window title ("unity-nvim: <Project>") so Unity can bring this window to the front
  set_title = true,
  --- ask Unity to refresh (import + compile) when one of these files is saved
  refresh_on_save = true,
  refresh_patterns = { "*.cs", "*.shader", "*.hlsl", "*.hlsli", "*.cginc", "*.compute", "*.uxml", "*.uss", "*.asmdef", "*.asmref" },
  --- Unity compiler messages as diagnostics (skipped when the LSP already reports the same code on that line)
  diagnostics = true,
  --- put Unity compiler errors in the quickfix list
  quickfix = true,
  open_quickfix = false,
  --- notify when a compile finishes
  notify = true,
  --- receive Unity console logs (:Unity log)
  logs = true,
  log_max = 2000,
  --- vim.notify exceptions/errors logged by Unity (rate limited)
  notify_exceptions = true,
}

---@type UnityNvimConfig
M.config = vim.deepcopy(M.defaults)

local started = false

---@param opts? UnityNvimConfig
function M.setup(opts)
  M.config = vim.tbl_deep_extend("force", vim.deepcopy(M.defaults), opts or {})
  -- normally runs before the plugin/ file's (scheduled) attach; a later setup() re-attaches with the new config
  if started then
    started = false
    require("unity-nvim.bridge").state.root = nil
  end
  M.attach()
end

--- Current project root (nil outside Unity projects).
function M.root()
  return require("unity-nvim.bridge").state.root
end

--- Called on startup and on :cd. Cheap: two fs_stat calls when not in a Unity project.
function M.attach()
  local project = require("unity-nvim.project")
  local bridge = require("unity-nvim.bridge")
  local root = project.find_root()
  if root == bridge.state.root and started then
    return
  end
  started = true
  bridge.stop()
  bridge.state.root = root
  if not root then
    return
  end

  if M.config.listen then
    local addr = project.address(root)
    if not vim.tbl_contains(vim.fn.serverlist(), addr) and not pcall(vim.fn.serverstart, addr) then
      -- another nvim already serves this project (fine), or a crashed one left its socket file behind
      local ok, chan = pcall(vim.fn.sockconnect, "pipe", addr, { rpc = true })
      if ok and chan > 0 then
        vim.fn.chanclose(chan)
      elseif vim.fn.has("win32") == 0 then
        os.remove(addr)
        pcall(vim.fn.serverstart, addr)
      end
    end
  end
  if M.config.set_title then
    vim.o.titlestring = "unity-nvim: " .. vim.fs.basename(root)
    vim.o.title = true
  end
  if M.config.auto_connect then
    bridge.start(root)
  end
end

--- Normal window to show a file in (not a float, terminal, file tree or picker).
local function target_window()
  local function usable(win)
    local buf = vim.api.nvim_win_get_buf(win)
    return vim.api.nvim_win_get_config(win).relative == "" and vim.bo[buf].buftype == ""
  end
  local cur = vim.api.nvim_get_current_win()
  if usable(cur) then
    return cur
  end
  for _, win in ipairs(vim.api.nvim_tabpage_list_wins(0)) do
    if usable(win) then
      return win
    end
  end
end

--- Open file at line/col (1-based). Used by Unity through the bridge or `nvim --server ... --remote-expr`.
---@return boolean
function M.open(file, line, col)
  vim.cmd("stopinsert")
  if not file or file == "" then
    return true
  end
  local win = target_window()
  if win then
    vim.api.nvim_set_current_win(win)
  else
    vim.cmd("botright vsplit")
  end
  vim.cmd("drop " .. vim.fn.fnameescape(file))
  local last = vim.api.nvim_buf_line_count(0)
  pcall(vim.api.nvim_win_set_cursor, 0, { math.min(math.max(line or 1, 1), last), math.max((col or 1) - 1, 0) })
  vim.cmd("normal! zv")
  vim.cmd("normal! zz")
  return true
end

local commands = {
  play = function(b) b.request("play") end,
  stop = function(b) b.request("stop") end,
  pause = function(b) b.request("pause") end,
  refresh = function(b) b.refresh() end,
  recompile = function(b) b.request("recompile") end,
  status = function(b) b.show_status() end,
  log = function() require("unity-nvim.log").open() end,
  connect = function(b)
    if not b.state.root then
      vim.notify("Not inside a Unity project", vim.log.levels.WARN, { title = "Unity" })
      return
    end
    b.start(b.state.root, true)
  end,
  disconnect = function(b) b.stop() end,
}

function M.command(args)
  local sub = args.fargs[1] or "status"
  local fn = commands[sub]
  if not fn then
    vim.notify("Unknown subcommand: " .. sub, vim.log.levels.ERROR, { title = "Unity" })
    return
  end
  fn(require("unity-nvim.bridge"))
end

function M.complete(arg_lead)
  return vim.tbl_filter(function(c)
    return c:find("^" .. vim.pesc(arg_lead)) ~= nil
  end, vim.tbl_keys(commands))
end

return M
