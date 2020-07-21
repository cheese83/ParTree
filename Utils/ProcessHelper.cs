﻿using System;
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
                var processTcs = new TaskCompletionSource<int>();
                var stdOutTcs = new TaskCompletionSource<int>();

                process.EnableRaisingEvents = true;

                process.StartInfo = new ProcessStartInfo(path)
                {
                    CreateNoWindow = true,
                    RedirectStandardOutput = processStdOut != null,
                    StandardOutputEncoding = processStdOut != null ? Encoding.UTF8 : null, // Output will be UTF8 because all par2j commands use the /uo option.
                    Arguments = string.Join(" ", arguments)
                };

                process.Exited += (sender, e) => processTcs.TrySetResult(process.ExitCode);

                // Although par2j can be cancelled by pressing 'c', it reads it using _getch, which can't read anything that can be sent from C#.
                // Just kill the process instead.
                using var cancellationRegistration = token.Register(process.Kill);

                process.Start();

                if (processStdOut != null)
                {
                    process.OutputDataReceived += (sender, e) =>
                    {
                        // e.Data is set to null when the stream is closed. Don't call the event handler in that case, so it doesn't have to handle nulls.
                        if (e.Data == null)
                            stdOutTcs.TrySetResult(0);
                        else
                            processStdOut(e.Data);
                    };
                    process.BeginOutputReadLine();
                    // Wait, otherwise the process could be disposed before the last few lines have been received.
                    await stdOutTcs.Task;
                }

                exitCode = await processTcs.Task;
            }

            return exitCode;
        }
    }
}
