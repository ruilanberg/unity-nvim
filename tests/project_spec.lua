-- nvim --clean --headless -l tests/project_spec.lua   (from the repo root)
-- Vectors shared with Packages/com.ruilanberg.unity-nvim/Tests/Editor/ServerAddressTests.cs
vim.opt.rtp:prepend(vim.fn.getcwd())
local p = require("unity-nvim.project")
local failures = 0
local function eq(got, want, what)
  if got ~= want then
    failures = failures + 1
    io.stdout:write(("FAIL %s: got %s, want %s\n"):format(what, tostring(got), tostring(want)))
  end
end

eq(p.hash("c:/users/usuario/documents/projects/dinx/front-child"), "7da3be4f", "hash windows path")
eq(p.hash("/Users/me/Game Project"), "7f0f5ac1", "hash posix path with space")
eq(p.hash("c:/projetos/ação é"), "a3ba0682", "hash utf-8")
eq(p.hash("a"), "e40c292c", "hash single byte")

eq(p.normalize([[C:\Users\Me\Proj\]], true), "c:/users/me/proj", "normalize windows")
eq(p.normalize("C:/", true), "c:/", "normalize drive root")
eq(p.normalize("/Users/Me/Proj/", false), "/Users/Me/Proj", "normalize posix keeps case")

eq(p.address([[C:\Users\Usuario\Documents\Projects\Dinx\front-child]], true), [[\\.\pipe\unity-nvim-7da3be4f]], "address windows")
eq(p.address("/Users/me/Game Project", false, "/var/folders/x/T/"), "/var/folders/x/T/unity-nvim-7f0f5ac1.sock", "address mac")
eq(p.address("/Users/me/Game Project", false, ""), "/tmp/unity-nvim-7f0f5ac1.sock", "address linux")

io.stdout:write(failures == 0 and "project_spec: OK\n" or ("project_spec: %d failure(s)\n"):format(failures))
os.exit(failures == 0 and 0 or 1)
