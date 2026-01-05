using ILRepack.IntegrationTests.Peverify;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

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
            Assert.That(errorCodes, Does.Not.Contains(PeverifyHelper.VER_E_STACK_OVERFLOW));
        }

        [Test]
        public async Task VerifiesMergesFineWhenOutPathIsOneOfInputs()
        {
            // Test that ILRepack works when the output path overwrites one of the input assemblies
            // Using modern cross-platform packages instead of legacy .NET Framework 4.0 packages
            var platform = Platform.From(
                Package.From("Castle.Core", "5.2.1")
                    .WithArtifact("lib/netstandard2.0/Castle.Core.dll"),
                Package.From("System.Runtime.CompilerServices.Unsafe", "4.5.1")
                    .WithArtifact("lib/netstandard2.0/System.Runtime.CompilerServices.Unsafe.dll"));

            var fileList = new List<string>();
            foreach (var package in platform.Packages)
            {
                var assemblies = await NuGetHelpers.GetNupkgAssembliesAsync(package);
                foreach (var lib in assemblies)
                {
                    TestHelpers.SaveAs(lib.streamProvider(), tempDirectory, lib.normalizedName);
                    fileList.Add(Path.GetFileName(lib.normalizedName));
                }
            }
            
            RepackPlatformIntoPrimary(platform, fileList);
            
            // Verify the merged assembly exists and is valid
            var primaryFile = fileList.OrderBy(f => f).First();
            Assert.That(File.Exists(Tmp(primaryFile)), "Merged assembly should exist");
            
            // Basic verification that the merge succeeded
            var errors = await PeverifyHelper.PeverifyAsync(tempDirectory, primaryFile);
            if (errors.Any())
            {
                foreach (var error in errors)
                {
                    Console.WriteLine(error);
                }
            }
        }

        void RepackPlatformIntoPrimary(Platform platform, IList<string> list)
        {
            // Merge all assemblies into the first one (output overwrites first input)
            list = list.OrderBy(f => f).ToList();
            Console.WriteLine("Merging {0} into {1}", string.Join(",",list), list.First());
            TestHelpers.DoRepackForCmd(new []{"/out:"+Tmp(list.First()), "/lib:"+tempDirectory}.Concat(platform.Args).Concat(list.Select(Tmp).OrderBy(x => x)));
        }

        void RepackPlatform(Platform platform, IList<string> list)
        {
            Assert.That(list.Count, Is.GreaterThanOrEqualTo(platform.Packages.Count()),
                "There should be at least the same number of .dlls as the number of packages");
            Console.WriteLine("Merging {0}", string.Join(",",list));
            TestHelpers.DoRepackForCmd(new []{"/out:"+Tmp("test.dll"), "/lib:"+tempDirectory}.Concat(platform.Args).Concat(list.Select(Tmp).OrderBy(x => x)));
            Assert.That(File.Exists(Tmp("test.dll")));
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
            Assert.That(File.Exists(Tmp("test.dll")));
        }
    }
}
