/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Unity Technologies.
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See THIRD-PARTY-NOTICES.md in the package root.
 *
 *  Modified for unity-nvim (ported from com.unity.ide.visualstudio 2.0.27: AssemblyNameProvider,
 *  FileIOProvider, GUIDProvider). Package lookups return a small PackageLocation instead of
 *  PackageInfo so the generator can be tested with fakes; player assemblies are not supported.
 *--------------------------------------------------------------------------------------------*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UpmSource = UnityEditor.PackageManager.PackageSource;

namespace UnityNvim.Editor.Generation
{
    internal enum ScriptingLanguage
    {
        None,
        CSharp
    }

    /// <summary>Where a "Packages/x" asset path lives on disk and where the package came from.</summary>
    internal sealed class PackageLocation
    {
        public string AssetPath;    // e.g. "Packages/com.foo.bar"
        public string ResolvedPath; // absolute folder on disk
        public PackageSource Source;
    }

    internal interface IAssemblyNameProvider
    {
        string[] ProjectSupportedExtensions { get; }
        string[] BuiltinSupportedExtensions { get; }

        string GetAssemblyNameFromScriptPath(string path);
        IEnumerable<Assembly> GetAssemblies(Func<string, bool> shouldFileBePartOfSolution);
        PackageLocation FindForAssetPath(string assetPath);
        ResponseFileData ParseResponseFile(string responseFilePath, string projectDirectory, ApiCompatibilityLevel apiCompatibilityLevel);
        void ResetPackageInfoCache();
    }

    internal sealed class AssemblyNameProvider : IAssemblyNameProvider
    {
        internal static readonly string AssemblyOutput = @"Temp\bin\Debug\".NormalizePathSeparators();

        readonly Dictionary<string, PackageLocation> m_PackageInfoCache = new Dictionary<string, PackageLocation>();

        public string[] ProjectSupportedExtensions => EditorSettings.projectGenerationUserExtensions ?? Array.Empty<string>();
        public string[] BuiltinSupportedExtensions => EditorSettings.projectGenerationBuiltinExtensions ?? Array.Empty<string>();

        public string GetAssemblyNameFromScriptPath(string path)
        {
            return CompilationPipeline.GetAssemblyNameFromScriptPath(path);
        }

        public IEnumerable<Assembly> GetAssemblies(Func<string, bool> shouldFileBePartOfSolution)
        {
            foreach (var assembly in CompilationPipeline.GetAssemblies(AssembliesType.Editor))
            {
                if (assembly.sourceFiles.Any(shouldFileBePartOfSolution))
                {
                    yield return new Assembly(
                        assembly.name,
                        AssemblyOutput,
                        assembly.sourceFiles,
                        assembly.defines,
                        assembly.assemblyReferences,
                        assembly.compiledAssemblyReferences,
                        assembly.flags,
                        assembly.compilerOptions,
                        assembly.rootNamespace);
                }
            }
        }

        static string ResolvePotentialParentPackageAssetPath(string assetPath)
        {
            const string packagesPrefix = "packages/";
            if (!assetPath.StartsWith(packagesPrefix, StringComparison.OrdinalIgnoreCase))
                return null;

            var followupSeparator = assetPath.IndexOf('/', packagesPrefix.Length);
            if (followupSeparator == -1)
                return assetPath.ToLowerInvariant();

            return assetPath.Substring(0, followupSeparator).ToLowerInvariant();
        }

        public PackageLocation FindForAssetPath(string assetPath)
        {
            var parentPackageAssetPath = ResolvePotentialParentPackageAssetPath(assetPath);
            if (parentPackageAssetPath == null)
                return null;

            if (m_PackageInfoCache.TryGetValue(parentPackageAssetPath, out var cached))
                return cached;

            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(parentPackageAssetPath);
            var result = info == null
                ? null
                : new PackageLocation { AssetPath = info.assetPath, ResolvedPath = info.resolvedPath, Source = Map(info.source) };
            m_PackageInfoCache[parentPackageAssetPath] = result;
            return result;
        }

        static PackageSource Map(UpmSource source)
        {
            switch (source)
            {
                case UpmSource.Embedded: return PackageSource.Embedded;
                case UpmSource.Local: return PackageSource.Local;
                case UpmSource.Registry: return PackageSource.Registry;
                case UpmSource.Git: return PackageSource.Git;
                case UpmSource.BuiltIn: return PackageSource.BuiltIn;
                case UpmSource.LocalTarball: return PackageSource.LocalTarball;
                default: return PackageSource.Unknown;
            }
        }

        public ResponseFileData ParseResponseFile(string responseFilePath, string projectDirectory, ApiCompatibilityLevel apiCompatibilityLevel)
        {
            return CompilationPipeline.ParseResponseFile(
                responseFilePath,
                projectDirectory,
                CompilationPipeline.GetSystemAssemblyDirectories(apiCompatibilityLevel));
        }

        public void ResetPackageInfoCache()
        {
            m_PackageInfoCache.Clear();
        }
    }

    internal interface IFileIO
    {
        bool Exists(string fileName);
        string ReadAllText(string fileName);
        void WriteAllText(string fileName, string content);
    }

    internal sealed class FileIOProvider : IFileIO
    {
        public bool Exists(string fileName) => File.Exists(fileName);
        public string ReadAllText(string fileName) => File.ReadAllText(fileName);
        public void WriteAllText(string fileName, string content) => File.WriteAllText(fileName, content, Encoding.UTF8);
    }

    internal interface IGUIDGenerator
    {
        string ProjectGuid(string projectName, string assemblyName);
        string SolutionGuid(string projectName, ScriptingLanguage scriptingLanguage);
    }

    internal sealed class GUIDProvider : IGUIDGenerator
    {
        public string ProjectGuid(string projectName, string assemblyName) => SolutionGuidGenerator.GuidForProject(projectName + assemblyName);
        public string SolutionGuid(string projectName, ScriptingLanguage scriptingLanguage) => SolutionGuidGenerator.GuidForSolution(projectName, scriptingLanguage);
    }

    /// <summary>Same MD5-based GUIDs as Unity/VS, so switching editors keeps project identities.</summary>
    internal static class SolutionGuidGenerator
    {
        public static string GuidForProject(string projectName)
        {
            return ComputeGuidHashFor(projectName + "salt");
        }

        public static string GuidForSolution(string projectName, ScriptingLanguage language)
        {
            if (language == ScriptingLanguage.CSharp)
            {
                // GUID for a C# class library: http://www.codeproject.com/Reference/720512/List-of-Visual-Studio-Project-Type-GUIDs
                return "FAE04EC0-301F-11D3-BF4B-00C04F79EFBC";
            }

            return ComputeGuidHashFor(projectName);
        }

        static string ComputeGuidHashFor(string input)
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.Default.GetBytes(input));
                var sb = new StringBuilder();
                foreach (var b in hash)
                    sb.Append(b.ToString("x2"));
                var h = sb.ToString();
                return (h.Substring(0, 8) + "-" + h.Substring(8, 4) + "-" + h.Substring(12, 4) + "-" + h.Substring(16, 4) + "-" + h.Substring(20, 12)).ToUpper();
            }
        }
    }
}
