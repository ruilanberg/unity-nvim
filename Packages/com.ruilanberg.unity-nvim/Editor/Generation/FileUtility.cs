/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Unity Technologies.
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See THIRD-PARTY-NOTICES.md in the package root.
 *
 *  Modified for unity-nvim (ported from com.unity.ide.visualstudio 2.0.27).
 *--------------------------------------------------------------------------------------------*/
using System;
using System.IO;

namespace UnityNvim.Editor.Generation
{
    internal static class FileUtility
    {
        public const char WinSeparator = '\\';
        public const char UnixSeparator = '/';

        public static string NormalizePathSeparators(this string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            if (Path.DirectorySeparatorChar == WinSeparator)
                path = path.Replace(UnixSeparator, WinSeparator);
            if (Path.DirectorySeparatorChar == UnixSeparator)
                path = path.Replace(WinSeparator, UnixSeparator);

            return path.Replace(string.Concat(WinSeparator, WinSeparator), WinSeparator.ToString());
        }

        public static string NormalizeWindowsToUnix(this string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            return path.Replace(WinSeparator, UnixSeparator);
        }

        /// <summary>Absolute path; relative paths are resolved against <paramref name="baseDirectory"/> (not the process cwd).</summary>
        public static string MakeAbsolutePath(this string path, string baseDirectory)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;
            return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(baseDirectory, path));
        }

        internal static void SafeDelete(string file)
        {
            try
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            catch (IOException)
            {
                // ignore
            }
            catch (UnauthorizedAccessException)
            {
                // ignore
            }
        }
    }
}
