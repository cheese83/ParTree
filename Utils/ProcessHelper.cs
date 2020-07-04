using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ParTree
{
    public static class ProcessHelper
    {
        public static async Task<int> RunProcessAsync(string path, params string[] arguments) => await RunProcessAsync(path, null, CancellationToken.None, arguments);
        public static async Task<int> RunProcessAsync(string path, Action<string>? processStdOut, params string[] arguments) => await RunProcessAsync(path, processStdOut, CancellationToken.None, arguments);
        public static async Task<int> RunProcessAsync(string path, Action<string>? processStdOut, CancellationToken token, params string[] arguments)
        {
            int exitCode;

            using (var process = new Process())
            {
                var tcs = new TaskCompletionSource<int>();

                process.EnableRaisingEvents = true;

                process.StartInfo = new ProcessStartInfo(path)
                {
                    CreateNoWindow = true,
                    RedirectStandardOutput = processStdOut != null,
                    StandardOutputEncoding = processStdOut != null ? Encoding.UTF8 : null, // Output will be UTF8 because all par2j commands use the /uo option.
                    Arguments = string.Join(" ", arguments)
                };

                process.Exited += (sender, e) => tcs.TrySetResult(process.ExitCode);

                // Although par2j can be cancelled by pressing 'c', it reads it using _getch, which can't read anything that can be sent from C#.
                // Just kill the process instead.
                using var cancellationRegistration = token.Register(process.Kill);

                process.Start();

                if (processStdOut != null)
                {
                    // e.Data is set to null when the stream is closed. Don't call the event handler in that case, so it doesn't have to handle nulls.
                    process.OutputDataReceived += (sender, e) => { if (e.Data != null) processStdOut(e.Data); };
                    process.BeginOutputReadLine();
                }

                exitCode = await tcs.Task;
            }

            return exitCode;
        }
    }
}
