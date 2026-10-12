using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnityNvim.Editor.Launch
{
    public enum HostOS
    {
        Windows,
        Mac,
        Linux,
    }

    /// <summary>
    /// How to start nvim in a new window. Args are tokens with placeholders (see TemplateExpander):
    /// {nvim} {addr} {file} {line} {col} {root} {title} expand inside a token; {nvim_argv} / {nvim_args} expand to
    /// several tokens; {nvim_cmd_as} is a "cd root && exec nvim ..." shell command escaped for an AppleScript string.
    /// </summary>
    public sealed class TerminalPreset
    {
        public string Id;
        public string Name;
        public HostOS OS;
        /// <summary>Candidates for the executable: absolute paths or names looked up in PATH.</summary>
        public string[] Exe;
        public string[] Args;
        /// <summary>macOS application to activate for focus.</summary>
        public string MacApp;
    }

    public static class TerminalPresets
    {
        public const string Custom = "custom";

        public static HostOS Current
        {
            get
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsEditor: return HostOS.Windows;
                    case RuntimePlatform.OSXEditor: return HostOS.Mac;
                    default: return HostOS.Linux;
                }
            }
        }

        public static readonly TerminalPreset[] All =
        {
            // Windows
            new TerminalPreset
            {
                Id = "wt", Name = "Windows Terminal", OS = HostOS.Windows, Exe = new[] { "wt.exe" },
                Args = new[] { "-w", "new", "nt", "--title", "{title}", "--suppressApplicationTitle", "-d", "{root}", "{nvim_argv}" },
            },
            new TerminalPreset
            {
                Id = "wezterm", Name = "WezTerm", OS = HostOS.Windows,
                Exe = new[] { "wezterm.exe", @"C:\Program Files\WezTerm\wezterm.exe" },
                Args = new[] { "start", "--cwd", "{root}", "--", "{nvim_argv}" },
            },
            new TerminalPreset
            {
                Id = "alacritty", Name = "Alacritty", OS = HostOS.Windows,
                Exe = new[] { "alacritty.exe", @"C:\Program Files\Alacritty\alacritty.exe" },
                Args = new[] { "--working-directory", "{root}", "--title", "{title}", "-e", "{nvim_argv}" },
            },
            new TerminalPreset
            {
                Id = "neovide", Name = "Neovide", OS = HostOS.Windows,
                Exe = new[] { "neovide.exe", @"C:\Program Files\Neovide\neovide.exe" },
                Args = new[] { "--neovim-bin", "{nvim}", "--", "{nvim_args}" },
            },

            // macOS (Unity started from the Hub has a minimal PATH, so absolute app paths come first)
            new TerminalPreset
            {
                Id = "terminal", Name = "Terminal.app", OS = HostOS.Mac, Exe = new[] { "/usr/bin/osascript" }, MacApp = "Terminal",
                Args = new[]
                {
                    "-e", "tell application \"Terminal\" to do script \"{nvim_cmd_as}\"",
                    "-e", "tell application \"Terminal\" to activate",
                },
            },
            new TerminalPreset
            {
                Id = "iterm", Name = "iTerm2", OS = HostOS.Mac, Exe = new[] { "/usr/bin/osascript" }, MacApp = "iTerm",
                Args = new[]
                {
                    "-e", "tell application \"iTerm\" to create window with default profile command \"/bin/sh -c \" & quoted form of \"{nvim_cmd_as}\"",
                    "-e", "tell application \"iTerm\" to activate",
                },
            },
            new TerminalPreset
            {
                Id = "wezterm", Name = "WezTerm", OS = HostOS.Mac, MacApp = "WezTerm",
                Exe = new[] { "/Applications/WezTerm.app/Contents/MacOS/wezterm", "/opt/homebrew/bin/wezterm", "wezterm" },
                Args = new[] { "start", "--cwd", "{root}", "--", "{nvim_argv}" },
            },
            new TerminalPreset
            {
                Id = "kitty", Name = "kitty", OS = HostOS.Mac, MacApp = "kitty",
                Exe = new[] { "/Applications/kitty.app/Contents/MacOS/kitty", "/opt/homebrew/bin/kitty", "kitty" },
                Args = new[] { "--directory", "{root}", "--title", "{title}", "{nvim_argv}" },
            },
            new TerminalPreset
            {
                Id = "alacritty", Name = "Alacritty", OS = HostOS.Mac, MacApp = "Alacritty",
                Exe = new[] { "/Applications/Alacritty.app/Contents/MacOS/alacritty", "/opt/homebrew/bin/alacritty", "alacritty" },
                Args = new[] { "--working-directory", "{root}", "--title", "{title}", "-e", "{nvim_argv}" },
            },
            new TerminalPreset
            {
                Id = "neovide", Name = "Neovide", OS = HostOS.Mac, MacApp = "Neovide",
                Exe = new[] { "/Applications/Neovide.app/Contents/MacOS/neovide", "/opt/homebrew/bin/neovide", "neovide" },
                Args = new[] { "--neovim-bin", "{nvim}", "--", "{nvim_args}" },
            },

            // Linux (first one found is the default)
            new TerminalPreset
            {
                Id = "kitty", Name = "kitty", OS = HostOS.Linux, Exe = new[] { "kitty" },
                Args = new[] { "--directory", "{root}", "--title", "{title}", "{nvim_argv}" },
            },
            new TerminalPreset
            {
                Id = "wezterm", Name = "WezTerm", OS = HostOS.Linux, Exe = new[] { "wezterm" },
                Args = new[] { "start", "--cwd", "{root}", "--", "{nvim_argv}" },
            },
            new TerminalPreset
            {
                Id = "alacritty", Name = "Alacritty", OS = HostOS.Linux, Exe = new[] { "alacritty" },
                Args = new[] { "--working-directory", "{root}", "--title", "{title}", "-e", "{nvim_argv}" },
            },
            new TerminalPreset
            {
                Id = "gnome-terminal", Name = "GNOME Terminal", OS = HostOS.Linux, Exe = new[] { "gnome-terminal" },
                Args = new[] { "--working-directory={root}", "--title={title}", "--", "{nvim_argv}" },
            },
            new TerminalPreset
            {
                Id = "konsole", Name = "Konsole", OS = HostOS.Linux, Exe = new[] { "konsole" },
                Args = new[] { "--workdir", "{root}", "-p", "tabtitle={title}", "-e", "{nvim_argv}" },
            },
            new TerminalPreset
            {
                Id = "xterm", Name = "xterm", OS = HostOS.Linux, Exe = new[] { "xterm" },
                Args = new[] { "-T", "{title}", "-e", "{nvim_argv}" },
            },
            new TerminalPreset
            {
                Id = "neovide", Name = "Neovide", OS = HostOS.Linux, Exe = new[] { "neovide" },
                Args = new[] { "--neovim-bin", "{nvim}", "--", "{nvim_args}" },
            },
        };

        public static IEnumerable<TerminalPreset> For(HostOS os) => All.Where(p => p.OS == os);

        public static string ResolveExe(TerminalPreset preset, bool windows) =>
            preset.Exe.Select(e => ProcessUtil.Which(e, windows)).FirstOrDefault(e => e != null);

        /// <summary>Preset from settings, or the platform default (Linux: first installed one).</summary>
        public static TerminalPreset Selected(string id, HostOS os)
        {
            var list = For(os).ToList();
            var chosen = list.FirstOrDefault(p => p.Id == id);
            if (chosen != null)
                return chosen;
            if (os == HostOS.Linux)
                return list.FirstOrDefault(p => ResolveExe(p, false) != null) ?? list[0];
            return list[0];
        }
    }
}
