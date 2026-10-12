/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Unity Technologies.
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See THIRD-PARTY-NOTICES.md in the package root.
 *
 *  Modified for unity-nvim (ported from com.unity.ide.visualstudio 2.0.27: SdkStyleProjectGeneration,
 *  LegacyStyleProjectGeneration and the header helpers of ProjectGeneration). Removed VSTU flavoring
 *  and ProjectCapability items; the SDK imports are kept.
 *--------------------------------------------------------------------------------------------*/
using System.Linq;
using System.Text;

namespace UnityNvim.Editor.Generation
{
    /// <summary>Project-file dialect: SDK-style (default) or legacy ToolsVersion 4.0.</summary>
    internal abstract class ProjectWriter
    {
        internal const string NewLine = ProjectGenerator.NewLine;
        public const string MSBuildNamespaceUri = "http://schemas.microsoft.com/developer/msbuild/2003";

        public static readonly ProjectWriter Sdk = new SdkProjectWriter();
        public static readonly ProjectWriter Legacy = new LegacyProjectWriter();

        public abstract void AppendHeader(ProjectProperties properties, StringBuilder sb);
        public abstract void AppendProjectReference(StringBuilder sb, string referenceName, string referenceGuid);
        public abstract void AppendFooter(StringBuilder sb);

        protected static void AppendHeaderProperties(ProjectProperties properties, StringBuilder sb)
        {
            const string NoWarn = "0169;USG0001";

            sb.Append(@"  <PropertyGroup>").Append(NewLine);
            sb.Append(@"    <NoWarn>").Append(NoWarn).Append("</NoWarn>").Append(NewLine);
            sb.Append(@"    <DefineConstants>").Append(string.Join(";", properties.Defines)).Append(@"</DefineConstants>").Append(NewLine);
            sb.Append(@"    <AllowUnsafeBlocks>").Append(properties.Unsafe).Append(@"</AllowUnsafeBlocks>").Append(NewLine);
            sb.Append(@"  </PropertyGroup>").Append(NewLine);
        }

