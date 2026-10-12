# unity-nvim

Neovim as Unity's script editor — both ways.

- **Unity → Neovim**: "Neovim" in *Preferences → External Tools*. Double-clicking a script or a Console line opens `file:line:col` in **one nvim per project** (reused and brought to the front), starting it in your terminal of choice when needed. Generates `.sln`/`.csproj` for the Roslyn language server.
- **Neovim → Unity**: saving a `.cs` makes Unity import and compile **in the background** (no need to focus the editor); compiler errors land in diagnostics and the quickfix list; `:Unity play`, `:Unity stop`, Unity's Console in a buffer, and a statusline component.

The repo holds both halves: the Neovim plugin at the root and the Unity package in `Packages/com.ruilanberg.unity-nvim`.

## Install

### Unity (2021.3+, tested on Unity 6.3)

*Window → Package Manager → + → Install package from git URL*:

```
https://github.com/ruilanberg/unity-nvim.git?path=/Packages/com.ruilanberg.unity-nvim#v0.1.1
```

Then *Edit → Preferences → External Tools → External Script Editor → Neovim*.

Safe for shared projects: on machines where another editor (Visual Studio, Rider…) is selected the package only adds "Neovim" to that list — no bridge, no project generation, menu disabled.

### Neovim (0.10+)

lazy.nvim:

```lua
{
  "ruilanberg/unity-nvim",
  event = "VeryLazy",
  opts = {},
  keys = {
    { "<leader>Up", "<cmd>Unity play<cr>", desc = "Unity play" },
    { "<leader>Us", "<cmd>Unity stop<cr>", desc = "Unity stop" },
    { "<leader>Ur", "<cmd>Unity refresh<cr>", desc = "Unity refresh" },
    { "<leader>Ul", "<cmd>Unity log<cr>", desc = "Unity console" },
  },
},
-- statusline (optional)
{
  "nvim-lualine/lualine.nvim",
  opts = function(_, opts)
    table.insert(opts.sections.lualine_x, 1, require("unity-nvim.statusline").lualine())
  end,
},
```

For C# you also want a Roslyn client such as [roslyn.nvim](https://github.com/seblyng/roslyn.nvim); it picks up the generated `<Project>.sln`.

## How it works

| | |
|---|---|
| **Opening files** | Unity sends the file to the connected nvim over the bridge (instant). Without the bridge it calls `nvim --server <addr> --remote-expr …`, and when no nvim is running it starts one with `--listen <addr>` in the configured terminal. `<addr>` is `\\.\pipe\unity-nvim-<hash>` on Windows and `$TMPDIR/unity-nvim-<hash>.sock` elsewhere (hash of the project path), so a manually started nvim inside the project is reused too. |
| **Project files** | Ported from Unity's `com.unity.ide.visualstudio` generator: SDK-style projects (`netstandard2.1`) with Unity's analyzers and source generators, `LangVersion` taken from Unity's compiler (so the LSP rejects C# Unity can't compile), and the bundled [Microsoft.Unity.Analyzers](https://github.com/microsoft/Microsoft.Unity.Analyzers) (no more "unused `Update()`" warnings). Only changed `.csproj` files are rewritten. |
| **Bridge** | The editor listens on `127.0.0.1` (random port + token in `Library/UnityNvim/endpoint.json`). JSON lines: `refresh`, `recompile`, `play`, `stop`, `pause`, `status`; events for compile start/finish (with messages), play mode, console logs and `open`. The plugin reconnects after every domain reload. |

### Terminals

| OS | Presets |
|---|---|
| Windows | Windows Terminal (default), WezTerm, Alacritty, Neovide |
| macOS | Terminal.app (default), iTerm2, WezTerm, kitty, Alacritty, Neovide |
| Linux | kitty, WezTerm, Alacritty, GNOME Terminal, Konsole, xterm, Neovide (first one installed) |

Or a custom command with placeholders: `{nvim_argv}` (nvim and all its arguments), `{nvim}`, `{addr}`, `{file}`, `{line}`, `{col}`, `{root}`, `{title}` — e.g. `wezterm start --cwd {root} -- {nvim_argv}`. On macOS nvim starts through your login shell by default, since Unity's `PATH` is minimal there.

## Neovim usage

| Command | |
|---|---|
| `:Unity play` / `stop` / `pause` | play mode |
| `:Unity refresh` | import + compile (also automatic on save) |
| `:Unity recompile` | force a script recompile |
| `:Unity log` | Unity Console (`<CR>` jumps to the source, `c` clears, `q` closes) |
| `:Unity status` | connection, play mode, last compile |
| `:Unity connect` / `disconnect` | |
| `:checkhealth unity-nvim` | |

<details>
<summary>Options (defaults)</summary>

```lua
require("unity-nvim").setup({
  auto_connect = true,      -- connect to the editor when nvim is inside a Unity project
  listen = true,            -- serve the project's address so Unity reuses this nvim
  set_title = true,         -- "unity-nvim: <Project>" window title (used to focus the window)
  refresh_on_save = true,
  refresh_patterns = { "*.cs", "*.shader", "*.hlsl", "*.hlsli", "*.cginc", "*.compute", "*.uxml", "*.uss", "*.asmdef", "*.asmref" },
  diagnostics = true,       -- Unity compiler messages not already reported by the LSP
  quickfix = true,          -- compiler errors in the quickfix list ("all" to include warnings)
  open_quickfix = false,
  notify = true,            -- compile failed / back to OK
  logs = true,              -- receive Console logs
  log_max = 2000,
  notify_exceptions = true, -- vim.notify exceptions while the log buffer is hidden
})
```
</details>

## Unity preferences

Under *External Tools* with Neovim selected: nvim binary (auto-detected), terminal, file extensions sent to nvim (others open with the OS default app), which package sources get a `.csproj`, legacy-style projects, Microsoft.Unity.Analyzers, extra analyzers, and the bridge (on/off, status, *Responsive while connected* to disable editor throttling).

*Assets → Neovim → Regenerate Project Files*, or in batch mode:

```
Unity -batchmode -projectPath <project> -executeMethod UnityNvim.Editor.CommandLine.SyncAll -quit
```

## Notes

- Tested end to end on Windows 10 with Unity 6000.3. macOS and Linux paths (presets, sockets, quoting) are covered by unit tests only.
- Bringing the window to the front: Windows via the window title, macOS by activating the terminal app, Linux with `wmctrl`/`xdotool` when installed (not on Wayland).
- Play/stop and compiles work with Unity in the background; Unity may still throttle a background editor, so things can take a second or two.

## Development

```sh
nvim --clean --headless -l tests/project_spec.lua
nvim --clean --headless -l tests/bridge_spec.lua
# Unity EditMode tests (TestProject~ references the package with file: and marks it testable)
Unity -batchmode -projectPath TestProject~ -runTests -testPlatform EditMode -testResults results.xml
```

## License

MIT. The project generator is adapted from `com.unity.ide.visualstudio` (MIT) and `Microsoft.Unity.Analyzers` is redistributed under MIT — see [THIRD-PARTY-NOTICES.md](Packages/com.ruilanberg.unity-nvim/THIRD-PARTY-NOTICES.md).
