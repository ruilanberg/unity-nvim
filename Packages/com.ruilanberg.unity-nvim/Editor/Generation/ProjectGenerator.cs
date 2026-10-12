/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Unity Technologies.
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See THIRD-PARTY-NOTICES.md in the package root.
 *
 *  Modified for unity-nvim (ported from com.unity.ide.visualstudio 2.0.27 ProjectGeneration):
 *  - SDK-style projects by default (legacy as an option), always a classic .sln;
 *  - analyzers/source generators/ruleset/editorconfig always emitted (no Visual Studio needed);
 *  - LangVersion from Unity's compiler options instead of "latest";
 *  - no VSTU flavoring, .vsconfig, player projects, `None` asset items or external .sln projects.
 *--------------------------------------------------------------------------------------------*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor.Compilation;
using UnityEngine;

namespace UnityNvim.Editor.Generation
{
    /// <summary>Generates &lt;Project&gt;.sln and one .csproj per Unity assembly for the Roslyn language server.</summary>
    public sealed class ProjectGenerator
    {
        // Same newline on all platforms, for consistent output.
        internal const string NewLine = "\r\n";
        internal const string DefaultLangVersion = "9.0";

        const string SolutionProjectEntryTemplate = @"Project(""{{{0}}}"") = ""{1}"", ""{2}"", ""{{{3}}}""{4}EndProject";

#if UNITY_EDITOR_WIN
        static readonly Regex InvalidCharactersRegexPattern = new Regex(@"\?|&|\*|""|<|>|\||#|%|\^|;", RegexOptions.Compiled);
#else
        static readonly Regex InvalidCharactersRegexPattern = new Regex(@"\?|&|\*|""|<|>|\||#|%|\^|;|:", RegexOptions.Compiled);
#endif

        public string ProjectDirectory { get; }

        readonly string m_ProjectName;
        readonly IAssemblyNameProvider m_AssemblyNameProvider;
        readonly IFileIO m_FileIO;
        readonly IGUIDGenerator m_GUIDGenerator;
        readonly Func<GeneratorOptions> m_OptionsProvider;

        GeneratorOptions m_Options;
        HashSet<string> m_ProjectExtensions;
        bool m_WroteAnything;

        public ProjectGenerator() : this(Directory.GetParent(Application.dataPath).FullName)
        {
        }

        public ProjectGenerator(string projectDirectory)
            : this(projectDirectory, new AssemblyNameProvider(), new FileIOProvider(), new GUIDProvider(), GeneratorOptions.FromSettings)
        {
        }

        internal ProjectGenerator(string projectDirectory, IAssemblyNameProvider assemblyNameProvider, IFileIO fileIO,
            IGUIDGenerator guidGenerator, Func<GeneratorOptions> options)
        {
            ProjectDirectory = projectDirectory.NormalizeWindowsToUnix().TrimEnd('/');
            m_ProjectName = Path.GetFileName(ProjectDirectory);
            m_AssemblyNameProvider = assemblyNameProvider;
            m_FileIO = fileIO;
            m_GUIDGenerator = guidGenerator;
            m_OptionsProvider = options;
            Refresh();
        }

        ProjectWriter Writer => m_Options.LegacyProjects ? ProjectWriter.Legacy : ProjectWriter.Sdk;

        void Refresh()
        {
            m_Options = m_OptionsProvider() ?? new GeneratorOptions();

            m_ProjectExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dll", "asmdef", "additionalfile" };
            foreach (var extension in m_AssemblyNameProvider.ProjectSupportedExtensions)
                m_ProjectExtensions.Add(extension.TrimStart('.'));
            foreach (var extension in m_AssemblyNameProvider.BuiltinSupportedExtensions)
                m_ProjectExtensions.Add(extension.TrimStart('.'));
        }

        // ---------------------------------------------------------------- public API

