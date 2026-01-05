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
using System.Reflection;
using System.Reflection.Emit;
using ILRepacking;
using NUnit.Framework;

namespace ILRepack.Tests
{
    [TestFixture]
    public class MultiRepackOrchestratorTests
    {
        private string _tempDir;
        private TestLogger _logger;

        [SetUp]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);
            _logger = new TestLogger();
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
        public void CircularDependency_ThrowsException()
        {
            // Arrange
            var a1Path = CreateAssembly("A1", _tempDir);
            var b1Path = CreateAssembly("B1", _tempDir);
            
            // Create A2 that references B1
            var a2Path = CreateAssemblyWithReference("A2", _tempDir, "B1");
            
            // Create B2 that references A1
            var b2Path = CreateAssemblyWithReference("B2", _tempDir, "A1");

            var config = new MultiRepackConfiguration
            {
                Groups = new List<AssemblyGroup>
                {
                    new AssemblyGroup
                    {
                        Name = "GroupA",
                        InputAssemblies = new List<string> { a1Path, a2Path },
                        OutputAssembly = Path.Combine(_tempDir, "A.dll")
                    },
                    new AssemblyGroup
                    {
                        Name = "GroupB",
                        InputAssemblies = new List<string> { b1Path, b2Path },
                        OutputAssembly = Path.Combine(_tempDir, "B.dll")
                    }
                }
            };

            // Act & Assert
            using (var orchestrator = new MultiRepackOrchestrator(config, _logger))
            {
                var ex = Assert.Throws<InvalidOperationException>(() => orchestrator.Repack());
                Assert.That(ex.Message, Does.Contain("Circular dependency"));
            }
        }

        [Test]
        public void LinearDependencies_CorrectOrder()
        {
            // Arrange - Create assemblies where B1 depends on A1
            var a1Path = CreateAssembly("A1", _tempDir);
            var a2Path = CreateAssembly("A2", _tempDir);
            var b1Path = CreateAssemblyWithReference("B1", _tempDir, "A1");
            var b2Path = CreateAssembly("B2", _tempDir);

            var groupA = new AssemblyGroup
            {
                Name = "GroupA",
                InputAssemblies = new List<string> { a1Path, a2Path },
                OutputAssembly = Path.Combine(_tempDir, "A.dll")
            };
            
            var groupB = new AssemblyGroup
            {
                Name = "GroupB",
                InputAssemblies = new List<string> { b1Path, b2Path },
                OutputAssembly = Path.Combine(_tempDir, "B.dll")
            };

            var config = new MultiRepackConfiguration
            {
                Groups = new List<AssemblyGroup> { groupB, groupA }, // Intentionally wrong order
                GlobalOptions = new GlobalRepackOptions
                {
                    SearchDirectories = new List<string> { _tempDir }
                }
            };

            // Act - Determine processing order
            using (var orchestrator = new MultiRepackOrchestrator(config, _logger))
            {
                var processingOrder = orchestrator.DetermineProcessingOrder();

                // Assert - GroupA should be processed before GroupB (despite being listed second)
                Assert.AreEqual(2, processingOrder.Count);
                Assert.AreEqual("GroupA", processingOrder[0].Name, "GroupA should be first (no dependencies)");
                Assert.AreEqual("GroupB", processingOrder[1].Name, "GroupB should be second (depends on GroupA)");
            }
        }

        [Test]
        public void DuplicateAssemblyInGroups_ThrowsException()
        {
            // Arrange
            var a1Path = CreateAssembly("A1", _tempDir);

            var config = new MultiRepackConfiguration
            {
                Groups = new List<AssemblyGroup>
                {
                    new AssemblyGroup
                    {
                        Name = "GroupA",
                        InputAssemblies = new List<string> { a1Path },
                        OutputAssembly = Path.Combine(_tempDir, "A.dll")
                    },
                    new AssemblyGroup
                    {
                        Name = "GroupB",
                        InputAssemblies = new List<string> { a1Path },
                        OutputAssembly = Path.Combine(_tempDir, "B.dll")
                    }
                }
            };

            // Act & Assert
            using (var orchestrator = new MultiRepackOrchestrator(config, _logger))
            {
                var ex = Assert.Throws<InvalidOperationException>(() => orchestrator.Repack());
                Assert.That(ex.Message, Does.Contain("appears in multiple groups"));
            }
        }

        [Test]
        public void MultipleIndependentGroups_AnyOrderValid()
        {
            // Arrange - Create two independent groups with no cross-references
            var a1Path = CreateAssembly("A1", _tempDir);
            var a2Path = CreateAssembly("A2", _tempDir);
            var b1Path = CreateAssembly("B1", _tempDir);
            var b2Path = CreateAssembly("B2", _tempDir);

            var groupA = new AssemblyGroup
            {
                Name = "GroupA",
                InputAssemblies = new List<string> { a1Path, a2Path },
                OutputAssembly = Path.Combine(_tempDir, "A.dll")
            };
            
            var groupB = new AssemblyGroup
            {
                Name = "GroupB",
                InputAssemblies = new List<string> { b1Path, b2Path },
                OutputAssembly = Path.Combine(_tempDir, "B.dll")
            };

            var config = new MultiRepackConfiguration
            {
                Groups = new List<AssemblyGroup> { groupA, groupB },
                GlobalOptions = new GlobalRepackOptions
                {
                    SearchDirectories = new List<string> { _tempDir }
                }
            };

            // Act - Determine processing order for independent groups
            using (var orchestrator = new MultiRepackOrchestrator(config, _logger))
            {
                var processingOrder = orchestrator.DetermineProcessingOrder();

                // Assert - Both groups should be in the result (order doesn't matter for independent groups)
                Assert.AreEqual(2, processingOrder.Count);
                Assert.IsTrue(processingOrder.Contains(groupA), "GroupA should be in processing order");
                Assert.IsTrue(processingOrder.Contains(groupB), "GroupB should be in processing order");
            }
        }

