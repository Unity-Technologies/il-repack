using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace ILRepack.IntegrationTests.Helpers
{
    public class AsyncProcess
    {
        private readonly ProcessStartInfo startInfo;
        private readonly bool throwOnNonZeroExitCode;

        public AsyncProcess(ProcessStartInfo startInfo, bool throwOnNonZeroExitCode = true)
        {
            this.startInfo = startInfo;
            this.throwOnNonZeroExitCode = throwOnNonZeroExitCode;
        }

        public async Task<(int exitCode, List<string> output)> RunAsync()
        {
            var output = new List<string>();
            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            
            var outputCompleted = new TaskCompletionSource<bool>();
            var errorCompleted = new TaskCompletionSource<bool>();
            
            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null)
                {
                    outputCompleted.TrySetResult(true);
                }
                else
                {
                    lock (output)
                    {
                        output.Add(ReparseAsciiDataAsUtf8(e.Data));
                    }
                }
            };
            
            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data == null)
                {
                    errorCompleted.TrySetResult(true);
                }
                else
                {
                    lock (output)
                    {
                        output.Add(ReparseAsciiDataAsUtf8(e.Data));
                    }
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await Task.WhenAll(outputCompleted.Task, errorCompleted.Task);
            await Task.Run(() => process.WaitForExit());

            int exitCode = process.ExitCode;
            process.Close();

            if (exitCode != 0 && throwOnNonZeroExitCode)
            {
                var error = string.Join("\n", output);
                throw new Exception(error);
            }

            return (exitCode, output);
        }

        public List<string> Output { get; } = new List<string>();

        private static string ReparseAsciiDataAsUtf8(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var bytes = new byte[input.Length * 2];
            int i = 0;
            foreach (char c in input)
            {
                bytes[i] = (byte)(c & 0xFF);
                i++;

                var msb = (byte)(c & 0xFF00 >> 16);
                if (msb > 0)
                {
                    bytes[i] = msb;
                    i++;
                }
            }

            var ret = Encoding.UTF8.GetString(bytes, 0, i);
            return ret;
        }
    }
}
