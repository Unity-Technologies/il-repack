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
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ILRepacking
{
    /// <summary>
    /// Represents the configuration for multi-assembly repacking
    /// </summary>
    public class MultiRepackConfiguration
    {
        /// <summary>
        /// The assembly groups to merge
        /// </summary>
        [JsonPropertyName("groups")]
        public List<AssemblyGroup> Groups { get; set; } = new List<AssemblyGroup>();

        /// <summary>
        /// Global options to apply to all repacking operations
        /// </summary>
        [JsonPropertyName("globalOptions")]
        public GlobalRepackOptions GlobalOptions { get; set; } = new GlobalRepackOptions();

        /// <summary>
        /// Loads a multi-repack configuration from a JSON file
        /// </summary>
        public static MultiRepackConfiguration LoadFromFile(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Multi-repack configuration file not found: {path}");

            var json = File.ReadAllText(path);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            var config = JsonSerializer.Deserialize<MultiRepackConfiguration>(json, options);
            if (config == null)
                throw new InvalidOperationException($"Failed to deserialize configuration from {path}");

            config.Validate();
            return config;
        }

        /// <summary>
        /// Validates the configuration
        /// </summary>
        public void Validate()
        {
            if (Groups == null || Groups.Count == 0)
                throw new InvalidOperationException("Multi-repack configuration must contain at least one assembly group");

            var outputNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in Groups)
            {
                group.Validate();

                if (outputNames.Contains(group.OutputAssembly))
                    throw new InvalidOperationException($"Duplicate output assembly name: {group.OutputAssembly}");
                outputNames.Add(group.OutputAssembly);
            }
        }

        /// <summary>
        /// Saves the configuration to a JSON file
        /// </summary>
        public void SaveToFile(string path)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(path, json);
        }
    }

    /// <summary>
    /// Represents a group of assemblies to be merged together
    /// </summary>
    public class AssemblyGroup
    {
        /// <summary>
        /// Optional name for this group (for logging purposes)
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// List of input assemblies to merge
        /// </summary>
        [JsonPropertyName("inputAssemblies")]
        public List<string> InputAssemblies { get; set; } = new List<string>();

        /// <summary>
        /// The output assembly path
        /// </summary>
        [JsonPropertyName("outputAssembly")]
        public string OutputAssembly { get; set; }

        /// <summary>
        /// Optional group-specific options that override global options
        /// </summary>
        [JsonPropertyName("options")]
        public GroupRepackOptions Options { get; set; }

        public void Validate()
        {
            if (InputAssemblies == null || InputAssemblies.Count == 0)
                throw new InvalidOperationException($"Assembly group '{Name ?? OutputAssembly}' must contain at least one input assembly");

            if (string.IsNullOrWhiteSpace(OutputAssembly))
                throw new InvalidOperationException($"Assembly group '{Name}' must specify an output assembly");
        }
    }

    /// <summary>
    /// Global options that apply to all repack operations
    /// </summary>
    public class GlobalRepackOptions
    {
        [JsonPropertyName("searchDirectories")]
        public List<string> SearchDirectories { get; set; } = new List<string>();

        [JsonPropertyName("internalize")]
        public bool? Internalize { get; set; }

        [JsonPropertyName("debugInfo")]
        public bool? DebugInfo { get; set; }

        [JsonPropertyName("copyAttributes")]
        public bool? CopyAttributes { get; set; }

        [JsonPropertyName("allowMultipleAssemblyLevelAttributes")]
        public bool? AllowMultipleAssemblyLevelAttributes { get; set; }

        [JsonPropertyName("xmlDocumentation")]
        public bool? XmlDocumentation { get; set; }

        [JsonPropertyName("union")]
        public bool? Union { get; set; }

        [JsonPropertyName("targetKind")]
        public string TargetKind { get; set; }

        [JsonPropertyName("targetPlatformVersion")]
        public string TargetPlatformVersion { get; set; }

        [JsonPropertyName("parallel")]
        public bool? Parallel { get; set; }

        [JsonPropertyName("allowWildCards")]
        public bool? AllowWildCards { get; set; }

        [JsonPropertyName("allowZeroPeKind")]
        public bool? AllowZeroPeKind { get; set; }

        [JsonPropertyName("allowDuplicateResources")]
        public bool? AllowDuplicateResources { get; set; }

        [JsonPropertyName("log")]
        public bool? Log { get; set; }

        [JsonPropertyName("logVerbose")]
        public bool? LogVerbose { get; set; }
    }

    /// <summary>
    /// Group-specific options that can override global options
    /// </summary>
    public class GroupRepackOptions : GlobalRepackOptions
    {
        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("excludeFile")]
        public string ExcludeFile { get; set; }

        [JsonPropertyName("attributeFile")]
        public string AttributeFile { get; set; }
    }
}

