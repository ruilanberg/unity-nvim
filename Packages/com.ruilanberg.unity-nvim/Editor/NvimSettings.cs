using System;
using System.Linq;
using UnityEditor;

namespace UnityNvim.Editor
{
    /// <summary>Which package sources get a .csproj (same idea as the Visual Studio package's flags).</summary>
    [Flags]
    public enum PackageSource
    {
        None = 0,
        Embedded = 1 << 0,
        Local = 1 << 1,
        Registry = 1 << 2,
        Git = 1 << 3,
        BuiltIn = 1 << 4,
        Unknown = 1 << 5,
        LocalTarball = 1 << 6,
    }

    /// <summary>Per-machine settings (EditorPrefs), shared by every project.</summary>
    public static class NvimSettings
    {
        const string Prefix = "UnityNvim.";

        public static readonly string[] DefaultExtensions =
        {
            "cs", "shader", "cginc", "hlsl", "hlsli", "compute", "glsl", "uxml", "uss", "tss", "asmdef", "asmref",
            "json", "xml", "yaml", "txt", "md", "inputactions", "rsp", "editorconfig",
        };

        /// <summary>Real nvim binary used for --server/--listen (the editor path picked in Unity may be a wrapper).</summary>
        public static string NvimPath
        {
            get => EditorPrefs.GetString(Prefix + "NvimPath", "");
            set => EditorPrefs.SetString(Prefix + "NvimPath", value ?? "");
        }

        /// <summary>Terminal preset id (see Launch.TerminalPresets); empty = platform default.</summary>
        public static string Terminal
        {
            get => EditorPrefs.GetString(Prefix + "Terminal", "");
            set => EditorPrefs.SetString(Prefix + "Terminal", value ?? "");
        }

        /// <summary>Command line used by the "custom" preset, with {placeholders}.</summary>
        public static string CustomCommand
        {
            get => EditorPrefs.GetString(Prefix + "CustomCommand", "");
            set => EditorPrefs.SetString(Prefix + "CustomCommand", value ?? "");
        }

        /// <summary>Run nvim through the user's login shell so it gets the full PATH (useful on macOS, where Unity's PATH is minimal).</summary>
        public static bool LoginShell
        {
            get => EditorPrefs.GetBool(Prefix + "LoginShell", UnityEngine.Application.platform == UnityEngine.RuntimePlatform.OSXEditor);
            set => EditorPrefs.SetBool(Prefix + "LoginShell", value);
        }

        /// <summary>Extensions (without dot) opened in nvim; everything else goes to the OS default app.</summary>
        public static string[] Extensions
        {
            get
            {
                var raw = EditorPrefs.GetString(Prefix + "Extensions", "");
                return raw == "" ? DefaultExtensions : Split(raw);
            }
            set => EditorPrefs.SetString(Prefix + "Extensions", value == null ? "" : string.Join(";", value));
        }

        public static PackageSource PackageSources
        {
            get => (PackageSource)EditorPrefs.GetInt(Prefix + "PackageSources", (int)(PackageSource.Embedded | PackageSource.Local));
            set => EditorPrefs.SetInt(Prefix + "PackageSources", (int)value);
        }

        /// <summary>Extra Roslyn analyzer dlls added to every generated project (e.g. Microsoft.Unity.Analyzers.dll).</summary>
        public static string[] ExtraAnalyzers
        {
            get => Split(EditorPrefs.GetString(Prefix + "ExtraAnalyzers", ""));
            set => EditorPrefs.SetString(Prefix + "ExtraAnalyzers", value == null ? "" : string.Join(";", value));
        }

        /// <summary>Add the bundled Microsoft.Unity.Analyzers (suppresses false IDE warnings on Unity messages such as Update()).</summary>
        public static bool UnityAnalyzers
        {
            get => EditorPrefs.GetBool(Prefix + "UnityAnalyzers", true);
            set => EditorPrefs.SetBool(Prefix + "UnityAnalyzers", value);
        }

        /// <summary>Analyzers~/ is ignored by Unity's importer; the dll is only referenced from the generated projects.</summary>
        public static string BundledUnityAnalyzers
        {
            get
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(NvimSettings).Assembly);
                if (info == null)
                    return null;
                var path = System.IO.Path.Combine(info.resolvedPath, "Analyzers~", "Microsoft.Unity.Analyzers.dll");
                return System.IO.File.Exists(path) ? path : null;
            }
        }

        public static string[] AllExtraAnalyzers()
        {
            var bundled = UnityAnalyzers ? BundledUnityAnalyzers : null;
            return bundled == null ? ExtraAnalyzers : new[] { bundled }.Concat(ExtraAnalyzers).ToArray();
        }

        /// <summary>Generate legacy (ToolsVersion 4.0) projects instead of SDK-style.</summary>
        public static bool LegacyProjects
        {
            get => EditorPrefs.GetBool(Prefix + "LegacyProjects", false);
            set => EditorPrefs.SetBool(Prefix + "LegacyProjects", value);
        }

        public static bool BridgeEnabled
        {
            get => EditorPrefs.GetBool(Prefix + "BridgeEnabled", true);
            set => EditorPrefs.SetBool(Prefix + "BridgeEnabled", value);
        }

        /// <summary>Disable editor throttling while nvim is connected, so background refresh/compile react faster.</summary>
        public static bool ResponsiveMode
        {
            get => EditorPrefs.GetBool(Prefix + "ResponsiveMode", false);
            set => EditorPrefs.SetBool(Prefix + "ResponsiveMode", value);
        }

        static string[] Split(string raw) =>
            raw.Split(new[] { ';', ',', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().TrimStart('.'))
                .Where(s => s.Length > 0)
                .ToArray();
    }
}
