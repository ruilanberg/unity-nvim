-- Unity compiler messages → diagnostics (namespace "unity") and quickfix.
local M = {}

local ns = vim.api.nvim_create_namespace("unity")
local QF_TITLE = "Unity compile"

--- "Assets/A.cs(10,5): error CS0103: The name 'x'..." → "CS0103", "The name 'x'..."
local function split_message(text)
  local rest = text:match("^.-%(%d+,%d+%)%s*:%s*(.*)$") or text
  local code, msg = rest:match("^%a+%s+(%u%u%d+)%s*:%s*(.*)$")
  return code, (msg or rest):gsub("\r", "")
end

local function absolute(root, file)
  if file == "" then
    return nil
  end
  file = file:gsub("\\", "/")
  if not (file:match("^%a:/") or file:sub(1, 1) == "/") then
    file = root .. "/" .. file
  end
  return vim.fs.normalize(file)
end

--- Same code already reported by another source (the LSP) on that line?
local function covered(buf, lnum, code)
  if not code or not vim.api.nvim_buf_is_loaded(buf) then
    return false
  end
  for _, d in ipairs(vim.diagnostic.get(buf, { lnum = lnum })) do
    if d.namespace ~= ns and tostring(d.code) == code then
      return true
    end
  end
  return false
end

---@param root string
---@param result { errors: integer, warnings: integer, messages: table[] }
function M.apply(root, result)
  local cfg = require("unity-nvim").config
  local by_buf = {} ---@type table<integer, vim.Diagnostic[]>
  local qf = {}

  for _, m in ipairs(result.messages or {}) do
    local file = absolute(root, m.file or "")
    local code, text = split_message(m.message or "")
    local is_error = m.severity == "error"
    if file then
      local lnum = math.max((m.line or 1) - 1, 0)
      local col = math.max((m.col or 1) - 1, 0)
      if is_error or cfg.quickfix == "all" then
        qf[#qf + 1] = { filename = file, lnum = lnum + 1, col = col + 1, type = is_error and "E" or "W", text = (code and code .. ": " or "") .. text }
      end
      if cfg.diagnostics then
        local buf = vim.fn.bufadd(file)
        if not covered(buf, lnum, code) then
          by_buf[buf] = by_buf[buf] or {}
          table.insert(by_buf[buf], {
            lnum = lnum,
            col = col,
            severity = is_error and vim.diagnostic.severity.ERROR or vim.diagnostic.severity.WARN,
            message = text,
            code = code,
            source = "unity",
          })
        end
      end
    end
  end

  vim.diagnostic.reset(ns)
  for buf, diags in pairs(by_buf) do
    vim.diagnostic.set(ns, buf, diags)
  end

  if cfg.quickfix then
    local current = vim.fn.getqflist({ title = 0 }).title
    if #qf > 0 then
      vim.fn.setqflist({}, current == QF_TITLE and "r" or " ", { title = QF_TITLE, items = qf })
      if cfg.open_quickfix then
        vim.cmd("botright copen")
        vim.cmd("wincmd p")
      end
    elseif current == QF_TITLE then
      vim.fn.setqflist({}, "r", { title = QF_TITLE, items = {} })
      if cfg.open_quickfix then
        vim.cmd("cclose")
      end
    end
  end

  if cfg.notify then
    local errors, warnings = result.errors or 0, result.warnings or 0
    if errors > 0 then
      vim.notify(("Compile failed: %d error(s)%s"):format(errors, cfg.quickfix and " — :copen" or ""), vim.log.levels.ERROR, { title = "Unity" })
    elseif M.last_errors and M.last_errors > 0 then
      vim.notify("Compile OK" .. (warnings > 0 and (" (%d warning(s))"):format(warnings) or ""), vim.log.levels.INFO, { title = "Unity" })
    end
    M.last_errors = errors
  end
end

M.namespace = ns
M._split_message = split_message

return M
