// Copyright (c) 2012-2022 Wojciech Figat. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Flax.Build;
using Flax.Build.Platforms;

namespace Flax.Deps.Dependencies
{
    /// <summary>
    /// Mono open source ECMA CLI, C# and .NET implementation. http://www.mono-project.com/
    /// </summary>
    /// <seealso cref="Flax.Deps.Dependency" />
    class mono : Dependency
    {
        /// <inheritdoc />
        public override TargetPlatform[] Platforms
        {
            get
            {
                switch (BuildPlatform)
                {
                case TargetPlatform.Windows:
                    return new[]
                    {
                        TargetPlatform.Windows,
                        TargetPlatform.UWP,
                        TargetPlatform.XboxOne,
                        TargetPlatform.XboxScarlett,
                        TargetPlatform.Switch,
                    };
                case TargetPlatform.Linux:
                    return new[]
                    {
                        TargetPlatform.Linux,
                        TargetPlatform.Android,
                    };
                case TargetPlatform.Mac:
                    return new[]
                    {
                        TargetPlatform.Mac,
                    };
                default: return new TargetPlatform[0];
                }
            }
        }

        private string root;
        private string monoPropsPath;
        private string monoPreprocesorDefines;
        private static List<string> monoExports;

        private void Autogen(string host, string[] monoOptions, string buildDir, Dictionary<string, string> envVars = null)
        {
            var args = string.Format("--host={0} \"--prefix={2}\" {1}", host, string.Join(" ", monoOptions), buildDir.Replace('\\', '/'));
            if (envVars != null)
            {
                foreach (var e in envVars)
                {
                    if (e.Key == "PATH")
                        continue;
                    if (e.Value.Contains(' '))
                        args += $" \"{e.Key}={e.Value}\"";
                    else
                        args += $" {e.Key}={e.Value}";
                }
            }
            RunBash(Path.Combine(root, "autogen.sh"), args, root, envVars);
        }

        private void Make(Dictionary<string, string> envVars = null)
        {
            RunBash("make", string.Format("-j {0}", Environment.ProcessorCount), root, envVars);
            RunBash("make", "install", root, envVars);
        }

        private void BuildMsvc(BuildOptions options, TargetPlatform platform, TargetArchitecture architecture)
        {
            string configuration = "Release";
            string buildPlatform;
            switch (architecture)
            {
            case TargetArchitecture.x86:
                buildPlatform = "Win32";
                break;
            default:
                buildPlatform = architecture.ToString();
                break;
            }

            // Build mono
            var props = new Dictionary<string, string>
            {
                { "MONO_USE_TARGET_SUFFIX", "false" },
                { "MONO_USE_STATIC_LIBMONO", "true" },
                { "MONO_ENABLE_BTLS", "true" },
            };
            Deploy.VCEnvironment.BuildSolution(Path.Combine(root, "msvc", "mono.sln"), configuration, buildPlatform, props);

            // Deploy binaries
            var binaries = new[]
            {
                Path.Combine("lib", configuration, "libmono-static.lib"),
                Path.Combine("bin", configuration, "libmono-btls-shared.dll"),
                Path.Combine("bin", configuration, "MonoPosixHelper.dll"),
            };
            var srcBinaries = Path.Combine(root, "msvc", "build", "sgen", buildPlatform);
            var depsFolder = GetThirdPartyFolder(options, platform, architecture);
            Log.Verbose("Copy mono binaries from " + srcBinaries);
            foreach (var binary in binaries)
            {
                var src = Path.Combine(srcBinaries, binary);
                var dst = Path.Combine(depsFolder, Path.GetFileName(src));
                Utilities.FileCopy(src, dst);
            }

            // Deploy debug symbols
            var debugSymbolsLibs = new[]
            {
                "libmonoruntime",
                "libmonoutils",
                "libgcmonosgen",
                "libmini",
                "eglib",
            };
            foreach (var debugSymbol in debugSymbolsLibs)
            {
                var src = Path.Combine(srcBinaries, "obj", debugSymbol, configuration, debugSymbol + ".pdb");
                var dst = Path.Combine(depsFolder, Path.GetFileName(src));
                Utilities.FileCopy(src, dst);
            }
        }

        private void BuildBcl(BuildOptions options, TargetPlatform platform)
        {
            var configuration = "Release";
            string buildPlatform;
            switch (platform)
            {
            case TargetPlatform.Android:
                buildPlatform = "monodroid";
                break;
            case TargetPlatform.PS4:
                buildPlatform = "orbis";
                break;
            default:
                buildPlatform = "net_4_x";
                break;
            }

            // Build jay
            Deploy.VCEnvironment.BuildSolution(Path.Combine(root, "mcs", "jay", "jay.vcxproj"), "Release", "x64");

            // Build class library
            Utilities.Run(Deploy.VCEnvironment.CscPath, "prepare.cs", null, Path.Combine(root, "msvc", "scripts"));
            Utilities.Run(Path.Combine(root, "msvc", "scripts", "prepare.exe"), "..\\..\\mcs core", null, Path.Combine(root, "msvc", "scripts"));
            Deploy.VCEnvironment.BuildSolution(Path.Combine(root, "bcl.sln"), configuration, buildPlatform);
        }

        private static void ReplaceXmlNodeValue(XmlNode node, string name, string value)
        {
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.Name == name)
                {
                    child.InnerText = value;
                }

