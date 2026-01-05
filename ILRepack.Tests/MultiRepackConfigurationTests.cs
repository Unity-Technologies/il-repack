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
using ILRepacking;
using NUnit.Framework;

namespace ILRepack.Tests
{
    [TestFixture]
    public class MultiRepackConfigurationTests
    {
        private string _tempDir;

        [SetUp]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);
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
        public void LoadFromFile_ValidConfiguration_Success()
        {
            // Arrange
            var configPath = Path.Combine(_tempDir, "config.json");
            var json = @"{
  ""groups"": [
    {
      ""name"": ""GroupA"",
      ""inputAssemblies"": [""A1.dll"", ""A2.dll""],
      ""outputAssembly"": ""A.dll""
    },
    {
      ""name"": ""GroupB"",
      ""inputAssemblies"": [""B1.dll"", ""B2.dll""],
      ""outputAssembly"": ""B.dll""
    }
  ],
  ""globalOptions"": {
    ""internalize"": true,
    ""debugInfo"": false
  }
}";
            File.WriteAllText(configPath, json);

            // Act
            var config = MultiRepackConfiguration.LoadFromFile(configPath);

            // Assert
            Assert.IsNotNull(config);
            Assert.AreEqual(2, config.Groups.Count);
            Assert.AreEqual("GroupA", config.Groups[0].Name);
            Assert.AreEqual(2, config.Groups[0].InputAssemblies.Count);
            Assert.AreEqual("A.dll", config.Groups[0].OutputAssembly);
            Assert.IsTrue(config.GlobalOptions.Internalize.Value);
            Assert.IsFalse(config.GlobalOptions.DebugInfo.Value);
        }

        [Test]
        public void LoadFromFile_FileNotFound_ThrowsException()
        {
            // Arrange
            var configPath = Path.Combine(_tempDir, "nonexistent.json");

            // Act & Assert
            Assert.Throws<FileNotFoundException>(() =>
                MultiRepackConfiguration.LoadFromFile(configPath));
        }

        [Test]
        public void LoadFromFile_InvalidJson_ThrowsException()
        {
            // Arrange
            var configPath = Path.Combine(_tempDir, "invalid.json");
            File.WriteAllText(configPath, "{ invalid json }");

            // Act & Assert
            Assert.Throws<System.Text.Json.JsonException>(() =>
                MultiRepackConfiguration.LoadFromFile(configPath));
        }

        [Test]
        public void Validate_EmptyGroups_ThrowsException()
        {
            // Arrange
            var config = new MultiRepackConfiguration
            {
                Groups = new List<AssemblyGroup>()
            };

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
            Assert.That(ex.Message, Does.Contain("at least one assembly group"));
        }

        [Test]
        public void Validate_DuplicateOutputAssembly_ThrowsException()
        {
            // Arrange
            var config = new MultiRepackConfiguration
            {
                Groups = new List<AssemblyGroup>
                {
                    new AssemblyGroup
                    {
                        InputAssemblies = new List<string> { "A1.dll" },
                        OutputAssembly = "Output.dll"
                    },
                    new AssemblyGroup
                    {
                        InputAssemblies = new List<string> { "B1.dll" },
                        OutputAssembly = "Output.dll"
                    }
                }
            };

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
            Assert.That(ex.Message, Does.Contain("Duplicate output assembly"));
        }

        [Test]
        public void Validate_GroupWithoutInputAssemblies_ThrowsException()
        {
            // Arrange
            var config = new MultiRepackConfiguration
            {
                Groups = new List<AssemblyGroup>
                {
                    new AssemblyGroup
                    {
                        OutputAssembly = "Output.dll"
                    }
                }
            };

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
            Assert.That(ex.Message, Does.Contain("at least one input assembly"));
        }

        [Test]
        public void Validate_GroupWithoutOutputAssembly_ThrowsException()
        {
            // Arrange
            var config = new MultiRepackConfiguration
            {
                Groups = new List<AssemblyGroup>
                {
                    new AssemblyGroup
                    {
                        InputAssemblies = new List<string> { "A1.dll" }
                    }
                }
            };

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
            Assert.That(ex.Message, Does.Contain("must specify an output assembly"));
        }

        [Test]
        public void SaveToFile_ValidConfiguration_Success()
        {
            // Arrange
            var configPath = Path.Combine(_tempDir, "saved_config.json");
            var config = new MultiRepackConfiguration
            {
                Groups = new List<AssemblyGroup>
                {
                    new AssemblyGroup
                    {
                        Name = "TestGroup",
                        InputAssemblies = new List<string> { "A1.dll", "A2.dll" },
                        OutputAssembly = "A.dll"
                    }
                },
                GlobalOptions = new GlobalRepackOptions
                {
                    Internalize = true,
                    DebugInfo = false
                }
            };

            // Act
            config.SaveToFile(configPath);

            // Assert
            Assert.IsTrue(File.Exists(configPath));
            var loadedConfig = MultiRepackConfiguration.LoadFromFile(configPath);
            Assert.AreEqual(1, loadedConfig.Groups.Count);
            Assert.AreEqual("TestGroup", loadedConfig.Groups[0].Name);
        }

        [Test]
        public void RepackOptions_MultiRepackMode_IsMultiRepackReturnsTrue()
        {
            // Arrange
            var options = new RepackOptions(new[] { "/config:test.json" });

            // Act & Assert
            Assert.IsTrue(options.IsMultiRepack);
            Assert.AreEqual("test.json", options.MultiRepackConfigFile);
        }

        [Test]
        public void RepackOptions_MultiRepackMode_Validate_WithOutputFile_ThrowsException()
        {
            // Arrange
            var options = new RepackOptions(new[] { "/config:test.json", "/out:output.dll" });

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => options.Validate());
            Assert.That(ex.Message, Does.Contain("cannot be used with 'config'"));
        }

        [Test]
        public void RepackOptions_MultiRepackMode_Validate_WithInputAssemblies_ThrowsException()
        {
            // Arrange
            var options = new RepackOptions(new[] { "/config:test.json", "input.dll" });

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => options.Validate());
            Assert.That(ex.Message, Does.Contain("cannot be specified directly with 'config'"));
        }

        [Test]
        public void LoadFromFile_WithComments_Success()
        {
            // Arrange
            var configPath = Path.Combine(_tempDir, "config_with_comments.json");
            var json = @"{
  // This is a comment
  ""groups"": [
    {
      ""name"": ""GroupA"",
      ""inputAssemblies"": [""A1.dll""], // Another comment
      ""outputAssembly"": ""A.dll""
    }
  ]
}";
            File.WriteAllText(configPath, json);

            // Act
            var config = MultiRepackConfiguration.LoadFromFile(configPath);

            // Assert
            Assert.IsNotNull(config);
            Assert.AreEqual(1, config.Groups.Count);
        }

        [Test]
        public void LoadFromFile_WithTrailingCommas_Success()
        {
            // Arrange
            var configPath = Path.Combine(_tempDir, "config_with_trailing_commas.json");
            var json = @"{
  ""groups"": [
    {
      ""name"": ""GroupA"",
      ""inputAssemblies"": [""A1.dll"", ""A2.dll"",],
      ""outputAssembly"": ""A.dll"",
    },
  ],
}";
            File.WriteAllText(configPath, json);

            // Act
            var config = MultiRepackConfiguration.LoadFromFile(configPath);

            // Assert
            Assert.IsNotNull(config);
            Assert.AreEqual(1, config.Groups.Count);
        }

        [Test]
        public void LoadFromFile_GroupSpecificOptions_OverrideGlobal()
        {
            // Arrange
            var configPath = Path.Combine(_tempDir, "config_with_overrides.json");
            var json = @"{
  ""groups"": [
    {
      ""name"": ""GroupA"",
      ""inputAssemblies"": [""A1.dll""],
      ""outputAssembly"": ""A.dll"",
      ""options"": {
        ""internalize"": false,
        ""version"": ""1.0.0.0""
      }
    }
  ],
  ""globalOptions"": {
    ""internalize"": true
  }
}";
            File.WriteAllText(configPath, json);

            // Act
            var config = MultiRepackConfiguration.LoadFromFile(configPath);

            // Assert
            Assert.IsNotNull(config);
            Assert.IsTrue(config.GlobalOptions.Internalize.Value);
            Assert.IsFalse(config.Groups[0].Options.Internalize.Value);
            Assert.AreEqual("1.0.0.0", config.Groups[0].Options.Version);
        }

        [Test]
        public void LoadFromFile_CaseInsensitive_Success()
        {
            // Arrange
            var configPath = Path.Combine(_tempDir, "config_case_insensitive.json");
            var json = @"{
  ""Groups"": [
    {
      ""Name"": ""GroupA"",
      ""InputAssemblies"": [""A1.dll""],
      ""OutputAssembly"": ""A.dll""
    }
  ],
  ""GlobalOptions"": {
    ""Internalize"": true
  }
}";
            File.WriteAllText(configPath, json);

            // Act
            var config = MultiRepackConfiguration.LoadFromFile(configPath);

            // Assert
            Assert.IsNotNull(config);
            Assert.AreEqual(1, config.Groups.Count);
            Assert.AreEqual("GroupA", config.Groups[0].Name);
        }
    }
}

