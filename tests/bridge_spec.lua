-- nvim --clean --headless -l tests/bridge_spec.lua   (from the repo root)
-- Fake Unity bridge server in-process: handshake, compile events → diagnostics/quickfix, logs, open, refresh, reconnect.
vim.opt.rtp:prepend(vim.fn.getcwd())
vim.cmd("runtime plugin/unity-nvim.lua")
local uv = vim.uv or vim.loop

local failures = 0
local function check(cond, what)
  if not cond then
    failures = failures + 1
    io.stdout:write("FAIL " .. what .. "\n")
  else
    io.stdout:write("ok   " .. what .. "\n")
  end
end

-- fake Unity project
local root = vim.fs.normalize(vim.fn.tempname() .. "-proj")
vim.fn.mkdir(root .. "/Assets/Scripts", "p")
vim.fn.mkdir(root .. "/ProjectSettings", "p")
vim.fn.mkdir(root .. "/Library/UnityNvim", "p")
vim.fn.writefile({ "m_EditorVersion: 6000.3.5f2" }, root .. "/ProjectSettings/ProjectVersion.txt")
vim.fn.writefile({ "class A {", "  void F() { x = 1; }", "}" }, root .. "/Assets/Scripts/A.cs")

-- fake bridge server
local server = uv.new_tcp()
server:bind("127.0.0.1", 0)
local port = server:getsockname().port
local token = "secret"
vim.fn.writefile({ vim.json.encode({ port = port, token = token, project = root, projectName = "proj" }) }, root .. "/Library/UnityNvim/endpoint.json")

local clients, received, accepted = {}, {}, 0
local last_compile = vim.NIL
local function send(c, msg) c:write(vim.json.encode(msg) .. "\n") end
server:listen(8, function()
  local c = uv.new_tcp()
  server:accept(c)
  accepted = accepted + 1
  table.insert(clients, c)
  local buf = ""
  c:read_start(function(_, chunk)
    if not chunk then
      return
    end
    buf = buf .. chunk
    for line in buf:gmatch("([^\n]*)\n") do
      local msg = vim.json.decode(line)
      table.insert(received, msg)
      if msg.type == "hello" then
        if msg.token ~= token then
          c:close()
          return
        end
        send(c, { type = "welcome", projectName = "proj", unityVersion = "6000.3.5f2", state = { compiling = false }, lastCompile = last_compile })
      elseif msg.cmd == "refresh" then
        send(c, { type = "response", id = msg.id, ok = true, result = { compiling = true } })
      end
    end
    buf = buf:match("[^\n]*$")
  end)
end)

local function wait(cond, ms)
  return vim.wait(ms or 3000, cond, 20)
end

require("unity-nvim").setup({ notify_exceptions = false })
vim.fn.chdir(root) -- DirChanged attaches
local bridge = require("unity-nvim.bridge")

check(wait(function() return bridge.state.connected end), "connects and authenticates")
check(received[1] and received[1].type == "hello" and received[1].logs == true, "hello carries token and logs=true")
check(bridge.state.status == "idle", "status idle after welcome")
check(vim.tbl_contains(vim.fn.serverlist(), require("unity-nvim.project").address(root)), "listens on the project address")
check(vim.o.titlestring == "unity-nvim: " .. vim.fs.basename(root), "window title set")

-- compile with an error
send(clients[#clients], { type = "event", event = "compileStarted" })
check(wait(function() return bridge.state.status == "compiling" end), "compileStarted → compiling")
local finished = {
  type = "event", event = "compileFinished", errors = 1, warnings = 0, time = "10:00:00",
  messages = { { file = "Assets/Scripts/A.cs", line = 2, col = 15, severity = "error",
    message = "Assets\\Scripts\\A.cs(2,15): error CS0103: The name 'x' does not exist in the current context" } },
}
send(clients[#clients], finished)
check(wait(function() return bridge.state.last and bridge.state.last.errors == 1 end), "compileFinished stored")
local qf = vim.fn.getqflist({ title = 0, items = 0 })
check(qf.title == "Unity compile" and #qf.items == 1 and qf.items[1].lnum == 2 and qf.items[1].col == 15, "quickfix item at 2:15")
check(qf.items[1] and qf.items[1].text == "CS0103: The name 'x' does not exist in the current context", "quickfix text without the path prefix")
local abuf = vim.fn.bufnr(root .. "/Assets/Scripts/A.cs")
local diags = vim.diagnostic.get(abuf, { namespace = require("unity-nvim.diagnostics").namespace })
check(#diags == 1 and diags[1].code == "CS0103" and diags[1].lnum == 1, "diagnostic CS0103 on line 2")
check(require("unity-nvim.statusline").text() == "Unity ✗ 1", "statusline shows the error count")

-- logs
send(clients[#clients], { type = "event", event = "logs", dropped = 0, entries = {
  { type = "Exception", message = "NullReferenceException: boom", stack = "A.F () (at Assets/Scripts/A.cs:2)", time = "10:00:01" },
} })
wait(function() return false end, 100)
require("unity-nvim.log").open()
local lines = vim.api.nvim_buf_get_lines(0, 0, -1, false)
check(lines[1] == "10:00:01 [X] NullReferenceException: boom", "log line rendered")
local f, l = require("unity-nvim.log").location(lines[2])
check(f == root .. "/Assets/Scripts/A.cs" and l == 2, "stack frame location parsed")
vim.cmd("close")

-- open request from Unity
send(clients[#clients], { type = "event", event = "open", file = root .. "/Assets/Scripts/A.cs", line = 3, col = 1 })
check(wait(function() return vim.api.nvim_buf_get_name(0):find("A.cs$") ~= nil end), "open event opens the file")
check(vim.api.nvim_win_get_cursor(0)[1] == 3, "cursor on requested line")

-- refresh request
bridge.refresh()
check(wait(function()
  for _, m in ipairs(received) do if m.cmd == "refresh" then return true end end
end), "refresh request sent")
check(wait(function() return bridge.state.status == "compiling" end), "refresh reply compiling → status compiling")

-- successful compile: domain reload drops the socket, client reconnects and reads lastCompile
local ok_compile = { type = "event", event = "compileFinished", errors = 0, warnings = 0, time = "10:00:05", messages = {} }
send(clients[#clients], ok_compile)
check(wait(function() return bridge.state.status == "reloading" end), "compileFinished(0 errors) → reloading")
ok_compile.type, ok_compile.event = nil, nil
last_compile = ok_compile
clients[#clients]:close()
check(wait(function() return accepted == 2 and bridge.state.connected and bridge.state.status == "idle" end, 5000), "reconnects after the reload")
check(#vim.diagnostic.get(abuf, { namespace = require("unity-nvim.diagnostics").namespace }) == 0, "diagnostics cleared by lastCompile")
check(require("unity-nvim.statusline").text() == "Unity ✓", "statusline back to ✓")

bridge.stop()
server:close()
io.stdout:write(failures == 0 and "bridge_spec: OK\n" or ("bridge_spec: %d failure(s)\n"):format(failures))
os.exit(failures == 0 and 0 or 1)
