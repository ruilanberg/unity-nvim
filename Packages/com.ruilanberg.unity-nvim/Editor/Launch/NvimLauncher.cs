using System;
using System.IO;
using UnityEngine;
using UnityNvim.Editor.Bridge;

namespace UnityNvim.Editor.Launch
{
    /// <summary>Open file:line:col in this project's nvim, starting one when needed.</summary>
    public static class NvimLauncher
    {
        /// <param name="file">absolute path, or empty to just bring up nvim at the project root</param>
        public static bool Open(string editorPath, string file, int line, int column)
        {
            line = Math.Max(1, line);
            column = Math.Max(1, column);
            var os = TerminalPresets.Current;
            var windows = os == HostOS.Windows;
            var preset = TerminalPresets.Selected(NvimSettings.Terminal, os);
            var title = ProjectInfo.WindowTitle;

            // 1. connected nvim (instant, no process)
            if (BridgeServer.TrySendOpen(file, line, column))
            {
                WindowFocus.Focus(title, os, preset.MacApp);
                return true;
            }

            var nvim = NvimDiscovery.Nvim(editorPath);
            var address = ProjectInfo.NvimAddress;

            // 2. nvim already listening on the project's address
            try
            {
                if (NvimRemote.TryOpen(nvim, address, file, line, column, windows))
                {
                    WindowFocus.Focus(title, os, preset.MacApp);
                    return true;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[unity-nvim] could not reach nvim at " + address + ": " + e.Message);
            }

            // 3. new window
            NvimRemote.RemoveStaleSocket(nvim, address, windows);
            return Launch(preset, nvim, address, file, line, column, windows);
        }

        static bool Launch(TerminalPreset preset, string nvim, string address, string file, int line, int column, bool windows)
        {
            var ctx = new LaunchContext
            {
                Nvim = nvim,
                Address = address,
                File = file,
                Line = line,
                Column = column,
                Root = ProjectInfo.Root,
                Title = ProjectInfo.WindowTitle,
                LoginShell = !windows && NvimSettings.LoginShell ? (Environment.GetEnvironmentVariable("SHELL") ?? "/bin/zsh") : null,
            };

            string exe;
            string[] tokens;
            if (NvimSettings.Terminal == TerminalPresets.Custom)
            {
                var parts = ProcessUtil.SplitCommandLine(NvimSettings.CustomCommand);
                if (parts.Count == 0)
                {
                    Debug.LogError("[unity-nvim] the custom terminal command is empty (Preferences > External Tools)");
                    return false;
                }
                exe = ProcessUtil.Which(parts[0], windows) ?? parts[0];
                tokens = parts.GetRange(1, parts.Count - 1).ToArray();
            }
            else
            {
                exe = TerminalPresets.ResolveExe(preset, windows);
                if (exe == null)
                {
                    Debug.LogError("[unity-nvim] " + preset.Name + " not found (" + string.Join(", ", preset.Exe) +
                                   "). Pick another terminal in Preferences > External Tools.");
                    return false;
                }
                tokens = preset.Args;
            }

            var args = TemplateExpander.Expand(tokens, ctx);
            try
            {
                ProcessUtil.Start(exe, args, Directory.Exists(ctx.Root) ? ctx.Root : null, windows);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[unity-nvim] failed to start " + exe + ": " + e.Message);
                return false;
            }
        }
    }
}
