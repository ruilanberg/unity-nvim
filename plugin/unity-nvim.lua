if vim.g.loaded_unity_nvim then
  return
end
vim.g.loaded_unity_nvim = true

vim.api.nvim_create_user_command("Unity", function(args)
  require("unity-nvim").command(args)
end, {
  nargs = "?",
  complete = function(lead)
    return require("unity-nvim").complete(lead)
  end,
  desc = "Unity: play | stop | pause | refresh | recompile | status | log | connect | disconnect",
})

-- Attach when nvim starts inside a Unity project and whenever the cwd changes.
-- Outside Unity projects this costs a couple of fs_stat calls.
local group = vim.api.nvim_create_augroup("unity_nvim", { clear = true })
vim.api.nvim_create_autocmd("DirChanged", {
  group = group,
  callback = function()
    require("unity-nvim").attach()
  end,
})
if vim.v.vim_did_enter == 1 then
  -- lazy-loaded: let setup() (the plugin's config) run first
  vim.schedule(function()
    require("unity-nvim").attach()
  end)
else
  vim.api.nvim_create_autocmd("VimEnter", {
    group = group,
    once = true,
    callback = function()
      require("unity-nvim").attach()
    end,
  })
end
