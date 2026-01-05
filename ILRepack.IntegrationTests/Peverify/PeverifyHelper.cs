using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ILRepack.IntegrationTests.Helpers;

namespace ILRepack.IntegrationTests.Peverify
{
    public static class PeverifyHelper
    {
        public const string META_E_CA_FRIENDS_SN_REQUIRED = "801311e6";
        public const string VER_E_TOKEN_RESOLVE = "80131869";
        public const string VER_E_TYPELOAD = "801318f3";
        public const string VER_E_STACK_OVERFLOW = "80131856";

        // ILVerify patterns
        static Regex VerificationPassed = new Regex(@"All Classes and Methods in .* Verified\.?");
        static Regex ILVerifyError = new Regex(@"\[IL\]: Error \[");
        
        public static async Task<List<string>> PeverifyAsync(string workingDirectory, params string[] args)
        {
            // Use dotnet ilverify instead of legacy peverify.exe
            var assemblyPaths = args.Select(arg => Path.Combine(workingDirectory, arg)).ToList();
            
            // ILVerify requires explicit reference assemblies, so we need to find and include them
            var allReferencePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            // 1. Include DLLs from the working directory
            foreach (var dll in Directory.GetFiles(workingDirectory, "*.dll"))
            {
                if (!assemblyPaths.Contains(dll, StringComparer.OrdinalIgnoreCase))
                {
                    allReferencePaths.Add(dll);
                }
            }
            
            // 2. Include reference assemblies from test bin directory (NuGet packages)
            var testBinDir = AppContext.BaseDirectory;
            if (Directory.Exists(testBinDir))
            {
                foreach (var dll in Directory.GetFiles(testBinDir, "*.dll", SearchOption.TopDirectoryOnly))
                {
                    var fileName = Path.GetFileName(dll);
                    // Skip test assemblies themselves
                    if (!fileName.StartsWith("ILRepack.", StringComparison.OrdinalIgnoreCase) &&
                        !fileName.StartsWith("Microsoft.TestPlatform.", StringComparison.OrdinalIgnoreCase) &&
                        !fileName.StartsWith("NUnit", StringComparison.OrdinalIgnoreCase) &&
                        !fileName.Equals("testhost.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        allReferencePaths.Add(dll);
                    }
                }
            }
            
            // 3. Include .NET runtime references
            var runtimeRefs = GetRuntimeReferences();
            foreach (var runtimeRef in runtimeRefs)
            {
                allReferencePaths.Add(runtimeRef);
            }
            
            // Build the ilverify arguments
            var referencesArg = string.Join(" ", allReferencePaths.Select(r => $"-r \"{r}\""));
            var assembliesArg = string.Join(" ", assemblyPaths.Select(p => $"\"{p}\""));
            
            var info = new ProcessStartInfo
            {
                CreateNoWindow = true,
                FileName = "dotnet",
                Arguments = $"ilverify {assembliesArg} {referencesArg}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = workingDirectory
            };
            
            var process = new AsyncProcess(info, throwOnNonZeroExitCode: false);
            var (_, output) = await process.RunAsync();
            
            // Filter out success messages and return only error lines
            return output.Where(s => !string.IsNullOrWhiteSpace(s) && 
                                     !VerificationPassed.IsMatch(s) &&
                                     (ILVerifyError.IsMatch(s) || s.Contains("Error"))).ToList();
        }

        private static IEnumerable<string> GetRuntimeReferences()
        {
            // Try to find the .NET runtime reference assemblies
            // This helps ilverify resolve system types
            try
            {
                var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
                if (string.IsNullOrEmpty(dotnetRoot))
                {
                    // Try common locations
                    if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                    {
                        dotnetRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
                    }
                    else
                    {
                        dotnetRoot = "/usr/share/dotnet";
                        if (!Directory.Exists(dotnetRoot))
                        {
                            dotnetRoot = "/usr/local/share/dotnet";
                        }
                    }
                }
                
                if (Directory.Exists(dotnetRoot))
                {
                    var sharedPath = Path.Combine(dotnetRoot, "shared", "Microsoft.NETCore.App");
                    if (Directory.Exists(sharedPath))
                    {
                        // Get the latest version directory
                        var versionDirs = Directory.GetDirectories(sharedPath)
                            .Select(d => new { Path = d, Version = TryParseVersion(Path.GetFileName(d)) })
                            .Where(x => x.Version != null)
                            .OrderByDescending(x => x.Version)
                            .ToList();
                        
                        if (versionDirs.Any())
                        {
                            var latestPath = versionDirs[0].Path;
                            return Directory.GetFiles(latestPath, "*.dll");
                        }
                    }
                }
            }
            catch
            {
                // If we can't find runtime references, continue without them
                // ilverify will still do basic verification
            }
            
            return Enumerable.Empty<string>();
        }

        private static Version TryParseVersion(string versionString)
        {
            try
            {
                return new Version(versionString);
            }
            catch
            {
                return null;
            }
        }

        public static List<string> ToErrorCodes(this IEnumerable<string> output)
        {
            var errorCodes = new List<string>();
            
            foreach (var e in output)
            {
                var code = TryExtractErrorCode(e);
                if (code != null)
                {
                    errorCodes.Add(code);
                }
            }
            
            return errorCodes.Distinct().ToList();
        }

        private static string TryExtractErrorCode(string errorLine)
        {
            // Try legacy PEVerify.exe format: [HRESULT 0x80131869]
            var code = TryExtractHexCode(errorLine, "[HRESULT 0x", 11, 8);
            if (code != null) return code;
            
            // Try legacy PEVerify.exe format: [MD](0x80131869)
            code = TryExtractHexCode(errorLine, "[MD](0x", 7, 8);
            if (code != null) return code;
            
            // Try legacy PEVerify.exe format: (Error: 0x80131869)
            code = TryExtractHexCode(errorLine, "(Error: 0x", 10, 8);
            if (code != null) return code;
            
            // Map ILVerify error messages to legacy HRESULT codes for compatibility
            return MapILVerifyErrorToHResult(errorLine);
        }

        private static string TryExtractHexCode(string text, string prefix, int offset, int length)
        {
            var index = text.IndexOf(prefix);
            if (index != -1 && index + offset + length <= text.Length)
            {
                return text.Substring(index + offset, length).ToLowerInvariant();
            }
            return null;
        }

        private static string MapILVerifyErrorToHResult(string errorLine)
        {
            if (errorLine.Contains("StackOverflow"))
                return VER_E_STACK_OVERFLOW;
            
            if (errorLine.Contains("TypeLoad") || errorLine.Contains("Unable to resolve"))
                return VER_E_TYPELOAD;
            
            if (errorLine.Contains("token") || errorLine.Contains("Token"))
                return VER_E_TOKEN_RESOLVE;
            
            if (errorLine.Contains("InternalsVisibleTo") || errorLine.Contains("friend"))
                return META_E_CA_FRIENDS_SN_REQUIRED;
            
            return null;
        }
    }
}
