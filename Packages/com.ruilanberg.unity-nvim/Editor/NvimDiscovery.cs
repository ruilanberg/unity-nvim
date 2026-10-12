using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityNvim.Editor.Launch;

namespace UnityNvim.Editor
{
    /// <summary>Finds nvim installations and decides which editor paths this package claims.</summary>
    public static class NvimDiscovery
    {
        static string[] cached;

        static IEnumerable<string> Candidates(HostOS os)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            switch (os)
            {
                case HostOS.Windows:
                    var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    return new[]
                    {
                        Path.Combine(programFiles, "Neovim", "bin", "nvim.exe"),
                        Path.Combine(local, "Programs", "Neovim", "bin", "nvim.exe"),
                        Path.Combine(home, "scoop", "shims", "nvim.exe"),
                        @"C:\ProgramData\chocolatey\bin\nvim.exe",
                        @"C:\tools\neovim\nvim-win64\bin\nvim.exe",
                    };
                case HostOS.Mac:
                    return new[] { "/opt/homebrew/bin/nvim", "/usr/local/bin/nvim", Path.Combine(home, ".local/bin/nvim"), "/opt/local/bin/nvim" };
                default:
                    return new[]
                    {
                        "/usr/bin/nvim", "/usr/local/bin/nvim", Path.Combine(home, ".local/bin/nvim"), "/snap/bin/nvim",
                        "/home/linuxbrew/.linuxbrew/bin/nvim", "/opt/nvim-linux-x86_64/bin/nvim", "/opt/nvim/bin/nvim",
                    };
            }
        }

        /// <summary>Installed nvim binaries (PATH first), cached for the domain's lifetime.</summary>
        public static string[] Installations(bool refresh = false)
        {
            if (cached != null && !refresh)
                return cached;
            var os = TerminalPresets.Current;
            var windows = os == HostOS.Windows;
            var found = new List<string>();
            var onPath = ProcessUtil.Which("nvim", windows);
            if (onPath != null)
                found.Add(Path.GetFullPath(onPath));
            foreach (var c in Candidates(os))
                if (File.Exists(c) && !found.Any(f => SamePath(f, c, windows)))
                    found.Add(c);
            return cached = found.ToArray();
        }

        static bool SamePath(string a, string b, bool windows) =>
            string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

        /// <summary>Editor paths we answer for in "External Script Editor": nvim/neovide binaries and wrappers named *nvim*.</summary>
        public static bool IsNvimPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            return name.StartsWith("nvim") || name.StartsWith("neovide") || name.Contains("nvim");
        }

        /// <summary>True for an nvim binary we can call with --server/--listen (not a .cmd/.sh wrapper or neovide).</summary>
        public static bool IsRealNvim(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;
            var file = Path.GetFileName(path).ToLowerInvariant();
            return file == "nvim" || file == "nvim.exe";
        }

        /// <summary>The nvim binary to use: setting → selected editor path → first installation → "nvim".</summary>
        public static string Nvim(string editorPath)
        {
            var setting = NvimSettings.NvimPath;
            if (IsRealNvim(setting))
                return setting;
            if (IsRealNvim(editorPath))
                return editorPath;
            return Installations().FirstOrDefault() ?? "nvim";
        }
    }
}
