using ILRepack.IntegrationTests.Peverify;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ILRepacking.Steps.SourceServerData;

namespace ILRepack.IntegrationTests.NuGet
{
    public class RepackNuGetTests
    {
        string tempDirectory;

        [OneTimeSetUp]
        public void RegisterCodepage()
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        }

        [SetUp]
        public void GenerateTempFolder()
        {
            tempDirectory = TestHelpers.GenerateTempFolder();
        }

        [TearDown]
        public void CleanupTempFolder()
        {
            TestHelpers.CleanupTempFolder(ref tempDirectory);
        }

        [TestCaseSource(typeof(Data), nameof(Data.Packages))]
        public async Task RoundtripNupkg(Package p)
        {
            var assemblies = await NuGetHelpers.GetNupkgAssembliesAsync(p);
            Assume.That(assemblies.Count, Is.GreaterThan(0));
            
            foreach (var (normalizedName, streamProvider) in assemblies)
            {
                TestHelpers.SaveAs(streamProvider(), tempDirectory, "foo.dll");
                RepackFoo(normalizedName);
                await VerifyTestAsync(new[] { "foo.dll" });
            }
        }

        [Category("LongRunning")]
        [Platform(Include = "win")]
        [TestCaseSource(typeof(Data), nameof(Data.Platforms), Category = "ComplexTests")]
        public async Task NupkgPlatform(Platform platform)
        {
            var fileList = new List<string>();
            
            foreach (var package in platform.Packages)
            {
                var assemblies = await NuGetHelpers.GetNupkgAssembliesAsync(package);
                foreach (var lib in assemblies)
                {
                    TestHelpers.SaveAs(lib.Item2(), tempDirectory, lib.Item1);
                    fileList.Add(Path.GetFileName(lib.Item1));
                }
            }
            
            RepackPlatform(platform, fileList);
            
            var errors = await PeverifyHelper.PeverifyAsync(tempDirectory, "test.dll");
            foreach (var error in errors)
            {
                Console.WriteLine(error);
            }
            
            var errorCodes = errors.ToErrorCodes();
            Assert.IsFalse(errorCodes.Contains(PeverifyHelper.VER_E_STACK_OVERFLOW));
        }

        [Test]
        [Platform(Include = "win")]
        public async Task VerifiesMergesBclFine()
        {
            var platform = Platform.From(
                Package.From("Microsoft.Bcl", "1.1.10")
                    .WithArtifact(@"lib\net40\System.Runtime.dll"),
                Package.From("Microsoft.Bcl", "1.1.10")
                    .WithArtifact(@"lib\net40\System.Threading.Tasks.dll"),
                Package.From("Microsoft.Bcl.Async", "1.0.168")
                    .WithArtifact(@"lib\net40\Microsoft.Threading.Tasks.dll"))
                .WithExtraArgs(@"/targetplatform:v4,C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0");

            var fileList = new List<string>();
            foreach (var package in platform.Packages)
            {
                var assemblies = await NuGetHelpers.GetNupkgAssembliesAsync(package);
                foreach (var lib in assemblies)
                {
                    TestHelpers.SaveAs(lib.Item2(), tempDirectory, lib.Item1);
                    fileList.Add(Path.GetFileName(lib.Item1));
                }
            }
            
            RepackPlatform(platform, fileList);
            
            var errors = await PeverifyHelper.PeverifyAsync(tempDirectory, "test.dll");
            foreach (var error in errors)
            {
                Console.WriteLine(error);
            }
            
            var errorCodes = errors.ToErrorCodes();
            Assert.IsFalse(errorCodes.Contains(PeverifyHelper.VER_E_TOKEN_RESOLVE));
            Assert.IsFalse(errorCodes.Contains(PeverifyHelper.VER_E_TYPELOAD));
        }

