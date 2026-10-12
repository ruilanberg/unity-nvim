using System.IO;
using System.Linq;
using Unity.CodeEditor;
using UnityEditor;
using UnityNvim.Editor.Generation;
using UnityNvim.Editor.Launch;

namespace UnityNvim.Editor
{
    /// <summary>"Neovim" entry in Preferences > External Tools > External Script Editor.</summary>
    [InitializeOnLoad]
    public sealed class NvimCodeEditor : IExternalCodeEditor
    {
        static readonly ProjectGenerator Generator = new ProjectGenerator();

        static NvimCodeEditor()
        {
            if (!EditorProcess.IsMain)
                return;
            CodeEditor.Register(new NvimCodeEditor());
            // defines change with the build target: rebuild the projects so Roslyn sees the right #if branches
            EditorUserBuildSettings.activeBuildTargetChanged += () =>
            {
                if (IsCurrent)
                    Generator.Sync();
            };
        }

        public static bool IsCurrent => CodeEditor.CurrentEditor is NvimCodeEditor;

        public static ProjectGenerator ProjectGenerator => Generator;

        public CodeEditor.Installation[] Installations =>
            NvimDiscovery.Installations()
                .Select(p => new CodeEditor.Installation { Name = "Neovim (" + p + ")", Path = p })
                .ToArray();

        public bool TryGetInstallationForPath(string editorPath, out CodeEditor.Installation installation)
        {
            if (!NvimDiscovery.IsNvimPath(editorPath))
            {
                installation = default;
                return false;
            }
            installation = new CodeEditor.Installation
            {
                Name = NvimDiscovery.IsRealNvim(editorPath) ? "Neovim" : "Neovim (" + Path.GetFileName(editorPath) + ")",
                Path = editorPath,
            };
            return true;
        }

        public void OnGUI() => SettingsGUI.Draw(Generator);

        public void SyncIfNeeded(string[] addedFiles, string[] deletedFiles, string[] movedFiles, string[] movedFromFiles,
            string[] importedFiles) =>
            Generator.SyncIfNeeded(addedFiles, deletedFiles, movedFiles, movedFromFiles, importedFiles);

        public void SyncAll() => Generator.Sync();

        public void Initialize(string editorInstallationPath)
        {
        }

        public bool OpenProject(string filePath = "", int line = -1, int column = -1)
        {
            var hasFile = !string.IsNullOrEmpty(filePath);
            if (hasFile && !Generator.IsSupportedFile(filePath))
                return false; // Unity opens it with the OS default application

            if (!hasFile)
                Generator.Sync(); // Assets > Open C# Project
            else if (!Generator.HasSolutionBeenGenerated())
                Generator.Sync();

            var file = hasFile ? Path.GetFullPath(filePath) : "";
            return NvimLauncher.Open(CodeEditor.CurrentEditorInstallation, file, line, column);
        }
    }
}
