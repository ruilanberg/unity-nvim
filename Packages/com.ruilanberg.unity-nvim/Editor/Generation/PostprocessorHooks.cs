/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Unity Technologies.
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See THIRD-PARTY-NOTICES.md in the package root.
 *
 *  Modified for unity-nvim (ported from com.unity.ide.visualstudio 2.0.27: TypeCacheHelper and
 *  the AssetPostprocessor callbacks of ProjectGeneration).
 *--------------------------------------------------------------------------------------------*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;

namespace UnityNvim.Editor.Generation
{
    /// <summary>
    /// The AssetPostprocessor hooks other packages rely on:
    /// OnPreGeneratingCSProjectFiles, OnGeneratedCSProject, OnGeneratedSlnSolution, OnGeneratedCSProjectFiles.
    /// </summary>
    internal static class PostprocessorHooks
    {
        static IEnumerable<MethodInfo> Callbacks(string name)
        {
            return TypeCache
                .GetTypesDerivedFrom<AssetPostprocessor>()
                .Select(t => t.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                .Where(m => m != null);
        }

        /// <returns>true when some postprocessor already generated the projects (generation is then skipped).</returns>
        public static bool OnPreGeneratingCSProjectFiles()
        {
            var result = false;
            foreach (var method in Callbacks(nameof(OnPreGeneratingCSProjectFiles)))
            {
                var value = method.Invoke(null, Array.Empty<object>());
                if (method.ReturnType == typeof(bool))
                    result |= (bool)value;
            }

            return result;
        }

        public static void OnGeneratedCSProjectFiles()
        {
            foreach (var method in Callbacks(nameof(OnGeneratedCSProjectFiles)))
                method.Invoke(null, Array.Empty<object>());
        }

        public static string OnGeneratedCSProject(string path, string content) => Chain(nameof(OnGeneratedCSProject), path, content);

        public static string OnGeneratedSlnSolution(string path, string content) => Chain(nameof(OnGeneratedSlnSolution), path, content);

        static string Chain(string name, string path, string content)
        {
            foreach (var method in Callbacks(name))
            {
                var value = method.Invoke(null, new object[] { path, content });
                // chain content updates between invocations
                if (method.ReturnType == typeof(string))
                    content = (string)value;
            }

            return content;
        }
    }
}
