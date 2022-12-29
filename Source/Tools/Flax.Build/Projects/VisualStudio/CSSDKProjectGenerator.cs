// Copyright (c) 2012-2022 Wojciech Figat. All rights reserved.

using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.IO;

namespace Flax.Build.Projects.VisualStudio
{
    /// <summary>
    /// The Visual Studio project generator for C# projects (.NET SDK .csproj).
    /// </summary>
    /// <seealso cref="Flax.Build.Projects.VisualStudio.VisualStudioProjectGenerator" />
    public class CSSDKProjectGenerator : VisualStudioProjectGenerator
    {
        /// <inheritdoc />
        public CSSDKProjectGenerator(VisualStudioVersion version) : base(version)
        {
        }

        /// <inheritdoc />
        public override string ProjectFileExtension => "csproj";

        /// <inheritdoc />
        public override TargetType? Type => TargetType.DotNetCore;

        /// <inheritdoc />
        public override void GenerateProject(Project project)
        {
            var csProjectFileContent = new StringBuilder();

            var vsProject = (VisualStudioProject)project;
            var projectFileToolVersion = ProjectFileToolVersion;
            var projectDirectory = Path.GetDirectoryName(project.Path);
            var defaultTarget = project.Targets[0];
            foreach (var target in project.Targets)
            {
                // Pick the Editor-related target
                if (target.IsEditor)
                {
                    defaultTarget = target;
                    break;
                }
            }
            var defaultConfiguration = project.Configurations.First();
            foreach (var e in project.Configurations)
            {
                if (e.Configuration == defaultConfiguration.Configuration && e.Target == defaultTarget && e.Platform == Platform.BuildTargetPlatform)
                {
                    defaultConfiguration = e;
                    break;
                }
            }

            // Header
            csProjectFileContent.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
            csProjectFileContent.AppendLine("");

            // Properties

            csProjectFileContent.AppendLine("  <PropertyGroup>");

            // List supported platforms and configurations
            var allConfigurations = project.Configurations.Select(x => x.Text).Distinct().ToArray();
            var allPlatforms = project.Configurations.Select(x => x.ArchitectureName).Distinct().ToArray();
            csProjectFileContent.AppendLine(string.Format("    <Configurations>{0}</Configurations>", string.Join(";", allConfigurations)));
            csProjectFileContent.AppendLine(string.Format("    <Platforms>{0}</Platforms>", string.Join(";", allPlatforms)));
            
            // Provide default platform and configuration
            csProjectFileContent.AppendLine(string.Format("    <Configuration Condition=\" '$(Configuration)' == '' \">{0}</Configuration>", defaultConfiguration.Text));
            csProjectFileContent.AppendLine(string.Format("    <Platform Condition=\" '$(Platform)' == '' \">{0}</Platform>", defaultConfiguration.ArchitectureName));

            switch (project.OutputType ?? defaultTarget.OutputType)
            {
            case TargetOutputType.Executable:
                csProjectFileContent.AppendLine("    <OutputType>Exe</OutputType>");
                break;
            case TargetOutputType.Library:
                csProjectFileContent.AppendLine("    <OutputType>Library</OutputType>");
                break;
            default: throw new ArgumentOutOfRangeException();
            }

            var baseConfiguration = project.Configurations.First();
            var baseOutputDir = Utilities.MakePathRelativeTo(project.CSharp.OutputPath ?? baseConfiguration.TargetBuildOptions.OutputFolder, projectDirectory);
            var baseIntermediateOutputPath = Utilities.MakePathRelativeTo(project.CSharp.IntermediateOutputPath ?? Path.Combine(baseConfiguration.TargetBuildOptions.IntermediateFolder, "CSharp"), projectDirectory);

            csProjectFileContent.AppendLine("    <TargetFramework>net7.0</TargetFramework>");
            csProjectFileContent.AppendLine("    <ImplicitUsings>disable</ImplicitUsings>");
            csProjectFileContent.AppendLine("    <Nullable>annotations</Nullable>");
            csProjectFileContent.AppendLine("    <IsPackable>false</IsPackable>");
            csProjectFileContent.AppendLine("    <EnableDefaultItems>false</EnableDefaultItems>");
            csProjectFileContent.AppendLine("    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>");
            csProjectFileContent.AppendLine("    <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>");
            csProjectFileContent.AppendLine("    <EnableBaseIntermediateOutputPathMismatchWarning>false</EnableBaseIntermediateOutputPathMismatchWarning>");
            csProjectFileContent.AppendLine("    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>");
            csProjectFileContent.AppendLine("    <ProduceReferenceAssembly>false</ProduceReferenceAssembly>");
            csProjectFileContent.AppendLine(string.Format("    <RootNamespace>{0}</RootNamespace>", project.BaseName));
            csProjectFileContent.AppendLine(string.Format("    <AssemblyName>{0}.CSharp</AssemblyName>", project.BaseName));
            csProjectFileContent.AppendLine("    <LangVersion>11.0</LangVersion>");
            csProjectFileContent.AppendLine("    <FileAlignment>512</FileAlignment>");

            // Needed for Hostfxr
            csProjectFileContent.AppendLine("    <GenerateRuntimeConfigurationFiles>true</GenerateRuntimeConfigurationFiles>");
            csProjectFileContent.AppendLine("    <EnableDynamicLoading>true</EnableDynamicLoading>");
            //csProjectFileContent.AppendLine("    <CopyLocalLockFileAssemblies>false</CopyLocalLockFileAssemblies>"); // TODO: use it to reduce burden of framework libs

            // This needs to be set here to fix errors in VS
            csProjectFileContent.AppendLine(string.Format("    <OutDir>{0}</OutDir>", baseOutputDir));
            csProjectFileContent.AppendLine(string.Format("    <IntermediateOutputPath>{0}</IntermediateOutputPath>", baseIntermediateOutputPath));

            csProjectFileContent.AppendLine("  </PropertyGroup>");
            csProjectFileContent.AppendLine("");

            // Default configuration
            {
                var configuration = defaultConfiguration;
                var defines = string.Join(";", project.Defines);
                if (configuration.TargetBuildOptions.ScriptingAPI.Defines.Count != 0)
                {
                    if (defines.Length != 0)
                        defines += ";";
                    defines += string.Join(";", configuration.TargetBuildOptions.ScriptingAPI.Defines);
                }
                var outputPath = Utilities.MakePathRelativeTo(project.CSharp.OutputPath ?? configuration.TargetBuildOptions.OutputFolder, projectDirectory);
                var intermediateOutputPath = Utilities.MakePathRelativeTo(project.CSharp.IntermediateOutputPath ?? Path.Combine(configuration.TargetBuildOptions.IntermediateFolder, "CSharp"), projectDirectory);

                csProjectFileContent.AppendLine(string.Format("  <PropertyGroup Condition=\" '$(Configuration)|$(Platform)' == '{0}' \">", configuration.Name));
                csProjectFileContent.AppendLine("    <DebugSymbols>true</DebugSymbols>");
                csProjectFileContent.AppendLine("    <DebugType>portable</DebugType>");
                csProjectFileContent.AppendLine(string.Format("    <Optimize>{0}</Optimize>", configuration.Configuration == TargetConfiguration.Debug ? "false" : "true"));
                csProjectFileContent.AppendLine(string.Format("    <OutputPath>{0}\\</OutputPath>", outputPath));
                csProjectFileContent.AppendLine(string.Format("    <BaseIntermediateOutputPath>{0}\\</BaseIntermediateOutputPath>", intermediateOutputPath));
                csProjectFileContent.AppendLine(string.Format("    <IntermediateOutputPath>{0}\\</IntermediateOutputPath>", intermediateOutputPath));
                csProjectFileContent.AppendLine(string.Format("    <DefineConstants>{0}</DefineConstants>", defines));
                csProjectFileContent.AppendLine("    <ErrorReport>prompt</ErrorReport>");
                csProjectFileContent.AppendLine("    <WarningLevel>4</WarningLevel>");
                csProjectFileContent.AppendLine("    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>");
                if (configuration.TargetBuildOptions.ScriptingAPI.IgnoreMissingDocumentationWarnings)
                    csProjectFileContent.AppendLine("    <NoWarn>1591</NoWarn>");
                csProjectFileContent.AppendLine(string.Format("    <DocumentationFile>{0}\\{1}.CSharp.xml</DocumentationFile>", outputPath, project.BaseName));
                csProjectFileContent.AppendLine("    <UseVSHostingProcess>true</UseVSHostingProcess>");
                csProjectFileContent.AppendLine("  </PropertyGroup>");
                csProjectFileContent.AppendLine("");
            }

            // Configurations
            foreach (var configuration in project.Configurations)
            {
                var defines = string.Join(";", project.Defines);
                if (configuration.TargetBuildOptions.ScriptingAPI.Defines.Count != 0)
                {
                    if (defines.Length != 0)
                        defines += ";";
                    defines += string.Join(";", configuration.TargetBuildOptions.ScriptingAPI.Defines);
                }
                var outputPath = Utilities.MakePathRelativeTo(project.CSharp.OutputPath ?? configuration.TargetBuildOptions.OutputFolder, projectDirectory);
                var intermediateOutputPath = Utilities.MakePathRelativeTo(project.CSharp.IntermediateOutputPath ?? Path.Combine(configuration.TargetBuildOptions.IntermediateFolder, "CSharp"), projectDirectory);

                csProjectFileContent.AppendLine(string.Format("  <PropertyGroup Condition=\" '$(Configuration)|$(Platform)' == '{0}' \">", configuration.Name));
                csProjectFileContent.AppendLine("    <DebugSymbols>true</DebugSymbols>");
                csProjectFileContent.AppendLine("    <DebugType>portable</DebugType>");
                csProjectFileContent.AppendLine(string.Format("    <Optimize>{0}</Optimize>", configuration.Configuration == TargetConfiguration.Release ? "true" : "false"));
                csProjectFileContent.AppendLine(string.Format("    <OutputPath>{0}\\</OutputPath>", outputPath));
                csProjectFileContent.AppendLine(string.Format("    <BaseIntermediateOutputPath>{0}\\</BaseIntermediateOutputPath>", intermediateOutputPath));
                csProjectFileContent.AppendLine(string.Format("    <IntermediateOutputPath>{0}\\</IntermediateOutputPath>", intermediateOutputPath));
                csProjectFileContent.AppendLine(string.Format("    <DefineConstants>{0}</DefineConstants>", defines));
                csProjectFileContent.AppendLine("    <ErrorReport>prompt</ErrorReport>");
                csProjectFileContent.AppendLine("    <WarningLevel>4</WarningLevel>");
                csProjectFileContent.AppendLine("    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>");
                if (configuration.TargetBuildOptions.ScriptingAPI.IgnoreMissingDocumentationWarnings)
                    csProjectFileContent.AppendLine("    <NoWarn>1591</NoWarn>");
                csProjectFileContent.AppendLine(string.Format("    <DocumentationFile>{0}\\{1}.CSharp.xml</DocumentationFile>", outputPath, project.BaseName));
                csProjectFileContent.AppendLine("    <UseVSHostingProcess>true</UseVSHostingProcess>");
                csProjectFileContent.AppendLine("  </PropertyGroup>");
                csProjectFileContent.AppendLine("");
            }

            // References

            csProjectFileContent.AppendLine("  <ItemGroup>");

            // Unused when using explicitly NetCore7 ?
            //foreach (var reference in project.CSharp.SystemReferences)
            {
                //csProjectFileContent.AppendLine(string.Format("    <Reference Include=\"{0}\" />", reference));
            }

            foreach (var reference in project.CSharp.FileReferences)
            {
                csProjectFileContent.AppendLine(string.Format("    <Reference Include=\"{0}\">", Path.GetFileNameWithoutExtension(reference)));
                csProjectFileContent.AppendLine(string.Format("      <HintPath>{0}</HintPath>", Utilities.MakePathRelativeTo(reference, projectDirectory)));
                csProjectFileContent.AppendLine("    </Reference>");
            }

            foreach (var dependency in project.Dependencies)
            {
                csProjectFileContent.AppendLine(string.Format("    <ProjectReference Include=\"{0}\">", Utilities.MakePathRelativeTo(dependency.Path, projectDirectory)));
                csProjectFileContent.AppendLine(string.Format("      <Project>{0}</Project>", ((VisualStudioProject)dependency).ProjectGuid.ToString("B").ToUpperInvariant()));
                csProjectFileContent.AppendLine(string.Format("      <Name>{0}</Name>", dependency.BaseName));
                csProjectFileContent.AppendLine("    </ProjectReference>");
            }

            csProjectFileContent.AppendLine("  </ItemGroup>");
            csProjectFileContent.AppendLine("");

            // Files and folders

            csProjectFileContent.AppendLine("  <ItemGroup>");

            var files = new List<string>();
            if (project.SourceFiles != null)
                files.AddRange(project.SourceFiles);
            if (project.SourceDirectories != null)
            {
                foreach (var folder in project.SourceDirectories)
                {
                    files.AddRange(Directory.GetFiles(folder, "*", SearchOption.AllDirectories));
                }
            }

            foreach (var file in files)
            {
                string fileType;
                if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    fileType = "Compile";
                else
                    fileType = "None";

                var projectPath = Utilities.MakePathRelativeTo(file, projectDirectory);
                csProjectFileContent.AppendLine(string.Format("    <{0} Include=\"{1}\" />", fileType, projectPath));
            }

            if (project.GeneratedSourceFiles != null)
            {
                foreach (var file in project.GeneratedSourceFiles)
                {
                    string fileType;
                    if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                        fileType = "Compile";
                    else
                        fileType = "None";

                    csProjectFileContent.AppendLine(string.Format("    <{0} Visible=\"false\" Include=\"{1}\" />", fileType, file));
                }
            }

            csProjectFileContent.AppendLine("  </ItemGroup>");

            // End

            csProjectFileContent.AppendLine("</Project>");

            if (defaultTarget.CustomExternalProjectFilePath == null)
            {
                // Save the files
                Utilities.WriteFileIfChanged(project.Path, csProjectFileContent.ToString());
            }
        }
    }
}