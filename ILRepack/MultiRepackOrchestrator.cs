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
using System.Diagnostics;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace ILRepacking
{
    /// <summary>
    /// Orchestrates multiple repack operations with cross-assembly reference rewriting
    /// </summary>
    public class MultiRepackOrchestrator : IDisposable
    {
        private readonly MultiRepackConfiguration _config;
        private readonly ILogger _logger;
        private readonly Dictionary<string, AssemblyGroup> _assemblyToGroupMap = new Dictionary<string, AssemblyGroup>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _outputAssemblyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<AssemblyDefinition> _alreadyMergedAssemblies = new List<AssemblyDefinition>();
        private List<AssemblyGroup> _sortedGroups;

        public MultiRepackOrchestrator(MultiRepackConfiguration config, ILogger logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Determines the processing order for assembly groups based on their dependencies.
        /// Returns the groups in topologically sorted order (dependencies first).
        /// </summary>
        /// <returns>List of assembly groups in the order they should be processed</returns>
        internal List<AssemblyGroup> DetermineProcessingOrder()
        {
            // Build mapping of input assemblies to groups
            BuildAssemblyGroupMap();

            // Detect circular dependencies and determine merge order
            return TopologicalSort();
        }

        /// <summary>
        /// Performs the multi-assembly repack operation
        /// </summary>
        public void Repack()
        {
            var timer = Stopwatch.StartNew();
            _logger.Info("Starting multi-assembly repack");
            _logger.Info($"Processing {_config.Groups.Count} assembly groups");

            try
            {
                // Determine merge order
                _sortedGroups = DetermineProcessingOrder();
                _logger.Info($"Merge order determined: {string.Join(" -> ", _sortedGroups.Select(g => g.Name ?? g.OutputAssembly))}");

                // Perform each repack operation in order
                for (int i = 0; i < _sortedGroups.Count; i++)
                {
                    var group = _sortedGroups[i];
                    _logger.Info($"Processing group {i + 1}/{_sortedGroups.Count}: {group.Name ?? group.OutputAssembly}");
                    
                    var mergedAssemblies = PerformGroupRepack(group);
                    
                    // Track the output assembly for reference rewriting
                    foreach (var assembly in mergedAssemblies)
                    {
                        var assemblyName = assembly.Name.Name;
                        _outputAssemblyMap[assemblyName] = group.OutputAssembly;
                    }
                    
                    // Rewrite references in the just-merged assembly to point to previously merged assemblies
                    if (i > 0)
                    {
                        RewriteAssemblyReferences(group.OutputAssembly);
                    }
                    
                    _alreadyMergedAssemblies.AddRange(mergedAssemblies);
                }

                _logger.Info($"Multi-assembly repack completed in {timer.Elapsed}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Multi-assembly repack failed: {ex.Message}");
                throw;
            }
        }

        private void BuildAssemblyGroupMap()
        {
            foreach (var group in _config.Groups)
            {
                foreach (var assembly in group.InputAssemblies)
                {
                    var assemblyName = Path.GetFileNameWithoutExtension(assembly);
                    if (_assemblyToGroupMap.ContainsKey(assemblyName))
                    {
                        throw new InvalidOperationException(
                            $"Assembly '{assemblyName}' appears in multiple groups. Each input assembly must belong to exactly one group.");
                    }
                    _assemblyToGroupMap[assemblyName] = group;
                }
            }
        }

        /// <summary>
        /// Performs topological sort to determine merge order and detect circular dependencies
        /// </summary>
        private List<AssemblyGroup> TopologicalSort()
        {
            var dependencies = BuildDependencyGraph();
            
            var sorted = new List<AssemblyGroup>();
            var visited = new HashSet<AssemblyGroup>();
            var visiting = new HashSet<AssemblyGroup>();

            foreach (var group in _config.Groups)
            {
                if (!visited.Contains(group))
                {
                    Visit(group, dependencies, visited, visiting, sorted);
                }
            }

            return sorted;
        }

        private void Visit(
            AssemblyGroup group,
            Dictionary<AssemblyGroup, HashSet<AssemblyGroup>> dependencies,
            HashSet<AssemblyGroup> visited,
            HashSet<AssemblyGroup> visiting,
            List<AssemblyGroup> sorted)
        {
            if (visiting.Contains(group))
            {
                var cycle = BuildCycleDescription(group, visiting);
                throw new InvalidOperationException(
                    $"Circular dependency detected between assembly groups: {cycle}");
            }

            if (visited.Contains(group))
                return;

            visiting.Add(group);

            if (dependencies.TryGetValue(group, out var deps))
            {
                foreach (var dep in deps)
                {
                    Visit(dep, dependencies, visited, visiting, sorted);
                }
            }

            visiting.Remove(group);
            visited.Add(group);
            sorted.Add(group);
        }

        private string BuildCycleDescription(AssemblyGroup startGroup, HashSet<AssemblyGroup> visiting)
        {
            var cycle = visiting.Select(g => g.Name ?? g.OutputAssembly).ToList();
            return string.Join(" -> ", cycle) + $" -> {startGroup.Name ?? startGroup.OutputAssembly}";
        }

        /// <summary>
        /// Builds a dependency graph between assembly groups
        /// </summary>
        private Dictionary<AssemblyGroup, HashSet<AssemblyGroup>> BuildDependencyGraph()
        {
            var dependencies = new Dictionary<AssemblyGroup, HashSet<AssemblyGroup>>();

            foreach (var group in _config.Groups)
            {
                var groupDeps = new HashSet<AssemblyGroup>();

                foreach (var inputAssembly in group.InputAssemblies)
                {
                    if (!File.Exists(inputAssembly))
                    {
                        _logger.Warn($"Input assembly not found: {inputAssembly}");
                        continue;
                    }

                    try
                    {
                        using (var assembly = AssemblyDefinition.ReadAssembly(inputAssembly, new ReaderParameters { ReadingMode = ReadingMode.Deferred }))
                        {
                            foreach (var reference in assembly.MainModule.AssemblyReferences)
                            {
                                if (_assemblyToGroupMap.TryGetValue(reference.Name, out var depGroup))
                                {
                                    if (depGroup != group)
                                    {
                                        groupDeps.Add(depGroup);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn($"Failed to read assembly {inputAssembly}: {ex.Message}");
                    }
                }

                if (groupDeps.Count > 0)
                {
                    dependencies[group] = groupDeps;
                }
            }

            return dependencies;
        }

        private IList<AssemblyDefinition> PerformGroupRepack(AssemblyGroup group)
        {
            var options = CreateRepackOptions(group);
            
            using (var repack = new ILRepack(options, _logger))
            {
                // If this is not the first group, we need to use a custom assembly resolver
                // that can redirect references to merged assemblies
                if (_outputAssemblyMap.Count > 0)
                {
                    SetupReferenceRedirection(repack.GlobalAssemblyResolver);
                }

                repack.Repack();

                return repack.MergedAssemblies;
            }
        }

        private void SetupReferenceRedirection(RepackAssemblyResolver resolver)
        {
            // Register the already-merged assemblies with the resolver
            // and add their directories to the search path
            var addedDirectories = new HashSet<string>();
            
            foreach (var kvp in _outputAssemblyMap)
            {
                var inputAssemblyName = kvp.Key;
                var outputPath = kvp.Value;
                
                if (File.Exists(outputPath))
                {
                    try
                    {
                        // Add the output directory to search directories
                        var outputDir = Path.GetDirectoryName(outputPath);
                        if (!string.IsNullOrEmpty(outputDir) && addedDirectories.Add(outputDir))
                        {
                            resolver.AddSearchDirectory(outputDir);
                        }
                        
                        // Read and register the merged assembly
                        var assembly = AssemblyDefinition.ReadAssembly(outputPath, new ReaderParameters
                        {
                            AssemblyResolver = resolver,
                            ReadingMode = ReadingMode.Deferred
                        });
                        resolver.RegisterAssembly(assembly);
                        
                        _logger.Verbose($"Registered merged assembly: {inputAssemblyName} -> {Path.GetFileName(outputPath)}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn($"Failed to register merged assembly {outputPath}: {ex.Message}");
                    }
                }
            }
        }

        private RepackOptions CreateRepackOptions(AssemblyGroup group)
        {
            var options = new RepackOptions();

            // Set input assemblies
            options.InputAssemblies = group.InputAssemblies.ToArray();
            options.OutputFile = group.OutputAssembly;

            // Initialize SearchDirectories to empty list to avoid null reference
            options.SearchDirectories = new List<string>();

            // Apply global options
            ApplyGlobalOptions(options, _config.GlobalOptions);

            // Apply group-specific options (these override global options)
            if (group.Options != null)
            {
                ApplyGroupOptions(options, group.Options);
            }

            return options;
        }

        private void ApplyGlobalOptions(RepackOptions options, GlobalRepackOptions globalOptions)
        {
            if (globalOptions.SearchDirectories != null && globalOptions.SearchDirectories.Count > 0)
                options.SearchDirectories = globalOptions.SearchDirectories;

            if (globalOptions.Internalize.HasValue)
                options.Internalize = globalOptions.Internalize.Value;

            if (globalOptions.DebugInfo.HasValue)
                options.DebugInfo = globalOptions.DebugInfo.Value;

            if (globalOptions.CopyAttributes.HasValue)
                options.CopyAttributes = globalOptions.CopyAttributes.Value;

            if (globalOptions.AllowMultipleAssemblyLevelAttributes.HasValue)
                options.AllowMultipleAssemblyLevelAttributes = globalOptions.AllowMultipleAssemblyLevelAttributes.Value;

            if (globalOptions.XmlDocumentation.HasValue)
                options.XmlDocumentation = globalOptions.XmlDocumentation.Value;

            if (globalOptions.Union.HasValue)
                options.UnionMerge = globalOptions.Union.Value;

            if (globalOptions.Parallel.HasValue)
                options.Parallel = globalOptions.Parallel.Value;

            if (globalOptions.AllowWildCards.HasValue)
                options.AllowWildCards = globalOptions.AllowWildCards.Value;

            if (globalOptions.AllowZeroPeKind.HasValue)
                options.AllowZeroPeKind = globalOptions.AllowZeroPeKind.Value;

            if (globalOptions.AllowDuplicateResources.HasValue)
                options.AllowDuplicateResources = globalOptions.AllowDuplicateResources.Value;

            if (globalOptions.Log.HasValue)
                options.Log = globalOptions.Log.Value;

            if (globalOptions.LogVerbose.HasValue)
                options.LogVerbose = globalOptions.LogVerbose.Value;

            if (!string.IsNullOrWhiteSpace(globalOptions.TargetKind))
            {
                options.TargetKind = ParseTargetKind(globalOptions.TargetKind);
            }

            if (!string.IsNullOrWhiteSpace(globalOptions.TargetPlatformVersion))
                options.TargetPlatformVersion = globalOptions.TargetPlatformVersion;
        }

        private void ApplyGroupOptions(RepackOptions options, GroupRepackOptions groupOptions)
        {
            // Apply base global options
            ApplyGlobalOptions(options, groupOptions);

            // Apply group-specific options
            if (!string.IsNullOrWhiteSpace(groupOptions.Version))
                options.Version = new Version(groupOptions.Version);

            if (!string.IsNullOrWhiteSpace(groupOptions.ExcludeFile))
                options.ExcludeFile = groupOptions.ExcludeFile;

            if (!string.IsNullOrWhiteSpace(groupOptions.AttributeFile))
                options.AttributeFile = groupOptions.AttributeFile;
        }

        private ILRepack.Kind ParseTargetKind(string targetKind)
        {
            switch (targetKind.ToLowerInvariant())
            {
                case "dll":
                case "library":
                    return ILRepack.Kind.Dll;
                case "exe":
                    return ILRepack.Kind.Exe;
                case "winexe":
                    return ILRepack.Kind.WinExe;
                default:
                    throw new ArgumentException($"Invalid target kind: {targetKind}");
            }
        }

        /// <summary>
        /// Rewrites assembly references in a merged assembly to point to other merged assemblies
        /// </summary>
        private void RewriteAssemblyReferences(string assemblyPath)
        {
            if (!File.Exists(assemblyPath))
                return;

            try
            {
                var resolver = new RepackAssemblyResolver();
                resolver.Mode = AssemblyResolverMode.Core;
                
                foreach (var dir in _config.GlobalOptions.SearchDirectories)
                    resolver.AddSearchDirectory(dir);
                SetupReferenceRedirection(resolver);
                foreach (var alreadyMergedAssembly in _alreadyMergedAssemblies) 
                    resolver.RegisterAssembly(alreadyMergedAssembly);

                var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters(ReadingMode.Immediate)
                {
                    InMemory = true,
                    AssemblyResolver = resolver
                });
                
                bool modified = false;

                // Check each assembly reference
                for (int i = 0; i < assembly.MainModule.AssemblyReferences.Count; i++)
                {
                    var reference = assembly.MainModule.AssemblyReferences[i];
                    
                    // Check if this reference should be redirected to a merged assembly
                    if (_outputAssemblyMap.TryGetValue(reference.Name, out var mergedAssemblyPath))
                    {
                        // Get the name of the merged assembly
                        var mergedAssemblyName = Path.GetFileNameWithoutExtension(mergedAssemblyPath);
                        
                        // Only rewrite if the names are different
                        if (reference.Name != mergedAssemblyName)
                        {
                            _logger.Info($"Rewriting reference: {reference.Name} -> {mergedAssemblyName}");
                            
                            // Create a new reference with the merged assembly name
                            var newReference = new AssemblyNameReference(mergedAssemblyName, reference.Version)
                            {
                                Culture = reference.Culture,
                                PublicKeyToken = reference.PublicKeyToken,
                                HashAlgorithm = reference.HashAlgorithm
                            };
                            
                            assembly.MainModule.AssemblyReferences[i] = newReference;
                            modified = true;
                        }
                    }
                }

                // Save the assembly if we made changes
                if (modified)
                {
                    assembly.Write(assemblyPath);
                    _logger.Verbose($"Updated assembly references in {Path.GetFileName(assemblyPath)}");
                }

                assembly.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to rewrite references in {assemblyPath}: {ex.Message}");
                throw;
            }
        }

        public void Dispose()
        {
            // Cleanup if needed
        }
    }
}