        [Test]
        public void ThreeLevelDependencies_CorrectOrder()
        {
            // Arrange - Create: C -> B -> A (C depends on B, B depends on A)
            var a1Path = CreateAssembly("A1", _tempDir);
            var b1Path = CreateAssemblyWithReference("B1", _tempDir, "A1");
            var c1Path = CreateAssemblyWithReference("C1", _tempDir, "B1");

            var groupA = new AssemblyGroup
            {
                Name = "GroupA",
                InputAssemblies = new List<string> { a1Path },
                OutputAssembly = Path.Combine(_tempDir, "A.dll")
            };
            
            var groupB = new AssemblyGroup
            {
                Name = "GroupB",
                InputAssemblies = new List<string> { b1Path },
                OutputAssembly = Path.Combine(_tempDir, "B.dll")
            };
            
            var groupC = new AssemblyGroup
            {
                Name = "GroupC",
                InputAssemblies = new List<string> { c1Path },
                OutputAssembly = Path.Combine(_tempDir, "C.dll")
            };

            var config = new MultiRepackConfiguration
            {
                Groups = new List<AssemblyGroup> { groupC, groupB, groupA }, // Reverse order
                GlobalOptions = new GlobalRepackOptions
                {
                    SearchDirectories = new List<string> { _tempDir }
                }
            };

            // Act - Determine processing order
            using (var orchestrator = new MultiRepackOrchestrator(config, _logger))
            {
                var processingOrder = orchestrator.DetermineProcessingOrder();

                // Assert - Should be sorted: A (no deps), B (depends on A), C (depends on B)
                Assert.AreEqual(3, processingOrder.Count);
                Assert.AreEqual("GroupA", processingOrder[0].Name, "GroupA should be first (no dependencies)");
                Assert.AreEqual("GroupB", processingOrder[1].Name, "GroupB should be second (depends on GroupA)");
                Assert.AreEqual("GroupC", processingOrder[2].Name, "GroupC should be third (depends on GroupB)");
            }
        }

        // Helper methods to create test assemblies
        private string CreateAssembly(string name, string outputDir)
        {
            var outputPath = Path.Combine(outputDir, $"{name}.dll");
            
            // Create assembly using Cecil
            var assemblyName = new Mono.Cecil.AssemblyNameDefinition(name, new Version(1, 0, 0, 0));
            var assembly = Mono.Cecil.AssemblyDefinition.CreateAssembly(
                assemblyName,
                name,
                Mono.Cecil.ModuleKind.Dll);
            
            var testType = new Mono.Cecil.TypeDefinition(
                name,
                "TestClass",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                assembly.MainModule.TypeSystem.Object);
            
            assembly.MainModule.Types.Add(testType);
            assembly.Write(outputPath);
            assembly.Dispose();

            return outputPath;
        }

        private string CreateAssemblyWithReference(string name, string outputDir, string referenceName)
        {
            var generator = new Mono.Cecil.AssemblyNameDefinition(name, new Version(1, 0, 0, 0));
            var assembly = Mono.Cecil.AssemblyDefinition.CreateAssembly(
                generator,
                name,
                Mono.Cecil.ModuleKind.Dll);
            
            // Add reference
            var reference = new Mono.Cecil.AssemblyNameReference(referenceName, new Version(1, 0, 0, 0));
            assembly.MainModule.AssemblyReferences.Add(reference);
            
            var testType = new Mono.Cecil.TypeDefinition(
                name,
                "TestClass",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                assembly.MainModule.TypeSystem.Object);
            
            assembly.MainModule.Types.Add(testType);
            
            var outputPath = Path.Combine(outputDir, $"{name}.dll");
            assembly.Write(outputPath);
            assembly.Dispose();

            return outputPath;
        }

        // Test logger implementation
        private class TestLogger : ILogger
        {
            public List<string> InfoMessages { get; } = new List<string>();
            public List<string> WarnMessages { get; } = new List<string>();
            public List<string> ErrorMessages { get; } = new List<string>();

            public void Log(object str)
            {
                InfoMessages.Add(str?.ToString());
            }

            public void Error(string msg)
            {
                ErrorMessages.Add(msg);
            }

            public void Warn(string msg)
            {
                WarnMessages.Add(msg);
            }

            public void Info(string msg)
            {
                InfoMessages.Add(msg);
            }

            public void Verbose(string msg)
            {
                InfoMessages.Add(msg);
            }

            public void DuplicateIgnored(string ignoredType, object ignoredObject)
            {
                InfoMessages.Add($"Duplicate ignored: {ignoredType} - {ignoredObject}");
            }

            public bool ShouldLogVerbose { get; set; }

            public void Dispose()
            {
            }
        }
    }
}

