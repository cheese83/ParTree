using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ParTree
{
    public static class ProcessHelper
    {
        private static readonly char[] rn = new char[] { '\r', '\n' };

        public static async Task<int> RunProcessAsync(string path, params string[] arguments) => await RunProcessAsync(path, null, CancellationToken.None, arguments);
        public static async Task<int> RunProcessAsync(string path, Action<string, bool>? processStdOut, params string[] arguments) => await RunProcessAsync(path, processStdOut, CancellationToken.None, arguments);
        public static async Task<int> RunProcessAsync(string path, Action<string, bool>? processStdOut, CancellationToken token, params string[] arguments)
        {
            int exitCode;

            using (var process = new Process())
            {
                var processTcs = new TaskCompletionSource<int>();

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
                    // Some console apps update lines in stdout by writing a carriage return ('\r') to return to the beginning of the current line, allowing previous output to be overwritten.
                    // The newline char ('\n') is used to move on to the next line.
                    // There is no way to distinguish between the two using Process.OutputDataReceived, so read stdout as a charcter stream instead.
                    var buffer = new char[256];
                    var allChars = new List<char>();
                    int charsRead;

                    do
                    {
                        charsRead = await process.StandardOutput.ReadAsync(buffer, 0, buffer.Length);
                        allChars.AddRange(buffer.Take(charsRead));

                        while (allChars.Any() && !token.IsCancellationRequested)
                        {
                            var lineStartChars = allChars.Take(2).SequenceEqual(rn) ? rn : allChars.Take(1).Where(x => rn.Contains(x)).ToArray();
                            var lineChars = allChars.Skip(lineStartChars.Length).TakeWhile(x => x != '\r' && x != '\n').ToArray();
                            var lineEndChars = allChars.Skip(lineStartChars.Length + lineChars.Length).TakeWhile(x => x == '\r' || x == '\n').ToArray();
                            var newlineStarted = lineStartChars.Length == 0 || lineStartChars[0] == '\n' || lineStartChars.SequenceEqual(rn);

                            if (lineEndChars.Any())
                            {
                                var line = new string(lineChars);
                                processStdOut(line, newlineStarted);
                                allChars.RemoveRange(0, lineStartChars.Length + lineChars.Length);
                            }
                            else if (charsRead == 0)
                            {
                                // No more input, so treat everything that's left as a line.
                                var line = new string(lineChars);
                                processStdOut(line, newlineStarted);
                                break;
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                    while (charsRead > 0);
                }

                exitCode = await processTcs.Task;
            }

            return exitCode;
        }
    }
}