        [Test]
        [Platform(Include = "win")]
        public async Task VerifiesMergesFineWhenOutPathIsOneOfInputs()
        {
            var platform = Platform.From(
                Package.From("Microsoft.Bcl", "1.1.10")
                    .WithArtifact(@"lib\net40\System.Runtime.dll"),
                Package.From("Microsoft.Bcl", "1.1.10")
                    .WithArtifact(@"lib\net40\System.Threading.Tasks.dll"),
                Package.From("Microsoft.Bcl.Async", "1.0.168")
                    .WithArtifact(@"lib\net40\Microsoft.Threading.Tasks.dll"))
                .WithExtraArgs(@"/targetplatform:v4,C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0");

            var fileList = new List<string>();
            foreach (var package in platform.Packages)
            {
                var assemblies = await NuGetHelpers.GetNupkgAssembliesAsync(package);
                foreach (var lib in assemblies)
                {
                    TestHelpers.SaveAs(lib.Item2(), tempDirectory, lib.Item1);
                    fileList.Add(Path.GetFileName(lib.Item1));
                }
            }
            
            RepackPlatformIntoPrimary(platform, fileList);
        }

        [Test]
        [Platform(Include = "win")]
        public async Task VerifiesMergedSignedAssemblyHasNoUnsignedFriend()
        {
            var platform = Platform.From(
                Package.From("reactiveui-core", "6.5.0")
                    .WithArtifact(@"lib\net45\ReactiveUI.dll"),
                Package.From("Splat", "1.6.2")
                    .WithArtifact(@"lib\net45\Splat.dll"))
                .WithExtraArgs("/keyfile:../../../ILRepack/ILRepack.snk");
                
            var fileList = new List<string>();
            foreach (var package in platform.Packages)
            {
                var assemblies = await NuGetHelpers.GetNupkgAssembliesAsync(package);
                foreach (var lib in assemblies)
                {
                    TestHelpers.SaveAs(lib.Item2(), tempDirectory, lib.Item1);
                    fileList.Add(Path.GetFileName(lib.Item1));
                }
            }
            
            RepackPlatform(platform, fileList);
            
            var errors = await PeverifyHelper.PeverifyAsync(tempDirectory, "test.dll");
            foreach (var error in errors)
            {
                Console.WriteLine(error);
            }
            
            var errorCodes = errors.ToErrorCodes();
            Assert.IsFalse(errorCodes.Contains(PeverifyHelper.META_E_CA_FRIENDS_SN_REQUIRED));
        }


        [Test]
        [Platform(Include = "win")]
        public async Task VerifiesMergedPdbUnchangedSourceIndexationForTfsIndexation()
        {
            const string LibName = "TfsEngine.dll";
            const string PdbName = "TfsEngine.pdb";

            var platform = Platform.From(Package.From("TfsIndexer", "1.2.4"));
            
            var packages = platform.Packages.ToList();
            var allContent = await NuGetHelpers.GetNupkgContentAsync(packages[0]);
            var relevantFiles = allContent
                .Where(lib => new[] { LibName, PdbName }.Any(lib.Item1.EndsWith))
                .ToList();
                
            foreach (var lib in relevantFiles)
            {
                TestHelpers.SaveAs(lib.Item2(), tempDirectory, lib.Item1);
            }
            
            var dllFiles = relevantFiles
                .Select(lib => Path.GetFileName(lib.Item1))
                .Where(path => path.EndsWith("dll"))
                .ToList();
                
            foreach (var path in dllFiles)
            {
                RepackPlatform(platform, new List<string> { path });
            }

            var expected = GetSrcSrv(Tmp("TfsEngine.pdb"));
            var actual = GetSrcSrv(Tmp("test.pdb"));
            CollectionAssert.AreEqual(expected, actual);
        }

        private static IEnumerable<string> GetSrcSrv(string pdb)
        {
            return new PdbStr().Read(pdb).GetLines();
        }