        /// <summary>Full regeneration of the solution and every project.</summary>
        public void Sync()
        {
            Refresh();
            m_AssemblyNameProvider.ResetPackageInfoCache();

            var alreadyGenerated = m_Options.InvokePostprocessors && PostprocessorHooks.OnPreGeneratingCSProjectFiles();
            if (!alreadyGenerated)
                GenerateAndWriteSolutionAndProjects();

            if (m_Options.InvokePostprocessors)
                PostprocessorHooks.OnGeneratedCSProjectFiles();
        }

        /// <summary>
        /// Regenerates the .sln and the projects of the assemblies touched by the given files, if any of them matters.
        /// Same arguments as IExternalCodeEditor.SyncIfNeeded. Returns true if a file was written.
        /// </summary>
        public bool SyncIfNeeded(string[] addedFiles, string[] deletedFiles, string[] movedFiles, string[] movedFromFiles, string[] importedFiles)
        {
            var affected = (addedFiles ?? Array.Empty<string>())
                .Union(deletedFiles ?? Array.Empty<string>())
                .Union(movedFiles ?? Array.Empty<string>())
                .Union(movedFromFiles ?? Array.Empty<string>())
                .ToArray();
            var reimported = importedFiles ?? Array.Empty<string>();
            return SyncIfNeeded(affected, reimported);
        }

        internal bool SyncIfNeeded(ICollection<string> affected, ICollection<string> reimported)
        {
            Refresh();

            if (!affected.Any(ShouldFileBePartOfSolution) && !reimported.Any(ShouldSyncOnReimportedAsset))
                return false;

            m_WroteAnything = false;

            var assemblies = m_AssemblyNameProvider.GetAssemblies(ShouldFileBePartOfSolution);
            var allProjectAssemblies = RelevantAssemblies(assemblies).ToList();
            SyncSolution(allProjectAssemblies);

            var affectedAndReimported = new HashSet<string>(affected.Concat(reimported)
                .Select(asset => m_AssemblyNameProvider.GetAssemblyNameFromScriptPath(asset))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Split(new[] { ".dll" }, StringSplitOptions.RemoveEmptyEntries)[0]));

            foreach (var assembly in allProjectAssemblies)
            {
                if (affectedAndReimported.Contains(assembly.name))
                    SyncProject(assembly, ParseResponseFileData(assembly));
            }

            return m_WroteAnything;
        }

        public bool HasSolutionBeenGenerated() => m_FileIO.Exists(SolutionFile());

        public string SolutionFile()
        {
            return Path.Combine(ProjectDirectory.NormalizePathSeparators(), $"{InvalidCharactersRegexPattern.Replace(m_ProjectName, "_")}.sln");
        }

        /// <summary>Files that should open in nvim: project files plus the extensions configured in the preferences.</summary>
        public bool IsSupportedFile(string path)
        {
            var extension = GetExtensionWithoutDot(path);
            return IsProjectFile(path)
                || extension == "asmref"
                || m_Options.Extensions.Any(e => string.Equals(e.TrimStart('.'), extension, StringComparison.OrdinalIgnoreCase));
        }

        // ---------------------------------------------------------------- filtering

        static bool ShouldSyncOnReimportedAsset(string asset)
        {
            var extension = Path.GetExtension(asset);
            return extension == ".dll" || extension == ".asmdef";
        }

        bool IsProjectFile(string path) => m_ProjectExtensions.Contains(GetExtensionWithoutDot(path));

        bool ShouldFileBePartOfSolution(string file)
        {
            // Exclude files coming from packages whose source is filtered out in the preferences.
            if (IsInternalizedPackagePath(file))
                return false;

            return IsProjectFile(file);
        }

