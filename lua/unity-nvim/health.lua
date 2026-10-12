-- :checkhealth unity-nvim
local M = {}

function M.check()
  local h = vim.health
  local project = require("unity-nvim.project")
  local bridge = require("unity-nvim.bridge")

  h.start("unity-nvim")
  if vim.fn.has("nvim-0.10") == 1 then
    h.ok("Neovim " .. tostring(vim.version()))
  else
    h.error("Neovim 0.10+ is required")
  end

  local root = project.find_root()
  if not root then
    h.info("Not inside a Unity project (no Assets/ + ProjectSettings/ProjectVersion.txt above " .. vim.fn.getcwd() .. ")")
    return
  end
  h.ok("Unity project: " .. root)

  local addr = project.address(root)
  if vim.tbl_contains(vim.fn.serverlist(), addr) then
    h.ok("Listening on " .. addr .. " (Unity opens files in this nvim)")
  else
    h.warn("Not listening on " .. addr, { "Another nvim may already serve this project, or `listen = false`." })
  end

  local ep = project.endpoint(root)
  if not ep then
    h.warn("No Library/UnityNvim/endpoint.json", {
      "Open the project in Unity with the com.ruilanberg.unity-nvim package installed",
      "and the bridge enabled (Preferences > External Tools).",
    })
  else
    h.ok(("Unity bridge endpoint: 127.0.0.1:%s (Unity %s, pid %s)"):format(ep.port, ep.unityVersion or "?", ep.pid or "?"))
  end
  if bridge.state.connected then
    h.ok("Connected to the Unity editor")
  else
    h.warn("Not connected (status: " .. bridge.state.status .. ")")
  end

  local sln = root .. "/" .. vim.fs.basename(root) .. ".sln"
  if (vim.uv or vim.loop).fs_stat(sln) then
    h.ok("Solution: " .. sln)
  else
    h.warn("No " .. vim.fs.basename(sln), { "Choose Neovim as External Script Editor in Unity, or Assets > Open C# Project." })
  end
end

return M
