-- TCP client for the Unity editor bridge (one JSON object per line).
-- Reconnects with backoff: Unity drops the socket on every domain reload (after each successful compile).
local M = {}

local uv = vim.uv or vim.loop
local VERSION = "0.1.0"

M.state = {
  root = nil, ---@type string?
  connected = false,
  --- "disconnected" | "idle" | "refreshing" | "compiling" | "reloading"
  status = "disconnected",
  unity = {}, -- compiling / playing / paused / updating
  last = nil, -- last compileFinished payload
  project = nil,
  unity_version = nil,
}

local tcp ---@type uv.uv_tcp_t?
local active = false
local backoff = 250
local retry_timer ---@type uv.uv_timer_t?
local refresh_timer ---@type uv.uv_timer_t?
local next_id = 0
local pending = {} ---@type table<integer, fun(ok: boolean, result: any)>
local announced = {} ---@type table<string, boolean> roots already announced with a notification
local save_group = vim.api.nvim_create_augroup("unity_nvim_save", { clear = true })

local function cfg()
  return require("unity-nvim").config
end

local function notify(msg, level)
  vim.notify(msg, level or vim.log.levels.INFO, { title = "Unity" })
end

local function changed()
  require("unity-nvim.statusline").changed()
end

local function set_status(s)
  if M.state.status ~= s then
    M.state.status = s
    changed()
  end
end

local function close_tcp()
  if tcp then
    local t = tcp
    tcp = nil
    if not t:is_closing() then
      t:read_stop()
      t:close()
    end
  end
  for id, cb in pairs(pending) do
    pending[id] = nil
    vim.schedule(function()
      cb(false, "disconnected")
    end)
  end
end

local function send(msg)
  if not tcp then
    return false
  end
  tcp:write(vim.json.encode(msg) .. "\n")
  return true
end

local connect

local function schedule_retry(delay)
  if not active then
    return
  end
  retry_timer = retry_timer or uv.new_timer()
  retry_timer:stop()
  retry_timer:start(delay, 0, vim.schedule_wrap(function()
    connect()
  end))
end

local function on_disconnect()
  close_tcp()
  local was = M.state.connected
  M.state.connected = false
  -- Unity restarts the bridge after every domain reload (compile, entering play mode): show that while
  -- reconnecting; only a failing reconnect means the editor is really gone
  if was then
    set_status("reloading")
  elseif backoff >= 2000 then
    set_status("disconnected")
  end
  changed()
  if active then
    backoff = was and 250 or math.min(backoff * 2, 5000)
    schedule_retry(backoff)
  end
end

local function apply_compile(result)
  if type(result) ~= "table" then
    return
  end
  M.state.last = result
  require("unity-nvim.diagnostics").apply(M.state.root, result)
  changed()
end

local handlers = {}

function handlers.welcome(msg)
  M.state.connected = true
  M.state.project = msg.projectName
  M.state.unity_version = msg.unityVersion
  M.state.unity = type(msg.state) == "table" and msg.state or {}
  backoff = 250
  local last = msg.lastCompile
  if type(last) == "table" and (not M.state.last or M.state.last.time ~= last.time) then
    apply_compile(last)
  end
  set_status(M.state.unity.compiling and "compiling" or "idle")
  if not announced[M.state.root] then
    announced[M.state.root] = true
    notify(("Connected to Unity %s (%s)"):format(msg.unityVersion or "?", msg.projectName or "?"))
  end
end

function handlers.response(msg)
  local cb = pending[msg.id]
  pending[msg.id] = nil
  if cb then
    if msg.ok then
      cb(true, msg.result)
    else
      cb(false, msg.error)
    end
  end
end

handlers.event = {
  compileStarted = function()
    set_status("compiling")
  end,
  compileFinished = function(msg)
    apply_compile(msg)
    -- a successful compile is followed by a domain reload, which drops this connection
    set_status((msg.errors or 0) == 0 and "reloading" or "idle")
  end,
  playModeChanged = function(msg)
    M.state.unity = type(msg.state) == "table" and msg.state or M.state.unity
    changed()
  end,
  logs = function(msg)
    require("unity-nvim.log").append(msg.entries or {}, msg.dropped or 0)
  end,
  open = function(msg)
    require("unity-nvim").open(msg.file, msg.line, msg.col)
  end,
}

local function dispatch(msg)
  if type(msg) ~= "table" then
    return
  end
  if msg.type == "event" then
    local h = handlers.event[msg.event]
    if h then
      h(msg)
    end
  elseif msg.type == "error" then
    notify("Bridge: " .. tostring(msg.error), vim.log.levels.WARN)
  elseif handlers[msg.type] then
    handlers[msg.type](msg)
  end
end

