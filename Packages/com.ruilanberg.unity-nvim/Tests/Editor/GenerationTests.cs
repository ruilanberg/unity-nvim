using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Compilation;
using UnityNvim.Editor.Generation;

namespace UnityNvim.Editor.Tests
{
    public class GenerationTests
    {
        sealed class FakeFileIO : IFileIO
        {
            public readonly Dictionary<string, string> Files = new Dictionary<string, string>();
            public int Writes;

            public bool Exists(string fileName) => Files.ContainsKey(fileName);
            public string ReadAllText(string fileName) => Files[fileName];

            public void WriteAllText(string fileName, string content)
            {
                Files[fileName] = content;
                Writes++;
            }
        }

        sealed class FakeProvider : IAssemblyNameProvider
        {
            public readonly List<Assembly> Assemblies = new List<Assembly>();
            public readonly List<PackageLocation> Packages = new List<PackageLocation>();
            public ResponseFileData ResponseFile = new ResponseFileData
            {
                Defines = Array.Empty<string>(),
                FullPathReferences = Array.Empty<string>(),
                Errors = Array.Empty<string>(),
                OtherArguments = Array.Empty<string>(),
            };

            public string[] ProjectSupportedExtensions => Array.Empty<string>();
            public string[] BuiltinSupportedExtensions => new[] { "cs", "uxml", "uss", "shader" };

            public string GetAssemblyNameFromScriptPath(string path) =>
                Assemblies.FirstOrDefault(a => a.sourceFiles.Contains(path))?.name + ".dll";

            public IEnumerable<Assembly> GetAssemblies(Func<string, bool> shouldFileBePartOfSolution) =>
                Assemblies.Where(a => a.sourceFiles.Any(shouldFileBePartOfSolution)).ToList();

            public PackageLocation FindForAssetPath(string assetPath) =>
                Packages.FirstOrDefault(p => assetPath.StartsWith(p.AssetPath + "/", StringComparison.OrdinalIgnoreCase));

            public ResponseFileData ParseResponseFile(string responseFilePath, string projectDirectory, ApiCompatibilityLevel level) => ResponseFile;

            public void ResetPackageInfoCache()
            {
            }
        }

        static readonly string Root = Path.Combine(Path.GetTempPath(), "UnityNvimGenTests");
        static readonly string ProjectDir = Path.Combine(Root, "MyGame");
        const string Analyzer = "Tools/Unity.SourceGenerators.dll";

        FakeFileIO m_IO;
        FakeProvider m_Provider;
        GeneratorOptions m_Options;

        [SetUp]
        public void SetUp()
        {
            m_IO = new FakeFileIO();
            m_Provider = new FakeProvider();
            m_Options = new GeneratorOptions { InvokePostprocessors = false };
        }

        ProjectGenerator Generator() => new ProjectGenerator(ProjectDir, m_Provider, m_IO, new GUIDProvider(), () => m_Options);

        static Assembly Asm(string name, string[] sources, Assembly[] references = null, string[] defines = null)
        {
            var options = new ScriptCompilerOptions { RoslynAnalyzerDllPaths = new[] { Analyzer } };
            return new Assembly(name, $"Library/ScriptAssemblies/{name}.dll", sources, defines ?? new[] { "UNITY_EDITOR" },
                references ?? Array.Empty<Assembly>(), new[] { Path.Combine(Root, "Unity", "UnityEngine.dll") },
                AssemblyFlags.None, options, name);
        }

        string Csproj(string name) => m_IO.Files[Path.Combine(ProjectDir, name + ".csproj")];

        [Test]
        public void LangVersionComesFromUnityNeverLatest()
        {
            var game = Asm("Game", new[] { "Assets/Player.cs" });
            m_Provider.Assemblies.Add(game);
            Generator().Sync();

            var expected = string.IsNullOrEmpty(game.compilerOptions.LanguageVersion) || game.compilerOptions.LanguageVersion == "latest"
                ? ProjectGenerator.DefaultLangVersion
                : game.compilerOptions.LanguageVersion;
            StringAssert.Contains($"<LangVersion>{expected}</LangVersion>", Csproj("Game"));
            StringAssert.DoesNotContain("<LangVersion>latest</LangVersion>", Csproj("Game"));
        }