        [Test]
        [Platform(Include = "win")]
        public async Task VerifiesMergedPdbKeepSourceIndexationForHttpIndexation()
        {
            var platform = Platform.From(
                Package.From("SourceLink.Core", "1.1.0"),
                Package.From("sourcelink.symbolstore", "1.1.0"));
                
            var allFiles = new List<string>();
            foreach (var package in platform.Packages)
            {
                var content = await NuGetHelpers.GetNupkgContentAsync(package);
                foreach (var lib in content)
                {
                    TestHelpers.SaveAs(lib.Item2(), tempDirectory, lib.Item1);
                    var fileName = Path.GetFileName(lib.Item1);
                    if (fileName.EndsWith("dll"))
                    {
                        allFiles.Add(fileName);
                    }
                }
            }
            
            RepackPlatform(platform, allFiles);

            AssertSourceLinksAreEquivalent(
                new[] { "SourceLink.Core.pdb", "SourceLink.SymbolStore.pdb", "SourceLink.SymbolStore.CorSym.pdb" }.Select(Tmp),
                Tmp("test.pdb"));
        }

        private static void AssertSourceLinksAreEquivalent(IEnumerable<string> expectedPdbNames, string actualPdbName)
        {
            CollectionAssert.AreEquivalent(expectedPdbNames.SelectMany(GetSourceLinks), GetSourceLinks(actualPdbName));
        }

        private static IEnumerable<string> GetSourceLinks(string pdbName)
        {
            var processInfo = new ProcessStartInfo
                              {
                                  CreateNoWindow = true,
                                  UseShellExecute = false,
                                  RedirectStandardOutput = true,
                                  FileName = Path.Combine(
                                          TestContext.CurrentContext.TestDirectory,
                                          @"..\..\..\packages\SourceLink.1.1.0\tools\SourceLink.exe"),
                                  Arguments = "srctoolx --pdb " + pdbName
                              };
            using (var sourceLinkProcess = Process.Start(processInfo))
            using (StreamReader reader = sourceLinkProcess.StandardOutput)
            {
                return reader.ReadToEnd()
                        .GetLines()
                        .Take(reader.ReadToEnd().GetLines().ToArray().Length - 1)
                        .Skip(1);
            }
        }

        void RepackPlatformIntoPrimary(Platform platform, IList<string> list)
        {
            list = list.OrderBy(f => f).ToList();
            Console.WriteLine("Merging {0} into {1}", string.Join(",",list), list.First());
            TestHelpers.DoRepackForCmd(new []{"/out:"+Tmp(list.First()), "/lib:"+tempDirectory}.Concat(platform.Args).Concat(list.Select(Tmp).OrderBy(x => x)));
        }

        void RepackPlatform(Platform platform, IList<string> list)
        {
            Assert.IsTrue(list.Count >= platform.Packages.Count(), 
                "There should be at least the same number of .dlls as the number of packages");
            Console.WriteLine("Merging {0}", string.Join(",",list));
            TestHelpers.DoRepackForCmd(new []{"/out:"+Tmp("test.dll"), "/lib:"+tempDirectory}.Concat(platform.Args).Concat(list.Select(Tmp).OrderBy(x => x)));
            Assert.IsTrue(File.Exists(Tmp("test.dll")));
        }

        string Tmp(string file)
        {
            return Path.Combine(tempDirectory, file);
        }

        async Task VerifyTestAsync(IEnumerable<string> mergedLibraries)
        {
            // ilverify is cross-platform, so we can run verification on all platforms
            var errors = await PeverifyHelper.PeverifyAsync(tempDirectory, "test.dll");
            foreach (var error in errors)
            {
                Console.WriteLine(error);
            }
            
            if (errors.Any())
            {
                var origErrorsList = new List<string>();
                foreach (var lib in mergedLibraries)
                {
                    var libErrors = await PeverifyHelper.PeverifyAsync(tempDirectory, lib);
                    origErrorsList.AddRange(libErrors);
                }
                
                if (errors.Count != origErrorsList.Count)
                    Assert.Fail($"{errors.Count} errors in ilverify, check logs for details");
            }
        }

        void RepackFoo(string assemblyName)
        {
            Console.WriteLine("Merging {0}", assemblyName);
            TestHelpers.DoRepackForCmd("/out:"+Tmp("test.dll"), Tmp("foo.dll"));
            Assert.IsTrue(File.Exists(Tmp("test.dll")));
        }
    }
}