connect = function()
  if not active or tcp or not M.state.root then
    return
  end
  local ep = require("unity-nvim.project").endpoint(M.state.root)
  if not ep or not ep.port then
    -- Unity closed (or bridge off): look again later
    set_status("disconnected")
    schedule_retry(5000)
    return
  end
  local t = uv.new_tcp()
  tcp = t
  local buffer = ""
  -- callbacks of a replaced/closed handle must not touch the current connection
  local function lost()
    vim.schedule(function()
      if tcp == t then
        on_disconnect()
      end
    end)
  end
  t:connect("127.0.0.1", ep.port, function(err)
    if err then
      lost()
      return
    end
    t:read_start(function(rerr, chunk)
      if rerr or not chunk then
        lost()
        return
      end
      buffer = buffer .. chunk
      while true do
        local nl = buffer:find("\n", 1, true)
        if not nl then
          break
        end
        local line = buffer:sub(1, nl - 1)
        buffer = buffer:sub(nl + 1)
        local ok, msg = pcall(vim.json.decode, line, { luanil = { object = true, array = true } })
        if ok then
          vim.schedule(function()
            if tcp == t then
              dispatch(msg)
            end
          end)
        end
      end
    end)
    t:write(vim.json.encode({
      type = "hello",
      token = ep.token,
      client = "unity-nvim.lua",
      version = VERSION,
      logs = cfg().logs,
      server = vim.v.servername,
    }) .. "\n")
  end)
end

--- Start (or restart) talking to the Unity editor of `root`.
function M.start(root, verbose)
  M.state.root = root
  active = true
  backoff = 250
  if verbose and M.state.connected then
    notify("Already connected")
    return
  end
  if verbose then
    announced[root] = nil
  end

  vim.api.nvim_clear_autocmds({ group = save_group })
  if cfg().refresh_on_save then
    local prefix = require("unity-nvim.project").normalize(root) .. "/"
    vim.api.nvim_create_autocmd("BufWritePost", {
      group = save_group,
      pattern = cfg().refresh_patterns,
      callback = function(ev)
        local file = require("unity-nvim.project").normalize(vim.fn.fnamemodify(ev.file, ":p"))
        if file:sub(1, #prefix) == prefix then
          M.refresh(true)
        end
      end,
    })
  end
  connect()
end

function M.stop()
  active = false
  vim.api.nvim_clear_autocmds({ group = save_group })
  if retry_timer then
    retry_timer:stop()
  end
  close_tcp()
  M.state.connected = false
  set_status("disconnected")
end

---@param cmd string
---@param params? table
---@param cb? fun(ok: boolean, result: any)
function M.request(cmd, params, cb)
  if not M.state.connected then
    notify("Not connected to Unity" .. (M.state.root and " (is the editor open?)" or ""), vim.log.levels.WARN)
    return false
  end
  next_id = next_id + 1
  local msg = vim.tbl_extend("force", params or {}, { id = next_id, cmd = cmd })
  pending[next_id] = cb or function(ok, err)
    if not ok then
      notify(cmd .. ": " .. tostring(err), vim.log.levels.WARN)
    end
  end
  return send(msg)
end

--- Ask Unity to import changed files (and compile). Debounced: saving many files sends one request.
---@param silent? boolean no warning when disconnected (used on save)
function M.refresh(silent)
  if not M.state.connected then
    if not silent then
      M.request("refresh")
    end
    return
  end
  refresh_timer = refresh_timer or uv.new_timer()
  refresh_timer:stop()
  refresh_timer:start(150, 0, vim.schedule_wrap(function()
    if M.state.status ~= "compiling" then
      set_status("refreshing")
    end
    M.request("refresh", nil, function(ok, result)
      if not ok then
        set_status("idle")
        return
      end
      if type(result) == "table" and (result.compiling or result.updating) then
        set_status("compiling")
        return
      end
      -- compilation may still start on the next editor tick; give it a moment
      vim.defer_fn(function()
        if M.state.status == "refreshing" then
          set_status("idle")
        end
      end, 2000)
    end)
  end))
end

function M.show_status()
  local s = M.state
  if not s.root then
    notify("Not inside a Unity project")
    return
  end
  local lines = {
    ("Project: %s"):format(s.root),
    ("Bridge: %s"):format(s.connected and ("connected to Unity " .. (s.unity_version or "?")) or s.status),
  }
  if s.connected then
    lines[#lines + 1] = ("Editor: %s%s"):format(s.unity.playing and "playing" or "edit mode", s.unity.paused and " (paused)" or "")
  end
  if s.last then
    lines[#lines + 1] = ("Last compile (%s): %d error(s), %d warning(s)"):format(s.last.time or "?", s.last.errors or 0, s.last.warnings or 0)
  end
  notify(table.concat(lines, "\n"))
end

return M
