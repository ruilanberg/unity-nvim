using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityNvim.Editor.Bridge;
using UnityNvim.Editor.Generation;
using UnityNvim.Editor.Launch;

namespace UnityNvim.Editor
{
    /// <summary>Settings shown under the "External Script Editor" dropdown when Neovim is selected.</summary>
    static class SettingsGUI
    {
        static readonly (PackageSource flag, string label)[] Sources =
        {
            (PackageSource.Embedded, "Embedded packages"),
            (PackageSource.Local, "Local packages"),
            (PackageSource.Registry, "Registry packages"),
            (PackageSource.Git, "Git packages"),
            (PackageSource.BuiltIn, "Built-in packages"),
            (PackageSource.LocalTarball, "Local tarball"),
            (PackageSource.Unknown, "Packages from unknown sources"),
        };

        static bool showGeneration = true;
        static bool showBridge = true;

        public static void Draw(ProjectGenerator generator)
        {
            var os = TerminalPresets.Current;
            var windows = os == HostOS.Windows;

            // ---- nvim ----
            var nvim = NvimDiscovery.Nvim(Unity.CodeEditor.CodeEditor.CurrentEditorInstallation);
            using (new EditorGUILayout.HorizontalScope())
            {
                var path = EditorGUILayout.TextField(new GUIContent("nvim binary", "Real nvim used for --listen/--server. Empty = detect."),
                    NvimSettings.NvimPath);
                if (path != NvimSettings.NvimPath)
                    NvimSettings.NvimPath = path;
                if (GUILayout.Button("Browse", GUILayout.Width(70)))
                {
                    var picked = EditorUtility.OpenFilePanel("nvim binary", "", windows ? "exe" : "");
                    if (!string.IsNullOrEmpty(picked))
                        NvimSettings.NvimPath = picked;
                }
            }
            EditorGUILayout.LabelField(" ", "Using: " + nvim, EditorStyles.miniLabel);

            // ---- terminal ----
            var presets = TerminalPresets.For(os).ToList();
            var ids = presets.Select(p => p.Id).Concat(new[] { TerminalPresets.Custom }).ToArray();
            var names = presets.Select(p => p.Name + (TerminalPresets.ResolveExe(p, windows) == null ? " (not found)" : ""))
                .Concat(new[] { "Custom command" }).ToArray();
            var current = TerminalPresets.Selected(NvimSettings.Terminal, os).Id;
            if (NvimSettings.Terminal == TerminalPresets.Custom)
                current = TerminalPresets.Custom;
            var index = EditorGUILayout.Popup(new GUIContent("Open in", "Where a new nvim starts when none is running for this project."),
                Array.IndexOf(ids, current), names);
            if (index >= 0 && ids[index] != current)
                NvimSettings.Terminal = ids[index];
            if (NvimSettings.Terminal == TerminalPresets.Custom)
            {
                NvimSettings.CustomCommand = EditorGUILayout.TextField("Command", NvimSettings.CustomCommand);
                EditorGUILayout.HelpBox(
                    "Placeholders: {nvim_argv} (nvim + all its arguments), {nvim} {addr} {file} {line} {col} {root} {title}.\n" +
                    "Example: wezterm start --cwd {root} -- {nvim_argv}", MessageType.None);
            }
            if (!windows)
                NvimSettings.LoginShell = EditorGUILayout.Toggle(
                    new GUIContent("Use login shell", "Start nvim through $SHELL -l so it gets your PATH (Unity's PATH is minimal on macOS)."),
                    NvimSettings.LoginShell);

            var ext = EditorGUILayout.TextField(new GUIContent("File extensions", "Opened in nvim; others go to the OS default app. ';'-separated."),
                string.Join(";", NvimSettings.Extensions));
            if (ext != string.Join(";", NvimSettings.Extensions))
                NvimSettings.Extensions = ext.Split(';');

            EditorGUILayout.Space();

            // ---- project generation ----
            showGeneration = EditorGUILayout.Foldout(showGeneration, "Generate .csproj files for", true);
            if (showGeneration)
            {
                EditorGUI.indentLevel++;
                var flags = NvimSettings.PackageSources;
                foreach (var (flag, label) in Sources)
                {
                    var on = EditorGUILayout.Toggle(label, (flags & flag) != 0);
                    flags = on ? flags | flag : flags & ~flag;
                }
                if (flags != NvimSettings.PackageSources)
                    NvimSettings.PackageSources = flags;

                NvimSettings.LegacyProjects = EditorGUILayout.Toggle(
                    new GUIContent("Legacy-style projects", "ToolsVersion 4.0 projects instead of SDK-style."), NvimSettings.LegacyProjects);

                NvimSettings.UnityAnalyzers = EditorGUILayout.Toggle(
                    new GUIContent("Microsoft.Unity.Analyzers", "Bundled analyzers that understand Unity (e.g. no 'unused Update()' warnings)."),
                    NvimSettings.UnityAnalyzers);

                var analyzers = EditorGUILayout.TextField(
                    new GUIContent("Extra analyzers", "';'-separated .dll paths added to every project (e.g. Microsoft.Unity.Analyzers.dll)."),
                    string.Join(";", NvimSettings.ExtraAnalyzers));
                if (analyzers != string.Join(";", NvimSettings.ExtraAnalyzers))
                    NvimSettings.ExtraAnalyzers = analyzers.Split(';');
                EditorGUI.indentLevel--;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Regenerate project files", GUILayout.Width(180)))
                    generator.Sync();
                if (GUILayout.Button("Open project in nvim", GUILayout.Width(150)))
                    NvimLauncher.Open(Unity.CodeEditor.CodeEditor.CurrentEditorInstallation, "", 1, 1);
            }

            EditorGUILayout.Space();

            // ---- bridge ----
            showBridge = EditorGUILayout.Foldout(showBridge, "Neovim → Unity bridge", true);
            if (!showBridge)
                return;
            EditorGUI.indentLevel++;
            var enabled = EditorGUILayout.Toggle(new GUIContent("Enabled", "Lets the unity-nvim Lua plugin refresh/compile, play/stop and read errors and logs."),
                NvimSettings.BridgeEnabled);
            if (enabled != NvimSettings.BridgeEnabled)
            {
                NvimSettings.BridgeEnabled = enabled;
                if (enabled)
                    BridgeHost.Enable();
                else
                    BridgeHost.Disable();
            }
            NvimSettings.ResponsiveMode = EditorGUILayout.Toggle(
                new GUIContent("Responsive while connected", "No editor throttling while nvim is connected, so background compiles start sooner."),
                NvimSettings.ResponsiveMode);
            var clients = BridgeServer.ConnectedClients.Length;
            EditorGUILayout.LabelField("Status", BridgeServer.Running
                ? "127.0.0.1:" + BridgeServer.Port + " — " + clients + (clients == 1 ? " nvim connected" : " nvims connected")
                : "off");
            EditorGUI.indentLevel--;
        }
    }
}
