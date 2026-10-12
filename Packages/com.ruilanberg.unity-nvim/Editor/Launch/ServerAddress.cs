using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace UnityNvim.Editor.Launch
{
    /// <summary>
    /// One nvim per Unity project, addressed by a short hash of the project path.
    /// Must stay identical to lua/unity-nvim/project.lua (shared test vectors in Tests/Editor/ServerAddressTests.cs and tests/project_spec.lua).
    /// </summary>
    public static class ServerAddress
    {
        public static bool IsWindows => Application.platform == RuntimePlatform.WindowsEditor;

        /// <summary>Absolute, forward slashes, no trailing slash; lower-case on Windows (case-insensitive paths, drive letter case varies).</summary>
        public static string Normalize(string path, bool windows)
        {
            var p = path.Replace('\\', '/');
            while (p.Length > 1 && p.EndsWith("/", StringComparison.Ordinal) && !(windows && p.Length == 3 && p[1] == ':'))
                p = p.Substring(0, p.Length - 1);
            return windows ? p.ToLowerInvariant() : p;
        }

        /// <summary>FNV-1a 32-bit over the UTF-8 bytes, as 8 lower-case hex digits.</summary>
        public static string Hash(string normalizedPath)
        {
            uint h = 2166136261;
            foreach (var b in Encoding.UTF8.GetBytes(normalizedPath))
            {
                h ^= b;
                h *= 16777619;
            }
            return h.ToString("x8");
        }

        public static string For(string projectRoot) =>
            For(Path.GetFullPath(projectRoot), IsWindows, Environment.GetEnvironmentVariable("TMPDIR"));

        /// <param name="projectRoot">absolute project path</param>
        public static string For(string projectRoot, bool windows, string tmpDir)
        {
            var hash = Hash(Normalize(projectRoot, windows));
            if (windows)
                return @"\\.\pipe\unity-nvim-" + hash;
            var dir = string.IsNullOrEmpty(tmpDir) ? "/tmp" : tmpDir.TrimEnd('/');
            return dir + "/unity-nvim-" + hash + ".sock";
        }
    }
}
