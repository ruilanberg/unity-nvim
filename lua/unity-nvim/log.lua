-- Unity console in a buffer (:Unity log). <CR> on a line with "(at Assets/X.cs:12)" or "X.cs(12,5)" jumps there.
local M = {}

local entries = {} ---@type table[]
local bufnr ---@type integer?
local ns = vim.api.nvim_create_namespace("unity_log")
local last_notify = 0

local TAGS = {
  Log = { "I", "DiagnosticInfo" },
  Warning = { "W", "DiagnosticWarn" },
  Error = { "E", "DiagnosticError" },
  Assert = { "A", "DiagnosticError" },
  Exception = { "X", "DiagnosticError" },
}

local function cfg()
  return require("unity-nvim").config
end

local function render(entry)
  local tag = TAGS[entry.type] or { "?", "Comment" }
  local msg = (entry.message or ""):gsub("\r", "")
  local lines = {}
  for i, l in ipairs(vim.split(msg, "\n", { trimempty = true })) do
    lines[#lines + 1] = (i == 1 and ("%s [%s] "):format(entry.time or "", tag[1]) or "             ") .. l
  end
  if #lines == 0 then
    lines[1] = ("%s [%s]"):format(entry.time or "", tag[1])
  end
  for _, l in ipairs(vim.split((entry.stack or ""):gsub("\r", ""), "\n", { trimempty = true })) do
    lines[#lines + 1] = "    " .. l
  end
  return lines, tag[2]
end

local function buf_valid()
  return bufnr and vim.api.nvim_buf_is_valid(bufnr)
end

local function write(new)
  if not buf_valid() then
    return
  end
  local wins = vim.fn.win_findbuf(bufnr)
  local follow = {}
  for _, w in ipairs(wins) do
    follow[w] = vim.api.nvim_win_get_cursor(w)[1] >= vim.api.nvim_buf_line_count(bufnr) - 1
  end
  vim.bo[bufnr].modifiable = true
  local empty = vim.api.nvim_buf_line_count(bufnr) == 1 and vim.api.nvim_buf_get_lines(bufnr, 0, 1, false)[1] == ""
  for _, entry in ipairs(new) do
    local lines, hl = render(entry)
    local start = empty and 0 or vim.api.nvim_buf_line_count(bufnr)
    vim.api.nvim_buf_set_lines(bufnr, empty and 0 or -1, empty and -1 or -1, false, lines)
    empty = false
    -- highlight the "[E]" tag that follows "HH:MM:SS "
    pcall(vim.api.nvim_buf_set_extmark, bufnr, ns, start, 9, { end_col = 12, hl_group = hl })
  end
  -- keep the buffer bounded
  local excess = vim.api.nvim_buf_line_count(bufnr) - cfg().log_max * 3
  if excess > 0 then
    vim.api.nvim_buf_set_lines(bufnr, 0, excess, false, {})
  end
  vim.bo[bufnr].modifiable = false
  vim.bo[bufnr].modified = false
  for w, f in pairs(follow) do
    if f and vim.api.nvim_win_is_valid(w) then
      vim.api.nvim_win_set_cursor(w, { vim.api.nvim_buf_line_count(bufnr), 0 })
    end
  end
end

---@param new table[] {type, message, stack, time}
---@param dropped integer
function M.append(new, dropped)
  if dropped and dropped > 0 then
    new[#new + 1] = { type = "Warning", message = ("(%d log messages dropped)"):format(dropped), time = os.date("%H:%M:%S") }
  end
  for _, e in ipairs(new) do
    entries[#entries + 1] = e
  end
  if #entries > cfg().log_max then
    entries = vim.list_slice(entries, #entries - cfg().log_max + 1)
  end
  write(new)

  if cfg().notify_exceptions and not (buf_valid() and #vim.fn.win_findbuf(bufnr) > 0) then
    for _, e in ipairs(new) do
      if (e.type == "Exception" or e.type == "Error" or e.type == "Assert") and os.time() - last_notify >= 2 then
        last_notify = os.time()
        local first = vim.split(e.message or "", "\n")[1]
        vim.notify(first .. "\n(:Unity log)", vim.log.levels.ERROR, { title = "Unity console" })
        break
      end
    end
  end
end

--- File and line referenced on a log line, absolute when relative to the project.
function M.location(line)
  local file, lnum = line:match("%(at (.-):(%d+)%)")
  local col
  if not file then
    file, lnum, col = line:match("([%w%._%-/\\ ]+%.%a+)%((%d+),(%d+)%)")
  end
  if not file then
    return nil
  end
  file = vim.trim(file):gsub("\\", "/")
  local root = require("unity-nvim").root()
  if root and not (file:match("^%a:/") or file:sub(1, 1) == "/") then
    file = root .. "/" .. file
  end
  return file, tonumber(lnum), tonumber(col) or 1
end

local function jump()
  local file, lnum, col = M.location(vim.api.nvim_get_current_line())
  if not file then
    return
  end
  if not (vim.uv or vim.loop).fs_stat(file) then
    vim.notify("Not found: " .. file, vim.log.levels.WARN, { title = "Unity" })
    return
  end
  vim.cmd("wincmd p")
  require("unity-nvim").open(file, lnum, col)
end

function M.clear()
  entries = {}
  if buf_valid() then
    vim.bo[bufnr].modifiable = true
    vim.api.nvim_buf_set_lines(bufnr, 0, -1, false, {})
    vim.bo[bufnr].modifiable = false
  end
end

function M.open()
  if not buf_valid() then
    bufnr = vim.api.nvim_create_buf(false, true)
    vim.api.nvim_buf_set_name(bufnr, "unity://log")
    vim.bo[bufnr].filetype = "unitylog"
    vim.bo[bufnr].bufhidden = "hide"
    vim.bo[bufnr].modifiable = false
    vim.keymap.set("n", "<CR>", jump, { buffer = bufnr, desc = "Go to source" })
    vim.keymap.set("n", "c", M.clear, { buffer = bufnr, desc = "Clear Unity log" })
    vim.keymap.set("n", "q", "<cmd>close<cr>", { buffer = bufnr, desc = "Close" })
    write(entries)
  end
  local wins = vim.fn.win_findbuf(bufnr)
  if #wins > 0 then
    vim.api.nvim_set_current_win(wins[1])
  else
    vim.cmd("botright 12split")
    vim.api.nvim_win_set_buf(0, bufnr)
    vim.wo.wrap = false
    vim.wo.number = false
    vim.wo.relativenumber = false
  end
  vim.api.nvim_win_set_cursor(0, { vim.api.nvim_buf_line_count(bufnr), 0 })
  if not require("unity-nvim.bridge").state.connected then
    vim.notify("Not connected to Unity: logs appear once the editor is open", vim.log.levels.WARN, { title = "Unity" })
  end
end

return M