        internal bool IsInternalizedPackagePath(string path)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(path.Trim()))
                return false;

            var package = m_AssemblyNameProvider.FindForAssetPath(path.NormalizeWindowsToUnix());
            if (package == null)
                return false;

            return (m_Options.PackageSources & package.Source) == 0;
        }

        static string GetExtensionWithoutDot(string path)
        {
            if (!Path.HasExtension(path))
                return path;

            return Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        }

        static ScriptingLanguage ScriptingLanguageFor(Assembly assembly)
        {
            var files = assembly.sourceFiles;
            if (files.Length == 0)
                return ScriptingLanguage.None;

            return GetExtensionWithoutDot(files[0]) == "cs" ? ScriptingLanguage.CSharp : ScriptingLanguage.None;
        }

        static IEnumerable<Assembly> RelevantAssemblies(IEnumerable<Assembly> assemblies)
        {
            return assemblies.Where(i => ScriptingLanguage.CSharp == ScriptingLanguageFor(i));
        }

        // ---------------------------------------------------------------- generation

        void GenerateAndWriteSolutionAndProjects()
        {
            // Only assemblies with source files we actually want in the solution
            // (this also filters out dlls coming from .asmdef files in filtered packages).
            var assemblies = RelevantAssemblies(m_AssemblyNameProvider.GetAssemblies(ShouldFileBePartOfSolution)).ToList();

            SyncSolution(assemblies);

            foreach (var assembly in assemblies)
                SyncProject(assembly, ParseResponseFileData(assembly));
        }

        ResponseFileData[] ParseResponseFileData(Assembly assembly)
        {
            var responseFiles = assembly.compilerOptions?.ResponseFiles ?? Array.Empty<string>();
            var result = new List<ResponseFileData>();

            foreach (var responseFile in responseFiles)
            {
                var data = m_AssemblyNameProvider.ParseResponseFile(responseFile, ProjectDirectory, assembly.compilerOptions.ApiCompatibilityLevel);
                if (data == null)
                    continue;

                foreach (var error in data.Errors ?? Array.Empty<string>())
                    Debug.LogError($"{responseFile} Parse Error : {error}");

                result.Add(data);
            }

            return result.ToArray();
        }

        void SyncProject(Assembly assembly, ResponseFileData[] responseFileData)
        {
            var path = ProjectFile(assembly);
            var contents = ProjectText(assembly, responseFileData);
            if (m_Options.InvokePostprocessors)
                contents = PostprocessorHooks.OnGeneratedCSProject(path, contents);
            SyncFileIfNotChanged(path, contents);
        }

        void SyncSolution(IEnumerable<Assembly> assemblies)
        {
            var path = SolutionFile();
            var contents = SolutionText(assemblies);
            if (m_Options.InvokePostprocessors)
                contents = PostprocessorHooks.OnGeneratedSlnSolution(path, contents);
            SyncFileIfNotChanged(path, contents);
        }

        void SyncFileIfNotChanged(string filename, string newContents)
        {
            try
            {
                if (m_FileIO.Exists(filename) && newContents == m_FileIO.ReadAllText(filename))
                    return;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            m_FileIO.WriteAllText(filename, newContents);
            m_WroteAnything = true;
        }

        internal string ProjectFile(Assembly assembly) => Path.Combine(ProjectDirectory.NormalizePathSeparators(), $"{assembly.name}.csproj");

        internal string ProjectText(Assembly assembly, ResponseFileData[] responseFileData)
        {
            var sb = new StringBuilder();
            Writer.AppendHeader(ProjectPropertiesFor(assembly, responseFileData), sb);

            var dllReferences = new List<string>();

            sb.Append(@"  <ItemGroup>").Append(NewLine);
            foreach (var file in assembly.sourceFiles)
            {
                if (!IsProjectFile(file))
                    continue;

                if (GetExtensionWithoutDot(file) != "dll")
                    IncludeCompile(sb, file);
                else
                    dllReferences.Add(EscapedRelativePathFor(file, out _));
            }
            sb.Append(@"  </ItemGroup>").Append(NewLine);

            // References: Unity's compiled references, response-file references, dlls among the sources and
            // output dlls of referenced assemblies we don't generate a project for.
            var responseRefs = responseFileData.SelectMany(x => x.FullPathReferences ?? Array.Empty<string>());
            var internalAssemblyReferences = assembly.assemblyReferences
                .Where(i => !i.sourceFiles.Any(ShouldFileBePartOfSolution))
                .Select(i => i.outputPath);
            var allReferences = assembly.compiledAssemblyReferences
                .Union(responseRefs)
                .Union(dllReferences)
                .Union(internalAssemblyReferences);

            sb.Append(@"  <ItemGroup>").Append(NewLine);
            foreach (var reference in allReferences)
            {
                var fullReference = Path.IsPathRooted(reference) ? reference : Path.Combine(ProjectDirectory, reference);
                AppendReference(fullReference, sb);
            }
            sb.Append(@"  </ItemGroup>").Append(NewLine);

            var projectReferences = assembly.assemblyReferences.Where(i => i.sourceFiles.Any(ShouldFileBePartOfSolution)).ToList();
            if (projectReferences.Count > 0)
            {
                sb.Append(@"  <ItemGroup>").Append(NewLine);
                foreach (var reference in projectReferences)
                    Writer.AppendProjectReference(sb, reference.name, ProjectGuid(reference.name));
                sb.Append(@"  </ItemGroup>").Append(NewLine);
            }

            Writer.AppendFooter(sb);
            return sb.ToString();
        }

        void IncludeCompile(StringBuilder sb, string asset)
        {
            var filename = EscapedRelativePathFor(asset, out var package);

            sb.Append(@"    <Compile Include=""").Append(filename);
            if (Path.IsPathRooted(filename) && package != null)
            {
                // Outside the Unity project, inside a package: show it under the package-relative path
                var linkPath = XmlFilename(SkipPathPrefix(asset.NormalizePathSeparators(), package.AssetPath.NormalizePathSeparators()));
                sb.Append(@""">").Append(NewLine);
                sb.Append("      <Link>").Append(linkPath).Append("</Link>").Append(NewLine);
                sb.Append("    </Compile>").Append(NewLine);
            }
            else
            {
                sb.Append(@""" />").Append(NewLine);
            }
        }

        void AppendReference(string fullReference, StringBuilder sb)
        {
            var escapedFullPath = EscapedRelativePathFor(fullReference, out _);
            sb.Append(@"    <Reference Include=""").Append(Path.GetFileNameWithoutExtension(escapedFullPath)).Append(@""">").Append(NewLine);
            sb.Append("      <HintPath>").Append(escapedFullPath).Append("</HintPath>").Append(NewLine);
            sb.Append("      <Private>False</Private>").Append(NewLine);
            sb.Append("    </Reference>").Append(NewLine);
        }

        ProjectProperties ProjectPropertiesFor(Assembly assembly, ResponseFileData[] responseFileData)
        {
            var properties = new ProjectProperties
            {
                ProjectGuid = ProjectGuid(assembly.name),
                LangVersion = GetLangVersion(assembly, responseFileData),
                AssemblyName = assembly.name,
                RootNamespace = assembly.rootNamespace ?? string.Empty,
                OutputPath = assembly.outputPath,
                Defines = (assembly.defines ?? Array.Empty<string>())
                    .Concat(responseFileData.SelectMany(x => x.Defines ?? Array.Empty<string>()))
                    .Distinct()
                    .ToArray(),
                Unsafe = (assembly.compilerOptions?.AllowUnsafeCode ?? false) | responseFileData.Any(x => x.Unsafe),
            };

            SetAnalyzerAndSourceGeneratorProperties(assembly, responseFileData, properties);
            return properties;
        }

        /// <summary>
        /// The C# version Unity compiles with (a csc.rsp -langversion wins). Never "latest": the language server
        /// would then accept code that Unity can't compile.
        /// </summary>
        internal static string GetLangVersion(Assembly assembly, ResponseFileData[] responseFileData)
        {
            var fromResponseFile = GetOtherArguments(responseFileData, "langversion").FirstOrDefault();
            if (!string.IsNullOrEmpty(fromResponseFile))
                return fromResponseFile;

            var unity = assembly.compilerOptions?.LanguageVersion;
            if (string.IsNullOrEmpty(unity) || string.Equals(unity, "latest", StringComparison.OrdinalIgnoreCase))
                return DefaultLangVersion;

            return unity;
        }

        static IEnumerable<string> GetOtherArguments(ResponseFileData[] responseFileData, string name)
        {
            var lines = responseFileData
                .SelectMany(x => x.OtherArguments ?? Array.Empty<string>())
                .Where(l => !string.IsNullOrEmpty(l))
                .Select(l => l.Trim())
                .Where(l => l.StartsWith("/") || l.StartsWith("-"));

            foreach (var argument in lines)
            {
                var index = argument.IndexOf(":", StringComparison.Ordinal);
                if (index == -1)
                    continue;

                var key = argument.Substring(1, index - 1).Trim();
                if (name != key)
                    continue;

                if (argument.Length <= index)
                    continue;

                yield return argument.Substring(index + 1).Trim();
            }
        }

        /// <summary>
        /// Analyzers and source generators (Unity's own, csc.rsp and the extra ones from the preferences), ruleset,
        /// editorconfig and additional files. Unlike the Visual Studio package, this doesn't depend on an IDE install.
        /// </summary>
        void SetAnalyzerAndSourceGeneratorProperties(Assembly assembly, ResponseFileData[] responseFileData, ProjectProperties properties)
        {
            var analyzers = new List<string>();
            var additionalFilePaths = new List<string>();
            var rulesetPath = string.Empty;
            var analyzerConfigPath = string.Empty;
            var compilerOptions = assembly.compilerOptions;

            if (compilerOptions != null)
            {
                analyzers.AddRange(compilerOptions.RoslynAnalyzerDllPaths ?? Array.Empty<string>());
                rulesetPath = compilerOptions.RoslynAnalyzerRulesetPath ?? string.Empty;

#if UNITY_2022_2_OR_NEWER
                additionalFilePaths.AddRange(compilerOptions.RoslynAdditionalFilePaths ?? Array.Empty<string>());
                analyzerConfigPath = compilerOptions.AnalyzerConfigPath ?? string.Empty;
#else
                // Introduced in a 2021.3 patch release; not present in early 2021.3.x
                var type = compilerOptions.GetType();
                additionalFilePaths.AddRange(type.GetProperty("RoslynAdditionalFilePaths")?.GetValue(compilerOptions) as string[] ?? Array.Empty<string>());
                analyzerConfigPath = type.GetProperty("AnalyzerConfigPath")?.GetValue(compilerOptions) as string ?? string.Empty;
#endif
            }

            analyzers.AddRange(GetOtherArguments(responseFileData, "analyzer"));
            analyzers.AddRange(GetOtherArguments(responseFileData, "a"));
            analyzers.AddRange(m_Options.ExtraAnalyzers ?? Array.Empty<string>());
            additionalFilePaths.AddRange(GetOtherArguments(responseFileData, "additionalfile"));

            properties.RulesetPath = ToNormalizedPath(rulesetPath);
            properties.Analyzers = ToNormalizedPaths(analyzers);
            properties.AnalyzerConfigPath = ToNormalizedPath(analyzerConfigPath);
            properties.AdditionalFilePaths = ToNormalizedPaths(additionalFilePaths);
        }

        string ToNormalizedPath(string path) => path.MakeAbsolutePath(ProjectDirectory).NormalizePathSeparators();

        string[] ToNormalizedPaths(IEnumerable<string> values)
        {
            return values
                .Where(a => !string.IsNullOrEmpty(a))
                .Select(ToNormalizedPath)
                .Distinct()
                .ToArray();
        }

        // ---------------------------------------------------------------- solution

        internal string SolutionText(IEnumerable<Assembly> assemblies)
        {
            const string fileversion = "12.00";
            const string vsversion = "15";

            var projects = RelevantAssemblies(assemblies).ToList();
            var projectEntries = string.Join(NewLine, projects.Select(assembly => string.Format(
                SolutionProjectEntryTemplate,
                m_GUIDGenerator.SolutionGuid(m_ProjectName, ScriptingLanguageFor(assembly)),
                assembly.name,
                Path.GetFileName(ProjectFile(assembly)),
                ProjectGuid(assembly.name),
                NewLine)));
            var projectConfigurations = string.Join(NewLine, projects.Select(assembly => ProjectActiveConfigurations(ProjectGuid(assembly.name))));

            const string solutionText =
                "" + NewLine
                + "Microsoft Visual Studio Solution File, Format Version {0}" + NewLine
                + "# Visual Studio {1}" + NewLine
                + "{2}" + NewLine
                + "Global" + NewLine
                + "\tGlobalSection(SolutionConfigurationPlatforms) = preSolution" + NewLine
                + "\t\tDebug|Any CPU = Debug|Any CPU" + NewLine
                + "\t\tRelease|Any CPU = Release|Any CPU" + NewLine
                + "\tEndGlobalSection" + NewLine
                + "\tGlobalSection(ProjectConfigurationPlatforms) = postSolution" + NewLine
                + "{3}" + NewLine
                + "\tEndGlobalSection" + NewLine
                + "\tGlobalSection(SolutionProperties) = preSolution" + NewLine
                + "\t\tHideSolutionNode = FALSE" + NewLine
                + "\tEndGlobalSection" + NewLine
                + "EndGlobal" + NewLine
                + "";

            return string.Format(solutionText, fileversion, vsversion, projectEntries, projectConfigurations);
        }

        static string ProjectActiveConfigurations(string projectGuid)
        {
            const string template =
                "\t\t{{{0}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU" + NewLine
                + "\t\t{{{0}}}.Debug|Any CPU.Build.0 = Debug|Any CPU" + NewLine
                + "\t\t{{{0}}}.Release|Any CPU.ActiveCfg = Release|Any CPU" + NewLine
                + "\t\t{{{0}}}.Release|Any CPU.Build.0 = Release|Any CPU";

            return string.Format(template, projectGuid);
        }

        string ProjectGuid(string assemblyName) => m_GUIDGenerator.ProjectGuid(m_ProjectName, assemblyName);

        // ---------------------------------------------------------------- paths

        /// <summary>Path relative to the project when possible; package files resolve to their real location on disk.</summary>
        internal string EscapedRelativePathFor(string file, out PackageLocation package)
        {
            var projectDir = ProjectDirectory.NormalizePathSeparators();
            file = file.NormalizePathSeparators();
            var path = SkipPathPrefix(file, projectDir);

            var unixPath = path.NormalizeWindowsToUnix();
            package = m_AssemblyNameProvider.FindForAssetPath(unixPath);
            if (package != null && !string.IsNullOrEmpty(package.ResolvedPath) && !string.IsNullOrEmpty(package.AssetPath)
                && unixPath.StartsWith(package.AssetPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                var absolutePath = Path.Combine(package.ResolvedPath, unixPath.Substring(package.AssetPath.Length + 1)).NormalizePathSeparators();
                path = SkipPathPrefix(absolutePath, projectDir);
            }

            return XmlFilename(path);
        }

        internal static string SkipPathPrefix(string path, string prefix)
        {
            if (path.StartsWith($"{prefix}{Path.DirectorySeparatorChar}") && path.Length > prefix.Length)
                return path.Substring(prefix.Length + 1);
            return path;
        }

        static string XmlFilename(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            path = path.Replace(@"%", "%25");
            path = path.Replace(@";", "%3b");

            return SecurityElement.Escape(path);
        }
    }
}