        protected static void AppendAnalyzers(ProjectProperties properties, StringBuilder sb)
        {
            if (!string.IsNullOrEmpty(properties.RulesetPath))
            {
                sb.Append(@"  <PropertyGroup>").Append(NewLine);
                sb.Append(@"    <CodeAnalysisRuleSet>").Append(properties.RulesetPath).Append(@"</CodeAnalysisRuleSet>").Append(NewLine);
                sb.Append(@"  </PropertyGroup>").Append(NewLine);
            }

            if (properties.Analyzers.Any())
            {
                sb.Append(@"  <ItemGroup>").Append(NewLine);
                foreach (var analyzer in properties.Analyzers)
                    sb.Append(@"    <Analyzer Include=""").Append(analyzer).Append(@""" />").Append(NewLine);
                sb.Append(@"  </ItemGroup>").Append(NewLine);
            }

            if (!string.IsNullOrEmpty(properties.AnalyzerConfigPath))
            {
                sb.Append(@"  <ItemGroup>").Append(NewLine);
                sb.Append(@"    <EditorConfigFiles Include=""").Append(properties.AnalyzerConfigPath).Append(@""" />").Append(NewLine);
                sb.Append(@"  </ItemGroup>").Append(NewLine);
            }

            if (properties.AdditionalFilePaths.Any())
            {
                sb.Append(@"  <ItemGroup>").Append(NewLine);
                foreach (var additionalFile in properties.AdditionalFilePaths)
                    sb.Append(@"    <AdditionalFiles Include=""").Append(additionalFile).Append(@""" />").Append(NewLine);
                sb.Append(@"  </ItemGroup>").Append(NewLine);
            }
        }
    }

    /// <summary>
    /// SDK-style projects, the format Microsoft uses with the Roslyn language server (VS Code / roslyn.nvim).
    /// Targets netstandard2.1 so tools don't need .NET Framework reference assemblies (Mac/Linux); NoStdLib/NoConfig
    /// keep the API surface exactly the one Unity selected through the referenced dlls.
    /// </summary>
    internal sealed class SdkProjectWriter : ProjectWriter
    {
        public override void AppendHeader(ProjectProperties properties, StringBuilder sb)
        {
            sb.Append(@"<Project>").Append(NewLine);
            sb.Append(@"  <!-- Generated file, do not modify, your changes will be overwritten (use AssetPostprocessor.OnGeneratedCSProject) -->").Append(NewLine);

            // A dedicated obj subfolder per project, set before Sdk.props (MSB3539)
            sb.Append(@"  <PropertyGroup>").Append(NewLine);
            sb.Append($"    <BaseIntermediateOutputPath>{@"Temp\obj\$(MSBuildProjectName)".NormalizePathSeparators()}</BaseIntermediateOutputPath>").Append(NewLine);
            sb.Append(@"    <IntermediateOutputPath>$(BaseIntermediateOutputPath)</IntermediateOutputPath>").Append(NewLine);
            sb.Append(@"    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>").Append(NewLine);
            sb.Append(@"    <UseCommonOutputDirectory>true</UseCommonOutputDirectory>").Append(NewLine);
            sb.Append(@"    <OutputPath>").Append(properties.OutputPath.NormalizePathSeparators()).Append(@"</OutputPath>").Append(NewLine);
            sb.Append(@"  </PropertyGroup>").Append(NewLine);

            sb.Append(@"  <Import Project=""Sdk.props"" Sdk=""Microsoft.NET.Sdk"" />").Append(NewLine);

            sb.Append(@"  <PropertyGroup>").Append(NewLine);
            sb.Append(@"    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>").Append(NewLine);
            sb.Append(@"    <EnableDefaultItems>false</EnableDefaultItems>").Append(NewLine);
            sb.Append(@"    <LangVersion>").Append(properties.LangVersion).Append(@"</LangVersion>").Append(NewLine);
            sb.Append(@"    <RootNamespace>").Append(properties.RootNamespace).Append(@"</RootNamespace>").Append(NewLine);
            sb.Append(@"    <OutputType>Library</OutputType>").Append(NewLine);
            sb.Append(@"    <AssemblyName>").Append(properties.AssemblyName).Append(@"</AssemblyName>").Append(NewLine);
            sb.Append(@"    <TargetFramework>netstandard2.1</TargetFramework>").Append(NewLine);
            sb.Append(@"    <BaseDirectory>.</BaseDirectory>").Append(NewLine);
            sb.Append(@"  </PropertyGroup>").Append(NewLine);

            AppendHeaderProperties(properties, sb);

            // Explicit references only
            sb.Append(@"  <PropertyGroup>").Append(NewLine);
            sb.Append(@"    <NoStandardLibraries>true</NoStandardLibraries>").Append(NewLine);
            sb.Append(@"    <NoStdLib>true</NoStdLib>").Append(NewLine);
            sb.Append(@"    <NoConfig>true</NoConfig>").Append(NewLine);
            sb.Append(@"    <DisableImplicitFrameworkReferences>true</DisableImplicitFrameworkReferences>").Append(NewLine);
            sb.Append(@"    <MSBuildWarningsAsMessages>MSB3277</MSBuildWarningsAsMessages>").Append(NewLine);
            sb.Append(@"  </PropertyGroup>").Append(NewLine);

            AppendAnalyzers(properties, sb);
        }

        public override void AppendProjectReference(StringBuilder sb, string referenceName, string referenceGuid)
        {
            sb.Append(@"    <ProjectReference Include=""").Append(referenceName).Append(".csproj").Append(@""" />").Append(NewLine);
        }

        public override void AppendFooter(StringBuilder sb)
        {
            sb.Append(@"  <Import Project=""Sdk.targets"" Sdk=""Microsoft.NET.Sdk"" />").Append(NewLine);
            sb.Append("</Project>").Append(NewLine);
        }
    }

    internal sealed class LegacyProjectWriter : ProjectWriter
    {
        public override void AppendHeader(ProjectProperties properties, StringBuilder sb)
        {
            sb.Append(@"<?xml version=""1.0"" encoding=""utf-8""?>").Append(NewLine);
            sb.Append($@"<Project ToolsVersion=""4.0"" DefaultTargets=""Build"" xmlns=""{MSBuildNamespaceUri}"">").Append(NewLine);
            sb.Append(@"  <!-- Generated file, do not modify, your changes will be overwritten (use AssetPostprocessor.OnGeneratedCSProject) -->").Append(NewLine);
            sb.Append(@"  <PropertyGroup>").Append(NewLine);
            sb.Append(@"    <LangVersion>").Append(properties.LangVersion).Append(@"</LangVersion>").Append(NewLine);
            sb.Append(@"  </PropertyGroup>").Append(NewLine);
            sb.Append(@"  <PropertyGroup>").Append(NewLine);
            sb.Append(@"    <Configuration Condition="" '$(Configuration)' == '' "">Debug</Configuration>").Append(NewLine);
            sb.Append(@"    <Platform Condition="" '$(Platform)' == '' "">AnyCPU</Platform>").Append(NewLine);
            sb.Append(@"    <ProductVersion>10.0.20506</ProductVersion>").Append(NewLine);
            sb.Append(@"    <SchemaVersion>2.0</SchemaVersion>").Append(NewLine);
            sb.Append(@"    <RootNamespace>").Append(properties.RootNamespace).Append(@"</RootNamespace>").Append(NewLine);
            sb.Append(@"    <ProjectGuid>{").Append(properties.ProjectGuid).Append(@"}</ProjectGuid>").Append(NewLine);
            sb.Append(@"    <OutputType>Library</OutputType>").Append(NewLine);
            sb.Append(@"    <AppDesignerFolder>Properties</AppDesignerFolder>").Append(NewLine);
            sb.Append(@"    <AssemblyName>").Append(properties.AssemblyName).Append(@"</AssemblyName>").Append(NewLine);
            sb.Append(@"    <TargetFrameworkVersion>v4.7.1</TargetFrameworkVersion>").Append(NewLine);
            sb.Append(@"    <FileAlignment>512</FileAlignment>").Append(NewLine);
            sb.Append(@"    <BaseDirectory>.</BaseDirectory>").Append(NewLine);
            sb.Append(@"  </PropertyGroup>").Append(NewLine);

            AppendHeaderProperties(properties, sb);

            sb.Append(@"  <PropertyGroup Condition="" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' "">").Append(NewLine);
            sb.Append(@"    <DebugSymbols>true</DebugSymbols>").Append(NewLine);
            sb.Append(@"    <DebugType>full</DebugType>").Append(NewLine);
            sb.Append(@"    <Optimize>false</Optimize>").Append(NewLine);
            sb.Append(@"    <OutputPath>").Append(properties.OutputPath).Append(@"</OutputPath>").Append(NewLine);
            sb.Append(@"    <ErrorReport>prompt</ErrorReport>").Append(NewLine);
            sb.Append(@"    <WarningLevel>4</WarningLevel>").Append(NewLine);
            sb.Append(@"  </PropertyGroup>").Append(NewLine);
            sb.Append(@"  <PropertyGroup Condition="" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' "">").Append(NewLine);
            sb.Append(@"    <DebugType>pdbonly</DebugType>").Append(NewLine);
            sb.Append(@"    <Optimize>true</Optimize>").Append(NewLine);
            sb.Append($"    <OutputPath>{@"Temp\bin\Release\".NormalizePathSeparators()}</OutputPath>").Append(NewLine);
            sb.Append(@"    <ErrorReport>prompt</ErrorReport>").Append(NewLine);
            sb.Append(@"    <WarningLevel>4</WarningLevel>").Append(NewLine);
            sb.Append(@"  </PropertyGroup>").Append(NewLine);

            // Explicit references only
            sb.Append(@"  <PropertyGroup>").Append(NewLine);
            sb.Append(@"    <NoConfig>true</NoConfig>").Append(NewLine);
            sb.Append(@"    <NoStdLib>true</NoStdLib>").Append(NewLine);
            sb.Append(@"    <AddAdditionalExplicitAssemblyReferences>false</AddAdditionalExplicitAssemblyReferences>").Append(NewLine);
            sb.Append(@"    <ImplicitlyExpandNETStandardFacades>false</ImplicitlyExpandNETStandardFacades>").Append(NewLine);
            sb.Append(@"    <ImplicitlyExpandDesignTimeFacades>false</ImplicitlyExpandDesignTimeFacades>").Append(NewLine);
            sb.Append(@"  </PropertyGroup>").Append(NewLine);

            AppendAnalyzers(properties, sb);
        }

        public override void AppendProjectReference(StringBuilder sb, string referenceName, string referenceGuid)
        {
            sb.Append(@"    <ProjectReference Include=""").Append(referenceName).Append(".csproj").Append(@""">").Append(NewLine);
            sb.Append("      <Project>{").Append(referenceGuid).Append("}</Project>").Append(NewLine);
            sb.Append("      <Name>").Append(referenceName).Append("</Name>").Append(NewLine);
            sb.Append("    </ProjectReference>").Append(NewLine);
        }

        public override void AppendFooter(StringBuilder sb)
        {
            sb.Append($"  <Import Project=\"{@"$(MSBuildToolsPath)\Microsoft.CSharp.targets".NormalizePathSeparators()}\" />").Append(NewLine);
            sb.Append(@"  <Target Name=""GenerateTargetFrameworkMonikerAttribute"" />").Append(NewLine);
            sb.Append("</Project>").Append(NewLine);
        }
    }
}
