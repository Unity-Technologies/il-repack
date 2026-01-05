using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ILRepack.IntegrationTests.NuGet
{
    public static class Data
    {
        private static string[] supportedFwks = { "lib/netstandard2.0", "lib/net8.0" };
        public static readonly IEnumerable<Package> Packages = new[] {
            Package.From("Autofac", "9.0.0"),
            Package.From("AutoMapper", "16.0.0"),
            Package.From("Castle.Core", "5.2.1"),
            Package.From("Dapper", "2.1.66"),
            Package.From("FSharp.Core", "10.0.101"),
            Package.From("Iesi.Collections", "4.1.1"),
            Package.From("Newtonsoft.Json", "13.0.4"),
            Package.From("Ninject", "3.3.6"),
            Package.From("RestSharp", "113.0.0"),
            Package.From("SharpZipLib", "1.4.2"),
        }
        .Select(p => p.WithMatcher(file => supportedFwks.Select(d => d.Replace('/', Path.DirectorySeparatorChar)).Contains(Path.GetDirectoryName(file).ToLower())));

        public static readonly Package Ikvm = Package.From("IKVM", "8.15.0")
            .WithMatcher(file => string.Equals($"runtimes/{CurrentPlatformRuntimeName}/lib/net8.0", Path.GetDirectoryName(file), StringComparison.InvariantCultureIgnoreCase));

        static string CurrentPlatformRuntimeName
            => System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier.Split('-')[0];
        
        public static readonly IEnumerable<Platform> Platforms = Platform.From(
            Package.From("FSharp.Core", "8.0.403")
        ).WithFwks("netstandard2.0").Concat(Platform.From(
            Package.From("MassTransit", "8.5.7"),
            Package.From("MassTransit.Abstractions", "8.5.7"),
            Package.From("Newtonsoft.Json", "13.0.4")
        ).WithFwks("netstandard2.0")).Concat(new[]
        {
            Platform.From(Ikvm),
            Platform.From(
                Package.From("Paket.Core", "9.0.2").WithFwk("netstandard2.0"),
                Package.From("FSharp.Core", "8.0.403").WithFwk("netstandard2.0")
            ),
            Platform.From(
                Package.From("FSharp.Compiler.Service", "43.10.101").WithFwk("netstandard2.0"),
                Package.From("FSharp.Core", "8.0.403").WithFwk("netstandard2.0")
            )
        });
    }
}
