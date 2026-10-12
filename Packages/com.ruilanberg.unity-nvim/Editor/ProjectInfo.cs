using System.IO;
using UnityEngine;

namespace UnityNvim.Editor
{
    /// <summary>Project facts captured on the main thread (Application.dataPath can't be read from socket threads).</summary>
    public static class ProjectInfo
    {
        static string root;

        public static string Root => root ?? (root = Directory.GetParent(Application.dataPath).FullName);
        public static string Name => Path.GetFileName(Root);
        public static string SolutionPath => Path.Combine(Root, Name + ".sln");
        public static string NvimAddress => Launch.ServerAddress.For(Root);
        public static string WindowTitle => "unity-nvim: " + Name;

        /// <summary>Call once from the main thread before any background thread needs the values.</summary>
        public static void Capture() => _ = Root;
    }
}
