using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace ILRepacking
{
    public enum AssemblyResolverMode
    {
        Framework, Core
    }

    public class RepackAssemblyResolver : DefaultAssemblyResolver
    {
        private static AssemblyResolverMode BaseMode;

        static RepackAssemblyResolver()
        {
            var corlib = typeof(BaseAssemblyResolver).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance).FirstOrDefault(m => m.Name == "GetCorlib");
            if(corlib != null)
            {
                BaseMode = AssemblyResolverMode.Framework;
            }
            else
            {
                BaseMode = AssemblyResolverMode.Core;
            }
        }

        List<string> gac_paths;
        List<string> latest_core_paths;
        List<string> all_core_paths;
        public AssemblyResolverMode Mode { get; set; }

        public new void RegisterAssembly(AssemblyDefinition assembly)
        {
            base.RegisterAssembly(assembly);
        }

        public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
        {
            if(Mode == BaseMode)
            {
                //return base.Resolve(name, parameters);
            }

            if (Mode == AssemblyResolverMode.Core)
            { 
                return ResolveCore(name, parameters); 
            }
            else
            { 
                return ResolveFramework(name, parameters);
            }
        }

        private AssemblyDefinition ResolveBase(ref AssemblyNameReference name, ReaderParameters parameters)
        {
            var assembly = SearchDirectory(name, GetSearchDirectories(), parameters);
            if (assembly != null)
                return assembly;

            if (name.IsRetargetable)
            {
                // if the reference is retargetable, zero it
                name = new AssemblyNameReference(name.Name, new Version(0, 0, 0, 0))
                {
                    PublicKeyToken = Array.Empty<byte>(),
                };
            }

            return null;
        }

        static bool IsZero(Version version)
        {
            return version.Major == 0 && version.Minor == 0 && version.Build == 0 && version.Revision == 0;
        }

        private AssemblyDefinition ResolveFramework(AssemblyNameReference name, ReaderParameters parameters)
        {
            var assembly = ResolveBase(ref name, parameters);
            if (assembly != null) return assembly;

            var framework_dir = GetFrameworkDir();

            var framework_dirs = new[] { framework_dir };

            if (IsZero(name.Version))
            {
                assembly = SearchDirectory(name, framework_dirs, parameters);
                if (assembly != null)
                    return assembly;
            }

            if (name.Name == "mscorlib")
            {
                assembly = GetCorlib(name, parameters);
                if (assembly != null)
                    return assembly;
            }

            assembly = GetAssemblyInGac(name, parameters);
            if (assembly != null)
                return assembly;

            assembly = SearchDirectory(name, framework_dirs, parameters);
            if (assembly != null)
                return assembly;

            throw new AssemblyResolutionException(name);
        }

        AssemblyDefinition GetAssembly(string file, ReaderParameters parameters)
        {
            if (parameters.AssemblyResolver == null)
                parameters.AssemblyResolver = this;

            return ModuleDefinition.ReadModule(file, parameters).Assembly;
        }


        private AssemblyDefinition SearchDirectoryCheckVersion(AssemblyNameReference name, IEnumerable<string> directories, ReaderParameters parameters)
        {
            var extensions = name.IsWindowsRuntime ? new[] { ".winmd", ".dll" } : new[] { ".exe", ".dll" };
            foreach (var directory in directories)
            {
                foreach (var extension in extensions)
                {
                    string file = Path.Combine(directory, name.Name + extension);
                    if (!File.Exists(file))
                        continue;
                    try
                    {
                        var assembly = GetAssembly(file, parameters);
                        if (IsZero(name.Version) || assembly.Name.Version == name.Version)
                        {
                            return assembly;
                        }
                    }
                    catch (System.BadImageFormatException)
                    {
                        continue;
                    }
                }
            }

            return null;
        }

        private string _frameworkDir;

        private void FindFrameworkDir(AssemblyNameReference corlib)
        {
            var version = corlib.Version;

            string path = GetFrameworkRootDir();

            switch (version.Major)
            {
                case 1:
                    if (version.MajorRevision == 3300)
                        path = Path.Combine(path, "v1.0.3705");
                    else
                        path = Path.Combine(path, "v1.1.4322");
                    break;
                case 2:
                    path = Path.Combine(path, "v2.0.50727");
                    break;
                case 4:
                    path = Path.Combine(path, "v4.0.30319");
                    break;
                default:
                    throw new NotSupportedException("Version not supported: " + version);
            }

            _frameworkDir = path;
        }

        private static string GetFrameworkRootDir()
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var path64 = Path.Combine(windows, "Microsoft.NET\\Framework64");
            if (Directory.Exists(path64)) return path64;

            var path = Path.Combine(windows, "Microsoft.NET\\Framework");
            if (Directory.Exists(path)) return path;


            throw new NotSupportedException("Current platform not supported. Unable to find .NET Framework root");
        }

        private string GetFrameworkDir()
        {
            if (_frameworkDir == null) throw new InvalidOperationException(".NET Framework is not found");

            return _frameworkDir;
        }

        AssemblyDefinition GetCorlib(AssemblyNameReference reference, ReaderParameters parameters)
        {
            var version = reference.Version;
            var corlib = typeof(object).Assembly.GetName();
            if (corlib.Name == reference.Name && (corlib.Version == version || IsZero(version)))
                return GetAssembly(typeof(object).Module.FullyQualifiedName, parameters);

            var path = GetFrameworkDir();

            var file = Path.Combine(path, "mscorlib.dll");
            if (File.Exists(file))
                return GetAssembly(file, parameters);

            return null;
        }


        AssemblyDefinition GetAssemblyInGac(AssemblyNameReference reference, ReaderParameters parameters)
        {
            if (reference.PublicKeyToken == null || reference.PublicKeyToken.Length == 0)
                return null;

            if (gac_paths == null)
                gac_paths = GetGacPaths();

            return GetAssemblyInNetGac(reference, parameters);
        }

        private AssemblyDefinition ResolveCore(AssemblyNameReference name, ReaderParameters parameters)
        {
            var assembly = ResolveBase(ref name, parameters);
            if (assembly != null) return assembly;
            
            if (all_core_paths == null)
                FindCoreSdkFolders();

            assembly = SearchDirectoryCheckVersion(name, all_core_paths, parameters);
            if (assembly != null)
                return assembly;

            assembly = SearchDirectory(name, all_core_paths, parameters);
            if (assembly != null)
                return assembly;

            throw new AssemblyResolutionException(name);
        }

        static List<string> GetGacPaths()
        {
            var paths = new List<string>(2);
            var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (windir == null)
                return paths;

            paths.Add(Path.Combine(windir, "assembly"));
            paths.Add(Path.Combine(windir, Path.Combine("Microsoft.NET", "assembly")));
            return paths;
        }

        AssemblyDefinition GetAssemblyInNetGac(AssemblyNameReference reference, ReaderParameters parameters)
        {
            var gacs = new[] { "GAC_MSIL", "GAC_32", "GAC_64", "GAC" };
            var prefixes = new[] { string.Empty, "v4.0_" };

            for (int i = 0; i < gac_paths.Count; i++)
            {
                for (int j = 0; j < gacs.Length; j++)
                {
                    var gac = Path.Combine(gac_paths[i], gacs[j]);
                    var file = GetAssemblyFile(reference, prefixes[i], gac);
                    if (Directory.Exists(gac) && File.Exists(file))
                        return GetAssembly(file, parameters);
                }
            }

            return null;
        }

        static string GetAssemblyFile(AssemblyNameReference reference, string prefix, string gac)
        {
            var gac_folder = new StringBuilder()
                .Append(prefix)
                .Append(reference.Version)
                .Append("__");

            for (int i = 0; i < reference.PublicKeyToken.Length; i++)
                gac_folder.Append(reference.PublicKeyToken[i].ToString("x2"));

            return Path.Combine(
                Path.Combine(
                    Path.Combine(gac, reference.Name), gac_folder.ToString()),
                reference.Name + ".dll");
        }

        internal void MatchTarget(ModuleDefinition module)
        {
            var corlib = module.TypeSystem.CoreLibrary;
            if (corlib.Name == "mscorlib")
            {
                Mode = AssemblyResolverMode.Framework;
                FindFrameworkDir(corlib as AssemblyNameReference);
            }
            else
            {
                Mode = AssemblyResolverMode.Core;
                FindCoreSdkFolders();
            }
            var moduleDir = Path.GetDirectoryName(module.FileName);
            if (!string.IsNullOrEmpty(moduleDir))
            {
                AddSearchDirectory(moduleDir);
            }
        }

        private void FindCoreSdkFolders()
        {
            var info = new ProcessStartInfo("dotnet", "--list-runtimes");
            info.RedirectStandardOutput = true;
            info.UseShellExecute = false;
            info.CreateNoWindow = true;

            try
            {
                using var ps = Process.Start(info);
                var reader = new StringReader(ps.StandardOutput.ReadToEnd());
                ps.WaitForExit();
                if (ps.ExitCode != 0)
                {
                    throw new Exception(".NET Core SDK list query failed with code" + ps.ExitCode);
                }

                Dictionary<string, string> lastestRuntimes = new Dictionary<string, string>();
                List<string> allRuntimes = new List<string>();

                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var pathStart = line.LastIndexOf('[') + 1;
                    var path = line.Substring(pathStart, line.LastIndexOf(']') - pathStart);
                    var runtimeInfo = line.Substring(0, pathStart - 1);
                    var parts = runtimeInfo.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    var fullPath = Path.Combine(path, parts[1]);
                    lastestRuntimes[parts[0]] = fullPath;
                    allRuntimes.Add(fullPath);
                }

                latest_core_paths = lastestRuntimes.Keys.ToList();
                allRuntimes.Reverse();
                all_core_paths = allRuntimes;
            }
            catch
            {
                throw new NotSupportedException(".NET Core SDK required to process Core assemblies");
            }
        }
    }
}
