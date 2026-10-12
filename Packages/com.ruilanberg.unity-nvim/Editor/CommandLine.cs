using UnityEditor;
using UnityEngine;
using UnityNvim.Editor.Generation;

namespace UnityNvim.Editor
{
    /// <summary>
    /// Batch entry points, e.g.
    /// Unity -batchmode -projectPath &lt;p&gt; -executeMethod UnityNvim.Editor.CommandLine.SyncAll -quit
    /// </summary>
    public static class CommandLine
    {
        public static void SyncAll()
        {
            var generator = new ProjectGenerator();
            generator.Sync();
            Debug.Log("[unity-nvim] generated " + generator.SolutionFile());
        }

        [MenuItem("Assets/Neovim/Regenerate Project Files", priority = 1001)]
        static void Regenerate() => SyncAll();

        // greyed out unless Neovim is the selected editor (it would replace another IDE's project files)
        [MenuItem("Assets/Neovim/Regenerate Project Files", true)]
        static bool CanRegenerate() => NvimCodeEditor.IsSelected;
    }
}
