//
// Copyright (c) 2026 Unity Technologies
//
//   Licensed under the Apache License, Version 2.0 (the "License");
//   you may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//       http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ILRepacking;
using Mono.Cecil;
using NUnit.Framework;

namespace ILRepack.IntegrationTests
{
    [TestFixture]
    public class MultiRepackIntegrationTests
    {
        private string _tempDir;
        private string _outputDir;

        [SetUp]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            _outputDir = Path.Combine(_tempDir, "output");
            Directory.CreateDirectory(_tempDir);
            Directory.CreateDirectory(_outputDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
            {
                try
                {
                    Directory.Delete(_tempDir, true);
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }

        [Test]
        public void EndToEnd_TwoGroups_WithDependency_Success()
        {
            // Arrange - Create test assemblies
            var libAPath = CreateLibraryAssembly("LibraryA", _tempDir, new[] { "ClassA1", "ClassA2" });
            var libBPath = CreateLibraryAssembly("LibraryB", _tempDir, new[] { "ClassB1" }, new[] { "LibraryA" });

            var configPath = Path.Combine(_tempDir, "config.json");
            var config = new MultiRepackConfiguration
            {
                Groups = new List<AssemblyGroup>
                {
                    new AssemblyGroup
                    {
                        Name = "GroupA",
                        InputAssemblies = new List<string> { libAPath },
                        OutputAssembly = Path.Combine(_outputDir, "MergedA.dll")
                    },
                    new AssemblyGroup
                    {
                        Name = "GroupB",
                        InputAssemblies = new List<string> { libBPath },
                        OutputAssembly = Path.Combine(_outputDir, "MergedB.dll")
                    }
                },
                GlobalOptions = new GlobalRepackOptions
                {
                    SearchDirectories = new List<string> { _tempDir, _outputDir }
                }
            };
            config.SaveToFile(configPath);

            // Act
            var options = new RepackOptions(new[] { $"/config:{configPath}" });
            var logger = new RepackLogger(options);
            
            var configObj = MultiRepackConfiguration.LoadFromFile(configPath);
            using (var orchestrator = new MultiRepackOrchestrator(configObj, logger))
            {
                orchestrator.Repack();
            }

            // Assert
            Assert.That(File.Exists(Path.Combine(_outputDir, "MergedA.dll")), "MergedA.dll should exist");
            Assert.That(File.Exists(Path.Combine(_outputDir, "MergedB.dll")), "MergedB.dll should exist");

            // Verify that MergedB references MergedA
            using (var mergedB = AssemblyDefinition.ReadAssembly(Path.Combine(_outputDir, "MergedB.dll")))
            {
                var references = mergedB.MainModule.AssemblyReferences;
                
                var hasMergedAReference = references.Any(r => r.Name == "MergedA");
                Assert.That(hasMergedAReference, "MergedB should reference MergedA");
                
                var hasLibraryAReference = references.Any(r => r.Name == "LibraryA");
                Assert.That(hasLibraryAReference, Is.False, "MergedB should not reference LibraryA (should be rewritten to MergedA)");
            }
        }

        [Test]
        public void EndToEnd_ThreeGroups_ChainedDependencies_Success()
        {
            // Arrange - Create: C -> B -> A
            var libAPath = CreateLibraryAssembly("LibA", _tempDir, new[] { "ClassA" });
            var libBPath = CreateLibraryAssembly("LibB", _tempDir, new[] { "ClassB" }, new[] { "LibA" });
            var libCPath = CreateLibraryAssembly("LibC", _tempDir, new[] { "ClassC" }, new[] { "LibB" });

            var configPath = Path.Combine(_tempDir, "config.json");
            var config = new MultiRepackConfiguration
            {
                Groups = new List<AssemblyGroup>
                {
                    new AssemblyGroup
                    {
                        Name = "GroupA",
                        InputAssemblies = new List<string> { libAPath },
                        OutputAssembly = Path.Combine(_outputDir, "OutA.dll")
                    },
                    new AssemblyGroup
                    {
                        Name = "GroupB",
                        InputAssemblies = new List<string> { libBPath },
                        OutputAssembly = Path.Combine(_outputDir, "OutB.dll")
                    },
                    new AssemblyGroup
                    {
                        Name = "GroupC",
                        InputAssemblies = new List<string> { libCPath },
                        OutputAssembly = Path.Combine(_outputDir, "OutC.dll")
                    }
                },
                GlobalOptions = new GlobalRepackOptions
                {
                    SearchDirectories = new List<string> { _tempDir, _outputDir }
                }
            };
            config.SaveToFile(configPath);

            // Act
            var options = new RepackOptions(new[] { $"/config:{configPath}" });
            var logger = new RepackLogger(options);
            
            var configObj = MultiRepackConfiguration.LoadFromFile(configPath);
            using (var orchestrator = new MultiRepackOrchestrator(configObj, logger))
            {
                orchestrator.Repack();
            }

            // Assert
            Assert.That(File.Exists(Path.Combine(_outputDir, "OutA.dll")));
            Assert.That(File.Exists(Path.Combine(_outputDir, "OutB.dll")));
            Assert.That(File.Exists(Path.Combine(_outputDir, "OutC.dll")));

            // Verify reference chain
            using (var outB = AssemblyDefinition.ReadAssembly(Path.Combine(_outputDir, "OutB.dll")))
            {
                var hasOutAReference = outB.MainModule.AssemblyReferences.Any(r => r.Name == "OutA");
                Assert.That(hasOutAReference, "OutB should reference OutA");
            }

            using (var outC = AssemblyDefinition.ReadAssembly(Path.Combine(_outputDir, "OutC.dll")))
            {
                var hasOutBReference = outC.MainModule.AssemblyReferences.Any(r => r.Name == "OutB");
                Assert.That(hasOutBReference, "OutC should reference OutB");
            }
        }

        [Test]
        public void EndToEnd_MultipleAssembliesInGroup_Success()
        {
            // Arrange - Create multiple assemblies for each group
            var libA1Path = CreateLibraryAssembly("LibA1", _tempDir, new[] { "ClassA1" });
            var libA2Path = CreateLibraryAssembly("LibA2", _tempDir, new[] { "ClassA2" });
            var libB1Path = CreateLibraryAssembly("LibB1", _tempDir, new[] { "ClassB1" }, new[] { "LibA1" });
            var libB2Path = CreateLibraryAssembly("LibB2", _tempDir, new[] { "ClassB2" }, new[] { "LibA2" });

            var configPath = Path.Combine(_tempDir, "config.json");
            var config = new MultiRepackConfiguration
            {
                Groups = new List<AssemblyGroup>
                {
                    new AssemblyGroup
                    {
                        Name = "GroupA",
                        InputAssemblies = new List<string> { libA1Path, libA2Path },
                        OutputAssembly = Path.Combine(_outputDir, "CombinedA.dll")
                    },
                    new AssemblyGroup
                    {
                        Name = "GroupB",
                        InputAssemblies = new List<string> { libB1Path, libB2Path },
                        OutputAssembly = Path.Combine(_outputDir, "CombinedB.dll")
                    }
                },
                GlobalOptions = new GlobalRepackOptions
                {
                    SearchDirectories = new List<string> { _tempDir, _outputDir }
                }
            };
            config.SaveToFile(configPath);

            // Act
            var options = new RepackOptions(new[] { $"/config:{configPath}" });
            var logger = new RepackLogger(options);
            
            var configObj = MultiRepackConfiguration.LoadFromFile(configPath);
            using (var orchestrator = new MultiRepackOrchestrator(configObj, logger))
            {
                orchestrator.Repack();
            }

            // Assert
            Assert.That(File.Exists(Path.Combine(_outputDir, "CombinedA.dll")));
            Assert.That(File.Exists(Path.Combine(_outputDir, "CombinedB.dll")));

            // Verify that CombinedA contains types from both LibA1 and LibA2
            using (var combinedA = AssemblyDefinition.ReadAssembly(Path.Combine(_outputDir, "CombinedA.dll")))
            {
                var types = combinedA.MainModule.Types.Select(t => t.Name).ToList();
                Assert.That(types.Contains("ClassA1"), "CombinedA should contain ClassA1");
                Assert.That(types.Contains("ClassA2"), "CombinedA should contain ClassA2");
            }

            // Verify that CombinedB contains types from both LibB1 and LibB2
            using (var combinedB = AssemblyDefinition.ReadAssembly(Path.Combine(_outputDir, "CombinedB.dll")))
            {
                var types = combinedB.MainModule.Types.Select(t => t.Name).ToList();
                Assert.That(types.Contains("ClassB1"), "CombinedB should contain ClassB1");
                Assert.That(types.Contains("ClassB2"), "CombinedB should contain ClassB2");
                
                // Verify references are rewritten
                var hasLibA1Ref = combinedB.MainModule.AssemblyReferences.Any(r => r.Name == "LibA1");
                var hasLibA2Ref = combinedB.MainModule.AssemblyReferences.Any(r => r.Name == "LibA2");
                var hasCombinedARef = combinedB.MainModule.AssemblyReferences.Any(r => r.Name == "CombinedA");
                
                Assert.That(hasLibA1Ref, Is.False, "CombinedB should not reference LibA1");
                Assert.That(hasLibA2Ref, Is.False, "CombinedB should not reference LibA2");
                Assert.That(hasCombinedARef, "CombinedB should reference CombinedA");
            }
        }

        [Test]
        public void EndToEnd_IndependentGroups_Success()
        {
            // Arrange - Create two independent groups with no cross-references
            var libAPath = CreateLibraryAssembly("IndepA", _tempDir, new[] { "ClassA" });
            var libBPath = CreateLibraryAssembly("IndepB", _tempDir, new[] { "ClassB" });

            var configPath = Path.Combine(_tempDir, "config.json");
            var config = new MultiRepackConfiguration
            {
                Groups = new List<AssemblyGroup>
                {
                    new AssemblyGroup
                    {
                        Name = "Independent1",
                        InputAssemblies = new List<string> { libAPath },
                        OutputAssembly = Path.Combine(_outputDir, "Indep1.dll")
                    },
                    new AssemblyGroup
                    {
                        Name = "Independent2",
                        InputAssemblies = new List<string> { libBPath },
                        OutputAssembly = Path.Combine(_outputDir, "Indep2.dll")
                    }
                },
                GlobalOptions = new GlobalRepackOptions
                {
                    SearchDirectories = new List<string> { _tempDir, _outputDir }
                }
            };
            config.SaveToFile(configPath);

            // Act
            var options = new RepackOptions(new[] { $"/config:{configPath}" });
            var logger = new RepackLogger(options);
            
            var configObj = MultiRepackConfiguration.LoadFromFile(configPath);
            using (var orchestrator = new MultiRepackOrchestrator(configObj, logger))
            {
                orchestrator.Repack();
            }

            // Assert
            Assert.That(File.Exists(Path.Combine(_outputDir, "Indep1.dll")));
            Assert.That(File.Exists(Path.Combine(_outputDir, "Indep2.dll")));
        }

        // Helper method to create a library assembly with Cecil
        private string CreateLibraryAssembly(string name, string outputDir, string[] classNames, string[] references = null)
        {
            var assemblyName = new Mono.Cecil.AssemblyNameDefinition(name, new Version(1, 0, 0, 0));
            
            // Create a temporary assembly first
            var tempAssembly = Mono.Cecil.AssemblyDefinition.CreateAssembly(
                assemblyName,
                name,
                Mono.Cecil.ModuleKind.Dll);

            // Add references if specified
            if (references != null)
            {
                foreach (var refName in references)
                {
                    var reference = new Mono.Cecil.AssemblyNameReference(refName, new Version(1, 0, 0, 0));
                    tempAssembly.MainModule.AssemblyReferences.Add(reference);
                }
            }

            // Add classes
            foreach (var className in classNames)
            {
                var type = new Mono.Cecil.TypeDefinition(
                    name,
                    className,
                    Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                    tempAssembly.MainModule.TypeSystem.Object);

                // Add a simple method
                var method = new Mono.Cecil.MethodDefinition(
                    "GetValue",
                    Mono.Cecil.MethodAttributes.Public,
                    tempAssembly.MainModule.TypeSystem.Int32);

                var il = method.Body.GetILProcessor();
                il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4, 42);
                il.Emit(Mono.Cecil.Cil.OpCodes.Ret);

                type.Methods.Add(method);
                tempAssembly.MainModule.Types.Add(type);
            }

            var outputPath = Path.Combine(outputDir, $"{name}.dll");
            
            // Write and dispose the temp assembly
            tempAssembly.Write(outputPath);
            tempAssembly.Dispose();
            
            // Now reload it and fix the corlib reference to use .NET Core
            var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(outputPath);
            
            // Replace mscorlib with System.Private.CoreLib
            var mscorlibRef = assembly.MainModule.AssemblyReferences.FirstOrDefault(r => r.Name == "mscorlib");
            if (mscorlibRef != null)
            {
                // Remove mscorlib
                assembly.MainModule.AssemblyReferences.Remove(mscorlibRef);
                
                // Add System.Private.CoreLib (the .NET Core corlib)
                var coreLibRef = new Mono.Cecil.AssemblyNameReference("System.Private.CoreLib", new Version(8, 0, 0, 0))
                {
                    PublicKeyToken = new byte[] { 0x7c, 0xec, 0x85, 0xd7, 0xbe, 0xa7, 0x79, 0x8e }
                };
                assembly.MainModule.AssemblyReferences.Insert(0, coreLibRef);
                
                // Update all type references that point to mscorlib
                foreach (var typeRef in assembly.MainModule.GetTypeReferences())
                {
                    if (typeRef.Scope == mscorlibRef)
                    {
                        typeRef.Scope = coreLibRef;
                    }
                }
            }
            
            // Save the modified assembly
            assembly.Write(outputPath);
            assembly.Dispose();

            return outputPath;
        }
    }
}

