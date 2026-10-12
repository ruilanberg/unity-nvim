-- Unity project detection and the per-project nvim address.
-- The hash must match Packages/com.ruilanberg.unity-nvim/Editor/Launch/ServerAddress.cs (shared test vectors).
local M = {}

local is_win = vim.fn.has("win32") == 1
local uv = vim.uv or vim.loop

--- Unity project root containing `path` (a folder with Assets/ and ProjectSettings/ProjectVersion.txt), or nil.
---@param path? string file or directory; default: cwd
---@return string?
function M.find_root(path)
  path = path and path ~= "" and path or uv.cwd()
  local dir = vim.fs.normalize(vim.fn.fnamemodify(path, ":p"))
  if not uv.fs_stat(dir) or uv.fs_stat(dir).type ~= "directory" then
    dir = vim.fs.dirname(dir)
  end
  while dir do
    if uv.fs_stat(dir .. "/ProjectSettings/ProjectVersion.txt") and uv.fs_stat(dir .. "/Assets") then
      return dir
    end
    local parent = vim.fs.dirname(dir)
    if not parent or parent == dir then
      return nil
    end
    dir = parent
  end
end

--- Same rules as ServerAddress.Normalize: forward slashes, no trailing slash, lower-case on Windows.
function M.normalize(path, windows)
  if windows == nil then
    windows = is_win
  end
  local p = path:gsub("\\", "/")
  while #p > 1 and p:sub(-1) == "/" and not (windows and #p == 3 and p:sub(2, 2) == ":") do
    p = p:sub(1, -2)
  end
  return windows and p:lower() or p
end

local TWO32 = 4294967296

--- FNV-1a 32-bit over the bytes, as 8 hex digits.
function M.hash(s)
  local h = 2166136261
  for i = 1, #s do
    h = bit.bxor(h, s:byte(i)) % TWO32
    -- h * 16777619 (= 2^24 + 403) mod 2^32, exact in doubles
    h = ((h * 403) % TWO32 + bit.lshift(h, 24) % TWO32) % TWO32
  end
  return string.format("%08x", h)
end

--- --listen address Unity uses for this project.
---@param root string absolute project root
---@param windows? boolean
---@param tmpdir? string
function M.address(root, windows, tmpdir)
  if windows == nil then
    windows = is_win
  end
  local h = M.hash(M.normalize(root, windows))
  if windows then
    return [[\\.\pipe\unity-nvim-]] .. h
  end
  tmpdir = tmpdir or vim.env.TMPDIR
  local dir = (tmpdir and tmpdir ~= "") and tmpdir:gsub("/+$", "") or "/tmp"
  return dir .. "/unity-nvim-" .. h .. ".sock"
end

--- Parsed Library/UnityNvim/endpoint.json, or nil.
function M.endpoint(root)
  local f = io.open(root .. "/Library/UnityNvim/endpoint.json", "r")
  if not f then
    return nil
  end
  local raw = f:read("*a")
  f:close()
  local ok, data = pcall(vim.json.decode, raw)
  return ok and type(data) == "table" and data or nil
end

return M