        [Test]
        public void ResponseFileLangVersionWins()
        {
            m_Provider.ResponseFile.OtherArguments = new[] { "-langversion:8.0" };
            var game = Asm("Game", new[] { "Assets/Player.cs" });
            game.compilerOptions.ResponseFiles = new[] { "Assets/csc.rsp" };
            m_Provider.Assemblies.Add(game);
            Generator().Sync();

            StringAssert.Contains("<LangVersion>8.0</LangVersion>", Csproj("Game"));
        }

        [Test]
        public void AnalyzersAreEmittedWithoutAnyIde()
        {
            m_Options.ExtraAnalyzers = new[] { Path.Combine(Root, "Extra.Analyzers.dll") };
            m_Provider.ResponseFile.OtherArguments = new[] { "-a:Rsp.Analyzer.dll" };
            var game = Asm("Game", new[] { "Assets/Player.cs" });
            game.compilerOptions.ResponseFiles = new[] { "Assets/csc.rsp" };
            m_Provider.Assemblies.Add(game);
            Generator().Sync();

            var csproj = Csproj("Game");
            StringAssert.Contains("Unity.SourceGenerators.dll\" />", csproj);
            StringAssert.Contains("Rsp.Analyzer.dll\" />", csproj);
            StringAssert.Contains("Extra.Analyzers.dll\" />", csproj);
            Assert.That(csproj.Split('\n').Count(l => l.Contains("<Analyzer Include=")), Is.EqualTo(3));
        }

        [Test]
        public void DefinesAndUnsafeAreWritten()
        {
            m_Provider.ResponseFile.Defines = new[] { "FROM_RSP" };
            m_Provider.ResponseFile.Unsafe = true;
            var game = Asm("Game", new[] { "Assets/Player.cs" }, defines: new[] { "UNITY_EDITOR", "MY_DEFINE" });
            game.compilerOptions.ResponseFiles = new[] { "Assets/csc.rsp" };
            m_Provider.Assemblies.Add(game);
            Generator().Sync();

            var csproj = Csproj("Game");
            StringAssert.Contains("<DefineConstants>UNITY_EDITOR;MY_DEFINE;FROM_RSP</DefineConstants>", csproj);
            StringAssert.Contains("<AllowUnsafeBlocks>True</AllowUnsafeBlocks>", csproj);
        }

        [Test]
        public void SdkStyleByDefaultLegacyAsOption()
        {
            m_Provider.Assemblies.Add(Asm("Game", new[] { "Assets/Player.cs" }));
            Generator().Sync();
            StringAssert.Contains("<TargetFramework>netstandard2.1</TargetFramework>", Csproj("Game"));
            StringAssert.Contains("Sdk=\"Microsoft.NET.Sdk\"", Csproj("Game"));

            m_Options.LegacyProjects = true;
            Generator().Sync();
            StringAssert.Contains("ToolsVersion=\"4.0\"", Csproj("Game"));
        }

        [Test]
        public void ProjectReferenceBetweenGeneratedAssemblies()
        {
            var core = Asm("Core", new[] { "Assets/Core/Core.cs" });
            var game = Asm("Game", new[] { "Assets/Game/Player.cs" }, new[] { core });
            m_Provider.Assemblies.Add(core);
            m_Provider.Assemblies.Add(game);
            Generator().Sync();

            StringAssert.Contains("<ProjectReference Include=\"Core.csproj\" />", Csproj("Game"));
            StringAssert.Contains("<Compile Include=\"Assets", Csproj("Game"));
        }

