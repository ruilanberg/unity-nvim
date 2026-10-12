/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Unity Technologies.
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See THIRD-PARTY-NOTICES.md in the package root.
 *
 *  Modified for unity-nvim (ported from com.unity.ide.visualstudio 2.0.27): no VSTU flavoring,
 *  LangVersion defaults to the Unity compiler's version instead of "latest".
 *--------------------------------------------------------------------------------------------*/
using System;

namespace UnityNvim.Editor.Generation
{
    internal sealed class ProjectProperties
    {
        public string ProjectGuid { get; set; } = string.Empty;
        public string LangVersion { get; set; } = ProjectGenerator.DefaultLangVersion;
        public string AssemblyName { get; set; } = string.Empty;
        public string RootNamespace { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;

        // Analyzers
        public string[] Analyzers { get; set; } = Array.Empty<string>();
        public string RulesetPath { get; set; } = string.Empty;
        public string AnalyzerConfigPath { get; set; } = string.Empty;
        // Source generators
        public string[] AdditionalFilePaths { get; set; } = Array.Empty<string>();

        // RSP alterable
        public string[] Defines { get; set; } = Array.Empty<string>();
        public bool Unsafe { get; set; }
    }

    /// <summary>Generator settings; by default read from <see cref="NvimSettings"/> on every sync (tests pass fixed values).</summary>
    internal sealed class GeneratorOptions
    {
        public PackageSource PackageSources = PackageSource.Embedded | PackageSource.Local;
        public string[] ExtraAnalyzers = Array.Empty<string>();
        public bool LegacyProjects;
        public string[] Extensions = Array.Empty<string>();
        public bool InvokePostprocessors = true;

        public static GeneratorOptions FromSettings()
        {
            return new GeneratorOptions
            {
                PackageSources = NvimSettings.PackageSources,
                ExtraAnalyzers = NvimSettings.AllExtraAnalyzers(),
                LegacyProjects = NvimSettings.LegacyProjects,
                Extensions = NvimSettings.Extensions,
            };
        }
    }
}