                ReplaceXmlNodeValue(child, name, value);
            }
        }

        protected static string FindXmlNodeValue(XmlNode node, string name)
        {
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.Name == name)
                {
                    return child.InnerText;
                }

                var value = FindXmlNodeValue(child, name);
                if (value != null)
                    return value;
            }

            return null;
        }

        protected void ConfigureMsvc(BuildOptions options, string vcTools, string winSdkVer, string winVer = "0x0601", string customDefines = null)
        {
            // Patch vcxproj files
            var files = Directory.GetFiles(Path.Combine(root, "msvc"), "*.vcxproj", SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
                var projectXml = new XmlDocument();
                projectXml.Load(file);

                ReplaceXmlNodeValue(projectXml, "PlatformToolset", vcTools);
                ReplaceXmlNodeValue(projectXml, "WindowsTargetPlatformVersion", winSdkVer);

                projectXml.Save(file);
            }

            // Patch mono.props
            {
                var defines = monoPreprocesorDefines.Replace("WINVER=0x0601", "WINVER=" + winVer);
                defines = defines.Replace("_WIN32_WINNT=0x0601", "_WIN32_WINNT=" + winVer);
                if (customDefines != null)
                {
                    defines = customDefines + ';' + defines;
                }

                var monoProps = new XmlDocument();
                monoProps.Load(monoPropsPath);

                ReplaceXmlNodeValue(monoProps, "MONO_PREPROCESSOR_DEFINITIONS", defines);

                monoProps.Save(monoPropsPath);
            }
        }

        private static bool EnumSymbols(string name, ulong address, uint size, IntPtr context)
        {
            if (name.StartsWith("mono_") && !monoExports.Contains(name))
            {
                if (MonoExportsIncludePrefixes.Any(name.StartsWith) && !MonoExportsSkipPrefixes.Any(name.StartsWith))
                {
                    monoExports.Add(name);
                }
            }
            return true;
        }

        private void GetMonoExports(BuildOptions options)
        {
            // Search for all exported mono API functions from mono library
            monoExports = new List<string>(2048);
            monoExports.AddRange(MonoExportsCustom);
            IntPtr hCurrentProcess = Process.GetCurrentProcess().Handle;
            bool status = WinAPI.dbghelp.SymInitialize(hCurrentProcess, null, false);
            if (status == false)
            {
                Log.Error("Failed to initialize Sym.");
                return;
            }
            string dllPath = Path.Combine(root, "msvc\\build\\sgen\\x64\\bin\\Release\\mono-2.0.dll");
            ulong baseOfDll = WinAPI.dbghelp.SymLoadModuleEx(hCurrentProcess, IntPtr.Zero, dllPath, null, 0, 0, IntPtr.Zero, 0);
            if (baseOfDll == 0)
            {
                Log.Error($"Failed to load mono library for exports from \'{dllPath}\'.");
                WinAPI.dbghelp.SymCleanup(hCurrentProcess);
                return;
            }
            if (WinAPI.dbghelp.SymEnumerateSymbols64(hCurrentProcess, baseOfDll, EnumSymbols, IntPtr.Zero) == false)
            {
                Log.Error($"Failed to enumerate mono library exports from \'{dllPath}\'.");
            }
            WinAPI.dbghelp.SymCleanup(hCurrentProcess);

            // Make exports list stable
            monoExports.Sort();

            // Generate exports code
            var exports = new StringBuilder(monoExports.Count * 70);
            foreach (var monoExport in monoExports)
            {
                exports.AppendLine(string.Format("#pragma comment(linker, \"/export:{0}\")", monoExport));
            }

            // Update source file with exported symbols
            var mCorePath = Path.Combine(Globals.EngineRoot, "Source", "Engine", "Scripting", "ManagedCLR", "MCore.cpp");
            var contents = File.ReadAllText(mCorePath);
            var startPos = contents.IndexOf("#pragma comment(linker,");
            var endPos = contents.LastIndexOf("#pragma comment(linker,");
            endPos = contents.IndexOf(')', endPos);
            contents = contents.Remove(startPos, endPos - startPos + 3);
            contents = contents.Insert(startPos, exports.ToString());
            Utilities.WriteFileIfChanged(mCorePath, contents);
        }

        private void DeployDylib(string srcPath, string dstFolder)
        {
            var dstPath = Path.Combine(dstFolder, Path.GetFileName(srcPath));
            Utilities.FileCopy(srcPath, dstPath);
            MacPlatform.FixInstallNameId(dstPath);
        }

        private void DeployData(BuildOptions options, TargetPlatform platform, string buildDir, bool withEditor)
        {
            // Game
            var srcMonoLibs = Path.Combine(buildDir, "lib", "mono");
            var dstMonoFiles = Path.Combine(options.PlatformsFolder, platform.ToString(), "Binaries", "Mono");
            CloneDirectory(Path.Combine(buildDir, "etc"), Path.Combine(dstMonoFiles, "etc"));

            // Copy libs
            var dstMonoLib = Path.Combine(dstMonoFiles, "lib", "mono");
            SetupDirectory(dstMonoLib, true);
            Utilities.DirectoryCopy(Path.Combine(srcMonoLibs, "4.5"), Path.Combine(dstMonoLib, "4.5"), false, true, "*.dll");
            Utilities.DirectoryCopy(Path.Combine(srcMonoLibs, "gac"), Path.Combine(dstMonoLib, "gac"), true, true, "*.dll");
            Utilities.FilesDelete(dstMonoLib, "*.pdb");

            // Remove unused libs
            var unusedLibs = new[]
            {
                "Commons.Xml.Relaxng*",
                "cscompmgd*",
                "CustomMarshalers*",
                "I18N*",
                "IBM*",
                "Microsoft.Build.*",
                "Microsoft.CodeAnalysis.CSharp.Scripting",
                "Microsoft.CodeAnalysis.Scripting",
                "Microsoft.CodeAnalysis.VisualBasic",
                "Microsoft.VisualBasic",
                "Microsoft.VisualC",
                "Microsoft.Web.*",
                "Mono.Cairo*",
                "Mono.Cecil*",
                "Mono.CodeContracts*",
                "Mono.CompilerServices.SymbolWriter*",
                "Mono.CSharp*",
                "Mono.Data*",
                "Mono.Debugger.Soft*",
                "Mono.Http*",
                "Mono.Management*",
                "Mono.Messaging*",
                "Mono.Parallel*",
                "Mono.Profiler.Log*",
                "Mono.Simd*",
                "Mono.Tasklets*",
                "Mono.WebBrowser*",
                "Mono.XBuild.Tasks*",
                "Novell.Directory.Ldap*",
                "PEAPI*",
                "RabbitMQ.Client*",
                "SMDiagnostics*",
                "WebMatrix.Data*",
                "xsp4",
            };
            var editorUsedLibs = new[]
            {
                "Microsoft.CodeAnalysis",
                "Microsoft.CodeAnalysis.CSharp",
                "Microsoft.CSharp",
            };
            foreach (var gameUsedLib in unusedLibs)
            {
                Utilities.FilesDelete(dstMonoLib, gameUsedLib);
                if (!gameUsedLib.EndsWith("*"))
                    Utilities.FilesDelete(dstMonoLib, gameUsedLib + ".dll");
                Utilities.DirectoriesDelete(dstMonoLib, gameUsedLib);
            }
            foreach (var gameUsedLib in editorUsedLibs)
            {
                Utilities.FilesDelete(dstMonoLib, gameUsedLib);
                Utilities.DirectoriesDelete(dstMonoLib, gameUsedLib);
            }

            // Editor
            if (withEditor)
            {
                var dstMonoEditorFiles = Path.Combine(options.PlatformsFolder, "Editor", platform.ToString(), "Mono");
                CloneDirectory(Path.Combine(buildDir, "etc"), Path.Combine(dstMonoEditorFiles, "etc"));

                // Copy libs
                var dstMonoEditorLibs = Path.Combine(dstMonoEditorFiles, "lib", "mono");
                SetupDirectory(dstMonoEditorLibs, true);
                Utilities.DirectoryCopy(Path.Combine(srcMonoLibs, "4.5"), Path.Combine(dstMonoEditorLibs, "4.5"), false, true, "*.dll");
                Utilities.DirectoryCopy(Path.Combine(srcMonoLibs, "gac"), Path.Combine(dstMonoEditorLibs, "gac"), true, true, "*.dll");
                Utilities.FileCopy(Path.Combine(srcMonoLibs, "4.5", "csc.exe"), Path.Combine(dstMonoEditorLibs, "4.5", "csc.exe"));
                Utilities.DirectoryCopy(Path.Combine(srcMonoLibs, "4.5", "Facades"), Path.Combine(dstMonoEditorLibs, "4.5", "Facades"), false, true, "*.dll");
                Utilities.DirectoryCopy(Path.Combine(srcMonoLibs, "4.8-api"), Path.Combine(dstMonoEditorLibs, "4.8-api"), false, true, "*.dll");
                Utilities.DirectoryCopy(Path.Combine(srcMonoLibs, "xbuild", "14.0"), Path.Combine(dstMonoEditorLibs, "xbuild", "14.0"), true, true);
                Utilities.DirectoryCopy(Path.Combine(srcMonoLibs, "xbuild-frameworks", ".NETFramework", "v4.8"), Path.Combine(dstMonoEditorLibs, "xbuild-frameworks", ".NETFramework", "v4.8"), true, true);
                Utilities.FilesDelete(dstMonoEditorLibs, "*.pdb");

                // Remove unused libs
                foreach (var gameUsedLib in unusedLibs)
                {
                    Utilities.FilesDelete(dstMonoEditorLibs, gameUsedLib);
                    if (!gameUsedLib.EndsWith("*"))
                        Utilities.FilesDelete(dstMonoEditorLibs, gameUsedLib + ".dll");
                    Utilities.DirectoriesDelete(dstMonoEditorLibs, gameUsedLib);
                }
            }
        }

        /// <inheritdoc />
        public override void Build(BuildOptions options)
        {
            // Ensure the build platform is setup correctly
            if (BuildPlatform == TargetPlatform.Windows)
            {
                RunBash("git", "--version");
                RunBash("git", "lfs --version");
                RunBash("make", "-v");
            }

            // Set it to the local path of the mono rep oto use for the build instead of cloning the remote one (helps with rapid testing)
            string localRepoPath = string.Empty;
            //localRepoPath = @"D:\Flax\3rdParty\mono";

            root = options.IntermediateFolder;
            if (!string.IsNullOrEmpty(localRepoPath))
                root = localRepoPath;
            monoPropsPath = Path.Combine(root, "msvc", "mono.props");

            // Get the source
            if (string.IsNullOrEmpty(localRepoPath) && !Directory.Exists(Path.Combine(root, ".git")))
                CloneGitRepo(root, "https://github.com/FlaxEngine/mono.git", null, "--recursive");

            foreach (var platform in options.Platforms)
            {
                // Pick a proper branch
                GitCheckout(root, "flax-master-6-13");
                GitResetLocalChanges(root);

                // Get the default preprocessor defines for Mono on Windows-based platforms
                if (monoPreprocesorDefines == null)
                {
                    var monoProps = new XmlDocument();
                    monoProps.Load(monoPropsPath);
                    monoPreprocesorDefines = FindXmlNodeValue(monoProps, "MONO_PREPROCESSOR_DEFINITIONS");
                }

                // Setup build directory
                var buildDir = Path.Combine(root, "build", platform.ToString());
                SetupDirectory(buildDir, false);

                switch (platform)
                {
                case TargetPlatform.Windows:
                {
                    var monoBinaries = "C:\\Program Files\\Mono\\bin";
                    if (!File.Exists(Path.Combine(monoBinaries, "mono.exe")))
                        throw new Exception("Missing Mono! Install it to Program Files.");
                    var envVars = new Dictionary<string, string>
                    {
                        { "PATH", monoBinaries },
                        //{ "MONO_EXECUTABLE", Path.Combine("msvc/build/sgen/x64/bin/Release/mono.exe").Replace('\\', '/') },
                        //{ "MONO_EXECUTABLE", Path.Combine(monoBinaries, "mono.exe").Replace('\\', '/') },
                    };
                    var monoOptions = new[]
                    {
                        "--disable-boehm",
                        "--disable-crash-reporting",
                        //"--disable-btls",
                        "--enable-btls",
                        "--enable-btls-lib",
                        //"--enable-msvc",
                        "--enable-maintainer-mode",
                        "--with-crash-privacy=no",
                        "--with-mcs-docs=no",
                        "--without-ikvm-native",
                        "\"CFLAGS=-g -O2\"",
                        "\"CXXFLAGS=-g -O2\"",
                        "\"CPPFLAGS=-g -O2\"",
                    };

                    // Build mono
                    Autogen("x86_64-w64-mingw32", monoOptions, buildDir, envVars);
                    BuildMsvc(options, platform, TargetArchitecture.x64);
                    Make(envVars);

                    // Get exported mono methods to forward them in engine module (on Win32 platforms)
                    GetMonoExports(options);

                    // Deploy data
                    var editorBinOutput = Path.Combine(options.PlatformsFolder, "Editor", platform.ToString(), "Mono", "bin");
                    SetupDirectory(editorBinOutput, true);
                    Utilities.FileCopy(Path.Combine(root, "msvc\\build\\sgen\\x64\\bin\\Release", "mono.exe"), Path.Combine(editorBinOutput, "mono.exe"));
                    Utilities.FileCopy(Path.Combine(monoBinaries, "csc.bat"), Path.Combine(editorBinOutput, "csc.bat"));
                    //CloneDirectory(Path.Combine(buildDir, "include", "mono-2.0", "mono"), Path.Combine(options.ThirdPartyFolder, "mono-2.0", "mono"));
                    CloneDirectory(Path.Combine(root, "msvc\\include\\mono"), Path.Combine(options.ThirdPartyFolder, "mono-2.0", "mono"));
                    DeployData(options, platform, buildDir, true);
                    break;
                }
                case TargetPlatform.UWP:
                case TargetPlatform.XboxOne:
                case TargetPlatform.XboxScarlett:
                {
                    if (platform == TargetPlatform.UWP)
                        ConfigureMsvc(options, "v141", "10.0.17763.0", "0x0A00", "_UWP=1;DISABLE_JIT;WINAPI_FAMILY=WINAPI_FAMILY_PC_APP;HAVE_EXTERN_DEFINED_WINAPI_SUPPORT");
                    else
                        ConfigureMsvc(options, "v142", "10.0.19041.0", "0x0A00", "_XBOX_ONE=1;DISABLE_JIT;WINAPI_FAMILY=WINAPI_FAMILY_GAMES;HAVE_EXTERN_DEFINED_WINAPI_SUPPORT;CRITICAL_SECTION_NO_DEBUG_INFO=0x01000000");

                    BuildMsvc(options, platform, TargetArchitecture.x64);

                    // Mirror Mono data and libs from Windows
                    CloneDirectory(Path.Combine(Path.Combine(options.PlatformsFolder, TargetPlatform.Windows.ToString(), "Binaries", "Mono")), Path.Combine(Path.Combine(options.PlatformsFolder, platform.ToString(), "Binaries", "Mono")));
                    break;
                }
                case TargetPlatform.Linux:
                {
                    var envVars = new Dictionary<string, string>
                    {
                        { "CC", "clang-7" },
                        { "CXX", "clang++-7" }
                    };
                    var monoOptions = new[]
                    {
                        "--with-xen-opt=no",
                        "--without-ikvm-native",
                        "--disable-boehm",
                        //"--disable-mcs-build",
                        "--with-mcs-docs=no",
                        //"--enable-static",
                    };

                    // Build mono
                    var hostName = UnixToolchain.GetToolchainName(platform, TargetArchitecture.x64);
                    Autogen(hostName, monoOptions, buildDir, envVars);
                    Make(envVars);

                    // Deploy binaries
                    var depsFolder = GetThirdPartyFolder(options, platform, TargetArchitecture.x64);
                    Utilities.FileCopy(Path.Combine(buildDir, "lib", "libmonosgen-2.0.so.1.0.0"), Path.Combine(depsFolder, "libmonosgen-2.0.so.1.0.0"));
                    var gameLibOutput = Path.Combine(options.PlatformsFolder, "Linux", "Binaries", "Mono", "lib");
                    Utilities.FileCopy(Path.Combine(buildDir, "lib", "libMonoPosixHelper.so"), Path.Combine(gameLibOutput, "libMonoPosixHelper.so"));
                    Utilities.FileCopy(Path.Combine(buildDir, "lib", "libmono-native.so"), Path.Combine(gameLibOutput, "libmono-native.so"));
                    var editorLibOutput = Path.Combine(options.PlatformsFolder, "Editor", "Linux", "Mono", "lib");
                    Utilities.FileCopy(Path.Combine(buildDir, "lib", "libMonoPosixHelper.so"), Path.Combine(editorLibOutput, "libMonoPosixHelper.so"));
                    Utilities.FileCopy(Path.Combine(buildDir, "lib", "libmono-native.so"), Path.Combine(editorLibOutput, "libmono-native.so"));
                    var editorBinOutput = Path.Combine(options.PlatformsFolder, "Editor", "Linux", "Mono", "bin");
                    Utilities.FileCopy(Path.Combine(buildDir, "bin", "mono-sgen"), Path.Combine(editorBinOutput, "mono"));
                    Utilities.Run("strip", "mono", null, editorBinOutput, Utilities.RunOptions.None);
                    DeployData(options, platform, buildDir, true);
                    break;
                }
                case TargetPlatform.Android:
                {
                    var sdk = AndroidSdk.Instance.RootPath;
                    var ndk = AndroidNdk.Instance.RootPath;
                    var apiLevel = Configuration.AndroidPlatformApi.ToString();
                    var archName = UnixToolchain.GetToolchainName(platform, TargetArchitecture.ARM64);
                    var toolchainRoot = Path.Combine(ndk, "toolchains", "llvm", "prebuilt", AndroidSdk.GetHostName());
                    var ndkBin = Path.Combine(toolchainRoot, "bin");
                    var compilerFlags = string.Format("-DANDROID -DMONODROID=1 -DANDROID64 -D__ANDROID_API__={0} --sysroot=\"{1}/sysroot\" --gcc-toolchain=\"{1}\"", apiLevel, toolchainRoot);
                    var envVars = new Dictionary<string, string>
                    {
                        { "ANDROID_SDK_ROOT", sdk },
                        { "ANDROID_SDK", sdk },
                        { "ANDROID_NDK_ROOT", ndk },
                        { "ANDROID_NDK", ndk },
                        { "NDK", ndk },
                        { "NDK_BIN", ndkBin },
                        { "ANDROID_PLATFORM", "android-" + apiLevel },
                        { "ANDROID_API", apiLevel },
                        { "ANDROID_API_VERSION", apiLevel },
                        { "ANDROID_NATIVE_API_LEVEL", apiLevel },

                        { "CC", Path.Combine(ndkBin, archName + apiLevel + "-clang") },
                        { "CXX", Path.Combine(ndkBin, archName + apiLevel + "-clang++") },
                        { "AR", Path.Combine(ndkBin, archName + "-ar") },
                        { "AS", Path.Combine(ndkBin, archName + "-as") },
                        { "LD", Path.Combine(ndkBin, archName + "-ld") },
                        { "RANLIB", Path.Combine(ndkBin, archName + "-ranlib") },
                        { "STRIP", Path.Combine(ndkBin, archName + "-strip") },
                        { "SYSROOT", toolchainRoot },
                        { "CFLAGS", compilerFlags },
                        { "CXXFLAGS", compilerFlags },
                        { "CPPFLAGS", compilerFlags },
                    };
                    var monoOptions = new[]
                    {
                        "--disable-crash-reporting",
                        "--disable-executables",
                        "--disable-iconv",
                        "--disable-boehm",
                        "--disable-nls",
                        "--disable-mcs-build",
                        "--enable-maintainer-mode",
                        "--enable-dynamic-btls",
                        "--enable-monodroid",
                        "--with-btls-android-ndk",
                        "--with-sigaltstack=yes",
                        string.Format("--with-btls-android-ndk={0}", ndk),
                        string.Format("--with-btls-android-api={0}", apiLevel),
                        string.Format("--with-btls-android-cmake-toolchain={0}/build/cmake/android.toolchain.cmake", ndk),
                        "--without-ikvm-native",
                    };
                    var binaries = new[]
                    {
                        "lib/libmonosgen-2.0.so",
                    };

                    // Compile mono
                    var hostName = UnixToolchain.GetToolchainName(platform, TargetArchitecture.ARM64);
                    Autogen(hostName, monoOptions, buildDir, envVars);
                    Make(envVars);
                    var depsFolder = GetThirdPartyFolder(options, platform, TargetArchitecture.ARM64);
                    foreach (var binary in binaries)
                    {
                        var src = Path.Combine(buildDir, binary);
                        var dst = Path.Combine(depsFolder, Path.GetFileName(binary));
                        Utilities.FileCopy(src, dst);
                    }

                    // Clean before another build
                    GitResetLocalChanges(root);
                    Utilities.Run("make", "distclean", null, root, Utilities.RunOptions.None);

                    // Compile BCL
                    var installBcl = Path.Combine(root, "bcl-android");
                    var bclOutput = Path.Combine(GetBinariesFolder(options, platform), "Mono");
                    var bclLibOutput = Path.Combine(bclOutput, "lib");
                    var bclLibMonoOutput = Path.Combine(bclLibOutput, "mono");
                    SetupDirectory(installBcl, true);
                    SetupDirectory(bclOutput, true);
                    SetupDirectory(bclLibOutput, false);
                    SetupDirectory(bclLibMonoOutput, false);
                    Utilities.DirectoryCopy(Path.Combine(buildDir, "etc"), Path.Combine(bclOutput, "etc"), true, true);
                    Utilities.FileCopy(Path.Combine(buildDir, "lib", "libMonoPosixHelper.so"), Path.Combine(bclLibOutput, "libMonoPosixHelper.so"));
                    Utilities.FileCopy(Path.Combine(buildDir, "lib", "libmono-native.so"), Path.Combine(bclLibOutput, "libmono-native.so"));
                    Utilities.FileCopy(Path.Combine(buildDir, "lib", "libmono-btls-shared.so"), Path.Combine(bclLibOutput, "libmono-btls-shared.so"));
                    var bclOptions = new[]
                    {
                        "--disable-boehm",
                        "--disable-btls-lib",
                        "--disable-nls",
                        "--disable-support-build",
                        "--with-mcs-docs=no",
                    };
                    Utilities.Run(Path.Combine(root, "autogen.sh"), string.Format("--prefix={0} {1}", installBcl, string.Join(" ", bclOptions)), null, root);
                    Utilities.Run("make", $"-j1 -C {root} -C mono", null, root, Utilities.RunOptions.None);
                    Utilities.Run("make", $"-j2 -C {root} -C runtime all-mcs build_profiles=monodroid", null, root, Utilities.RunOptions.None);
                    Utilities.DirectoryCopy(Path.Combine(root, "mcs", "class", "lib", "monodroid"), Path.Combine(bclLibMonoOutput, "2.1"), true, true, "*.dll");
                    Utilities.DirectoryDelete(Path.Combine(bclLibMonoOutput, "2.1", "Facades"));
                    break;
                }
                case TargetPlatform.Switch:
                {
                    var type = Type.GetType("Flax.Build.Platforms.Switch.mono");
                    var method = type.GetMethod("Build");
                    method.Invoke(null, new object[] { root, options });
                    break;
                }
                case TargetPlatform.Mac:
                {
                    var compilerFlags = string.Format("-mmacosx-version-min={0}", Configuration.MacOSXMinVer);
                    var envVars = new Dictionary<string, string>
                    {
                        { "CFLAGS", compilerFlags },
                        { "CXXFLAGS", compilerFlags },
                        { "CPPFLAGS", compilerFlags },
                    };
                    var monoOptions = new[]
                    {
                        "--with-xen-opt=no",
                        "--without-ikvm-native",
                        "--disable-boehm",
                        "--disable-nls",
                        "--disable-iconv",
                        //"--disable-mcs-build",
                        "--with-mcs-docs=no",
                        "--with-tls=pthread",
                        //"--enable-static",
                        "--enable-maintainer-mode",
                    };

                    // Build mono
                    var hostName = "x86_64-apple-darwin18";
                    Autogen(hostName, monoOptions, buildDir, envVars);
                    Make(envVars);

                    // Deploy binaries
                    var depsFolder = GetThirdPartyFolder(options, platform, TargetArchitecture.x64);
                    Directory.CreateDirectory(depsFolder);
                    DeployDylib(Path.Combine(buildDir, "lib", "libmonosgen-2.0.1.dylib"), depsFolder);
                    var gameLibOutput = Path.Combine(options.PlatformsFolder, "Mac", "Binaries", "Mono", "lib");
                    Directory.CreateDirectory(gameLibOutput);
                    DeployDylib(Path.Combine(buildDir, "lib", "libMonoPosixHelper.dylib"), gameLibOutput);
                    DeployDylib(Path.Combine(buildDir, "lib", "libmono-btls-shared.dylib"), gameLibOutput);
                    DeployDylib(Path.Combine(buildDir, "lib", "libmono-native.dylib"), gameLibOutput);
                    var editorLibOutput = Path.Combine(options.PlatformsFolder, "Editor", "Mac", "Mono", "lib");
                    Directory.CreateDirectory(editorLibOutput);
                    DeployDylib(Path.Combine(buildDir, "lib", "libMonoPosixHelper.dylib"), editorLibOutput);
                    DeployDylib(Path.Combine(buildDir, "lib", "libmono-btls-shared.dylib"), editorLibOutput);
                    DeployDylib(Path.Combine(buildDir, "lib", "libmono-native.dylib"), editorLibOutput);
                    var editorBinOutput = Path.Combine(options.PlatformsFolder, "Editor", "Mac", "Mono", "bin");
                    Directory.CreateDirectory(editorBinOutput);
                    Utilities.FileCopy(Path.Combine(buildDir, "bin", "mono-sgen"), Path.Combine(editorBinOutput, "mono"));
                    Utilities.Run("strip", "mono", null, editorBinOutput, Utilities.RunOptions.None);
                    DeployData(options, platform, buildDir, true);
                    break;
                }
                }
            }
        }

        private static readonly string[] MonoExportsCustom =
        {
            "mono_value_box",
            "mono_add_internal_call",
        };

        private static readonly string[] MonoExportsIncludePrefixes =
        {
            "mono_free",
            "mono_array_",
            "mono_assembly_",
            "mono_class_",
            "mono_custom_attrs_",
            "mono_debug_",
            "mono_domain_",
            "mono_exception_",
            "mono_field_",
            "mono_free_",
            "mono_gc_",
            "mono_gchandle_",
            "mono_get_",
            "mono_image_",
            "mono_metadata_",
            "mono_method_",
            "mono_object_",
            "mono_profiler_",
            "mono_property_",
            "mono_raise_exception",
            "mono_reflection_",
            "mono_runtime_",
            "mono_signature_",
            "mono_stack_",
            "mono_string_",
            "mono_thread_",
            "mono_type_",
            "mono_value_",
        };

        private static readonly string[] MonoExportsSkipPrefixes =
        {
            "mono_type_to_",
            "mono_thread_state_",
            "mono_thread_internal_",
            "mono_thread_info_",
            "mono_array_get_",
            "mono_array_to_byvalarray",
            "mono_array_handle_length",
            "mono_array_addr_with_size_internal",
            "mono_array_class_get_cached_function",
            "mono_array_new_",
            "mono_assembly_apply_binding",
            "mono_assembly_bind_version",
            "mono_assembly_binding_",
            "mono_assembly_get_alc",
            "mono_assembly_invoke_search_hook_internal",
            "mono_assembly_is_in_gac",
            "mono_assembly_load_from_gac",
            "mono_assembly_load_from_assemblies_path",
            "mono_assembly_load_full_gac_base_default",
            "mono_assembly_load_publisher_policy",
            "mono_assembly_name_from_token",
            "mono_assembly_remap_version",
            "mono_assembly_try_decode_skip_verification",
            "mono_assembly_request_byname_nosearch",
            "mono_assembly_request_prepare",
            "mono_class_create_",
            "mono_class_from_name_checked_aux",
            "mono_class_get_appdomain_do_type_resolve_method",
            "mono_class_get_appdomain_do_type_builder_resolve_method",
            "mono_class_get_exception_handling_clause_class",
            "mono_class_get_field_idx",
            "mono_class_get_local_variable_info_class",
            "mono_class_get_assembly_class",
            "mono_class_get_asyncresult_class",
            "mono_class_get_call_context_class",
            "mono_class_get_class_interface_attribute_class",
            "mono_class_get_com_",
            "mono_class_get_context_class",
            "mono_class_get_culture_info_class",
            "mono_class_get_custom_attribute_",
            "mono_class_get_date_time_class",
            "mono_class_get_dbnull_class",
            "mono_class_get_fixed_buffer_attribute_class",
            "mono_class_get_geqcomparer_class",
            "mono_class_get_guid_attribute_class",
            "mono_class_get_icollection_class",
            "mono_class_get_ienumerable_class",
            "mono_class_get_iequatable_class",
            "mono_class_get_interface_type_attribute_class",
            "mono_class_get_ireadonly_collection_class",
            "mono_class_get_ireadonly_list_class",
            "mono_class_get_marshal_as_attribute_class",
            "mono_class_get_missing_class",
            "mono_class_get_module_builder_class",
            "mono_class_get_module_class",
            "mono_class_get_mono_assembly_class",
            "mono_class_get_mono_cmethod_class",
            "mono_class_get_mono_event_class",
            "mono_class_get_mono_field_class",
            "mono_class_get_mono_method_class",
            "mono_class_get_mono_module_class",
            "mono_class_get_mono_property_class",
            "mono_class_get_nullref_class",
            "mono_class_get_remoting_services_class",
            "mono_class_get_runtime_compat_attr_class",
            "mono_class_get_runtime_helpers_class",
            "mono_class_get_security_critical_class",
            "mono_class_get_security_manager_class",
            "mono_class_get_security_safe_critical_class",
            "mono_class_get_sta_thread_attribute_class",
            "mono_class_get_type_builder_class",
            "mono_class_get_unhandled_exception_event_args_class",
            "mono_class_get_valuetuple_0_class",
            "mono_class_get_valuetuple_1_class",
            "mono_class_get_valuetuple_2_class",
            "mono_class_get_valuetuple_3_class",
            "mono_class_get_valuetuple_4_class",
            "mono_class_get_valuetuple_5_class",
            "mono_class_get_pointer_class",
            "mono_class_get_activation_services_class",
            "mono_class_get_runtime_generic_context_template",
            "mono_class_get_virtual_methods",
            "mono_class_get_magic_index",
            "mono_class_get_method_body_class",
            "mono_class_get_mono_parameter_info_class",
            "mono_class_has_default_constructor",
            "mono_class_has_gtd_parent",
            "mono_class_has_parent",
            "mono_class_implement_interface_slow",
            "mono_class_is_ginst",
            "mono_class_is_gtd",
            "mono_class_is_interface",
            "mono_class_is_magic_assembly",
            "mono_class_is_valid_generic_instantiation",
            "mono_class_is_variant_compatible_slow",
            "mono_class_need_stelemref_method",
            "mono_class_proxy_vtable",
            "mono_class_setup_vtable_full",
            "mono_class_setup_interface_id_internal",
            "mono_class_try_get_unmanaged_function_pointer_attribute_class",
            "mono_class_try_get_math_class",
            "mono_class_unregister_image_generic_subclasses",
            "mono_custom_attrs_construct_by_type",
            "mono_custom_attrs_data_construct",
            "mono_custom_attrs_from_builders_handle",
            "mono_debug_add_assembly",
            "mono_debug_format",
            "mono_debug_handles",
            "mono_debug_initialized",
            "mono_debug_log_thread_state_to_string",
            "mono_debug_open_image",
            "mono_debug_read_method",
            "mono_domain_asmctx_from_path",
            "mono_domain_assembly_preload",
            "mono_domain_assembly_search",
            "mono_domain_create_appdomain_checked",
            "mono_domain_create_appdomain_internal",
            "mono_domain_fire_assembly_load",
            "mono_domain_fire_assembly_unload",
            "mono_domain_from_appdomain_handle",
            "mono_domain_code_",
            "mono_exception_new_argument_internal",
            "mono_exception_new_by_name_domain",
            "mono_exception_stacktrace_obj_walk",
            "mono_field_get_addr",
            "mono_field_get_rva",
            "mono_field_resolve_flags",
            "mono_free_static_data",
            "mono_gc_init_finalizer_thread",
            "mono_get_array_new_va_signature",
            "mono_get_corlib_version",
            "mono_get_domainvar",
            "mono_get_exception_argument_internal",
            "mono_get_exception_missing_member",
            "mono_get_exception_runtime_wrapped_checked",
            "mono_get_exception_type_initialization_checked",
            "mono_get_field_token",
            "mono_get_int_type",
            "mono_get_method_from_token",
            "mono_get_reflection_missing_object",
            "mono_get_runtime_build_version",
            "mono_get_seq_point_for_native_offset",
            "mono_get_unique_iid",
            "mono_get_version_info",
            "mono_get_vtable_var",
            "mono_get_xdomain_marshal_type",
            "mono_image_add_cattrs",
            "mono_image_add_decl_security",
            "mono_image_add_memberef_row",
            "mono_image_add_methodimpl",
            "mono_image_basic_method",
            "mono_image_create_method_token",
            "mono_image_emit_manifest",
            "mono_image_fill_export_table",
            "mono_image_fill_export_table_from_class",
            "mono_image_fill_export_table_from_module",
            "mono_image_fill_export_table_from_type_forwarders",
            "mono_image_fill_file_table",
            "mono_image_fill_module_table",
            "mono_image_get_array_token",
            "mono_image_get_event_info",
            "mono_image_get_field_info",
            "mono_image_get_fieldref_token",
            "mono_image_get_generic_param_info",
            "mono_image_get_method_info",
            "mono_image_get_methodspec_token",
            "mono_image_get_property_info",
            "mono_image_get_type_info",
            "mono_image_walk_resource_tree",
            "mono_image_storage_",
            "mono_metadata_custom_modifiers_",
            "mono_metadata_class_equal",
            "mono_metadata_field_info_full",
            "mono_metadata_fnptr_equal",
            "mono_metadata_generic_param_equal_internal",
            "mono_metadata_parse_array_internal",
            "mono_metadata_parse_generic_param",
            "mono_metadata_parse_type_internal",
            "mono_metadata_signature_dup_internal_with_padding",
            "mono_metadata_signature_vararg_match",
            "mono_method_check_inlining",
            "mono_method_get_equivalent_method",
            "mono_method_is_constructor",
            "mono_method_is_valid_generic_instantiation",
            "mono_method_is_valid_in_context",
            "mono_method_securestring_",
            "mono_method_signature_checked",
            "mono_method_signature_internal",
            "mono_object_new_by_vtable",
            "mono_object_get_data",
            "mono_object_unbox_internal",
            "mono_raise_exception_with_ctx",
            "mono_reflection_get_type_internal",
            "mono_reflection_get_type_internal_dynamic",
            "mono_reflection_get_type_with_rootimage",
            "mono_reflection_method_get_handle",
            "mono_reflection_type_get_underlying_system_type",
            "mono_reflection_cleanup_",
            "mono_runtime_capture_context",
            "mono_runtime_delegate_try_invoke_handle",
            "mono_runtime_set_execution_mode",
            "mono_runtime_walk_stack_with_ctx",
            "mono_runtime_create_",
            "mono_signature_to_name",
            "mono_string_builder_new",
            "mono_string_from_bstr_common",
            "mono_string_get_pinned",
            "mono_string_is_interned_lookup",
            "mono_string_new_utf32_checked",
            "mono_string_to_utf8_internal",
            "mono_string_chars_internal",
            "mono_string_length_internal",
            "mono_string_new_internal",
            "mono_string_utf16_to_builder_copy",
            "mono_string_utf16len_to_builder",
            "mono_string_utf8len_to_builder",
            "mono_thread_abort",
            "mono_thread_abort_dummy",
            "mono_thread_attach_cb",
            "mono_thread_attach_internal",
            "mono_thread_cleanup_fn",
            "mono_thread_clear_interruption_requested",
            "mono_thread_create_internal",
            "mono_thread_create_internal_handle",
            "mono_thread_current_for_thread",
            "mono_thread_detach_internal",
            "mono_thread_execute_interruption",
            "mono_thread_execute_interruption_ptr",
            "mono_thread_execute_interruption_void",
            "mono_thread_get_managed_sp",
            "mono_thread_get_name",
            "mono_thread_resume",
            "mono_thread_set_interruption_requested",
            "mono_thread_set_name_",
            "mono_thread_start_cb",
            "mono_thread_suspend",
            "mono_type_array_get_and_resolve_raw",
            "mono_type_array_get_and_resolve_with_modifiers",
            "mono_type_elements_shift_bits",
            "mono_type_equal",
            "mono_type_from_opcode",
            "mono_type_get_type_",
            "mono_type_get_name_recurse",
            "mono_type_get_underlying_type_",
            "mono_type_hash",
            "mono_type_init_",
            "mono_type_initialization_",
            "mono_type_is_enum_type",
            "mono_type_is_byref_internal",
            "mono_type_is_generic_argument",
            "mono_type_is_valid_type_in_context",
            "mono_type_is_value_type",
            "mono_type_is_native_blittable",
            "mono_type_is_valid_in_context",
            "mono_type_is_valid_type_in_context_full",
            "mono_type_normalize",
            "mono_type_name_check_byref",
            "mono_type_retrieve_from_typespec",
            "mono_type_with_mods_init",
            "mono_value_hash_table_",
        };
    }
}