        [Test]
        public void SolutionContainsBothProjects()
        {
            var core = Asm("Core", new[] { "Assets/Core/Core.cs" });
            var game = Asm("Game", new[] { "Assets/Game/Player.cs" }, new[] { core });
            m_Provider.Assemblies.Add(core);
            m_Provider.Assemblies.Add(game);
            var generator = Generator();
            generator.Sync();

            Assert.That(generator.SolutionFile(), Is.EqualTo(Path.Combine(ProjectDir, "MyGame.sln")));
            Assert.That(generator.HasSolutionBeenGenerated(), Is.True);
            var sln = m_IO.Files[generator.SolutionFile()];
            StringAssert.Contains("Microsoft Visual Studio Solution File, Format Version 12.00", sln);
            StringAssert.Contains("\"Core\", \"Core.csproj\"", sln);
            StringAssert.Contains("\"Game\", \"Game.csproj\"", sln);
        }

        [Test]
        public void PackageSourceFiltering()
        {
            m_Provider.Packages.Add(new PackageLocation
            {
                AssetPath = "Packages/com.foo",
                ResolvedPath = Path.Combine(Root, "Packages", "com.foo"),
                Source = PackageSource.Git,
            });
            var pkg = Asm("Foo", new[] { "Packages/com.foo/Runtime/Foo.cs" });
            var game = Asm("Game", new[] { "Assets/Player.cs" }, new[] { pkg });
            m_Provider.Assemblies.Add(pkg);
            m_Provider.Assemblies.Add(game);

            // Git packages are excluded by default: referenced as a dll, no project
            Generator().Sync();
            Assert.That(m_IO.Files.ContainsKey(Path.Combine(ProjectDir, "Foo.csproj")), Is.False);
            StringAssert.Contains("<Reference Include=\"Foo\">", Csproj("Game"));
            StringAssert.DoesNotContain("Foo.csproj", Csproj("Game"));

            // ... and get a project (with the real path on disk) once enabled
            m_Options.PackageSources |= PackageSource.Git;
            Generator().Sync();
            StringAssert.Contains("<ProjectReference Include=\"Foo.csproj\" />", Csproj("Game"));
            StringAssert.Contains("<Link>", Csproj("Foo"));
            StringAssert.Contains("com.foo", Csproj("Foo"));
        }

        [Test]
        public void WritesOnlyWhenContentChanges()
        {
            m_Provider.Assemblies.Add(Asm("Game", new[] { "Assets/Player.cs" }));
            var generator = Generator();
            generator.Sync();
            var writes = m_IO.Writes;
            Assert.That(writes, Is.EqualTo(2)); // .sln + Game.csproj

            generator.Sync();
            Assert.That(m_IO.Writes, Is.EqualTo(writes));

            // irrelevant asset: nothing to do
            Assert.That(generator.SyncIfNeeded(new[] { "Assets/icon.png" }, null, null, null, null), Is.False);
            // relevant but unchanged: nothing written
            Assert.That(generator.SyncIfNeeded(new[] { "Assets/Player.cs" }, null, null, null, null), Is.False);
            Assert.That(m_IO.Writes, Is.EqualTo(writes));

            // a new script in the assembly rewrites its project
            m_Provider.Assemblies[0] = Asm("Game", new[] { "Assets/Player.cs", "Assets/Enemy.cs" });
            Assert.That(generator.SyncIfNeeded(new[] { "Assets/Enemy.cs" }, null, null, null, null), Is.True);
            StringAssert.Contains("Enemy.cs", Csproj("Game"));
        }

        [Test]
        public void SupportedFilesForOpening()
        {
            m_Options.Extensions = new[] { "json", "md" };
            var generator = Generator();
            Assert.That(generator.IsSupportedFile("Assets/Player.cs"), Is.True);
            Assert.That(generator.IsSupportedFile("Assets/Game.asmdef"), Is.True);
            Assert.That(generator.IsSupportedFile("Assets/Game.asmref"), Is.True);
            Assert.That(generator.IsSupportedFile("Assets/data.json"), Is.True);
            Assert.That(generator.IsSupportedFile("Assets/icon.png"), Is.False);
        }
    }
}
