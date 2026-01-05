using ICSharpCode.SharpZipLib.Zip;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace ILRepack.IntegrationTests.NuGet
{
    static class NuGetHelpers
    {
        private static readonly HttpClient httpClient = new HttpClient();

        private static async Task<byte[]> DownloadWithRetryAsync(Uri uri, int maxRetries = 5)
        {
            Exception lastException = null;
            
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    return await httpClient.GetByteArrayAsync(uri);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    if (attempt < maxRetries - 1)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt))); // Exponential backoff
                    }
                }
            }
            
            throw new Exception($"Failed to download after {maxRetries} attempts", lastException);
        }

        private static bool IsDllOrExe(string normalizedName)
        {
            return Path.GetExtension(normalizedName) == ".dll" || Path.GetExtension(normalizedName) == ".exe";
        }
 
        public static async Task<List<(string normalizedName, Func<Stream> streamProvider)>> GetNupkgAssembliesAsync(Package package)
        {
            var allContent = await GetNupkgContentAsync(package);

            allContent.RemoveAll(t => !IsDllOrExe(t.normalizedName));
            allContent.RemoveAll(t => !package.Matches(t));

            return allContent;
        } 
 
        public static async Task<List<(string normalizedName, Func<Stream> streamProvider)>> GetNupkgContentAsync(Package package)
        {
            var downloadBytes = await DownloadWithRetryAsync(
                new Uri($"http://nuget.org/api/v2/package/{package.Name}/{package.Version}"));
            
            return ExtractZipContent(downloadBytes);
        }

        private static List<(string normalizedName, Func<Stream> streamProvider)> ExtractZipContent(byte[] downloadBytes)
        {
            var results = new List<(string normalizedName, Func<Stream> streamProvider)>();
            
            // We need to keep the zip file data in memory since we're returning Func<Stream>
            var zipData = new byte[downloadBytes.Length];
            Array.Copy(downloadBytes, zipData, downloadBytes.Length);
            
            using (var zipFile = new ZipFile(new MemoryStream(downloadBytes)))
            {
                foreach (ZipEntry entry in zipFile)
                {
                    // Create a closure over the entry data
                    var entryData = new byte[entry.Size];
                    if (entry.Size > 0)
                    {
                        using (var stream = zipFile.GetInputStream(entry))
                        {
                            int offset = 0;
                            int remaining = (int)entry.Size;
                            while (remaining > 0)
                            {
                                int read = stream.Read(entryData, offset, remaining);
                                if (read <= 0)
                                    break;
                                offset += read;
                                remaining -= read;
                            }
                        }
                    }
                    
                    var normalizedName = entry.Name
                        .Replace('\\', Path.DirectorySeparatorChar)
                        .Replace('/', Path.DirectorySeparatorChar)
                        .Replace("%2B", "+");
                    
                    results.Add((
                        normalizedName,
                        () => new MemoryStream(entryData)));
                }
            }
            
            return results;
        }
    }
}
