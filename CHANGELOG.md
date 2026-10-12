# Changelog

## 0.1.1 — 2026-10-12

- Inert unless Neovim is the selected External Script Editor: the bridge, project generation and the *Assets → Neovim* menu stay off otherwise, so teammates on Visual Studio/Rider are unaffected by the package being in the shared manifest. Switching editors turns the bridge on/off within ~2 s.
- Use `IActiveBuildTargetChanged` instead of the obsolete `activeBuildTargetChanged` event.

## 0.1.0 — 2026-10-12

First release.

- Unity package: Neovim as External Script Editor; one nvim per project (bridge, `--remote-expr` or a new terminal window); terminal presets for Windows, macOS and Linux plus custom commands; window focus.
- SDK-style `.sln`/`.csproj` generation (ported from `com.unity.ide.visualstudio`) with Unity's LangVersion, analyzers, source generators and bundled Microsoft.Unity.Analyzers 1.28.0.
- Bridge: refresh/recompile, play/stop/pause, compile events with messages, console logs, open requests; works with the editor in the background.
- Neovim plugin: auto-connect, refresh on save, diagnostics + quickfix, `:Unity` commands, console buffer, lualine component, `:checkhealth unity-nvim`.
