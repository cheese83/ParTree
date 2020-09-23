using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ParTree
{
    public static class Par2Helper
    {
        private static readonly string PAR2_EXE_NAME = Environment.Is64BitProcess ? "par2j64.exe" : "par2j.exe";
        private static readonly string PAR2_PATH = Path.Combine(AppContext.BaseDirectory, "Par2", PAR2_EXE_NAME);

        public static async Task<int> Create(string inputPath, string recoveryFilePath, Action<string, bool> processStdOutLine, double redundancy, CancellationToken token)
        {
            return await ProcessHelper.RunProcessAsync(PAR2_PATH, processStdOutLine, token, "create", "/uo", $"/rr{redundancy:0.##}", $"\"{recoveryFilePath}\"", $"\"{Path.Combine(inputPath, "*")}\"");
        }

        public static async Task<IReadOnlyList<string>> List(string recoveryFilePath)
        {
            var parser = new Par2jListParser();
            await ProcessHelper.RunProcessAsync(PAR2_PATH, parser.ProcessLine, "list", "/uo", $"\"{recoveryFilePath}\"");
            return parser.Results.ToList();
        }

        public static async Task<IReadOnlyList<Par2VerifyResult>> Verify(string inputPath, string recoveryFilePath, Action<string, bool> processStdOutLine, CancellationToken token)
        {
            var parser = new Par2jVerifyParser();
            await ProcessHelper.RunProcessAsync(PAR2_PATH, (line, newline) => { parser.ProcessLine(line, newline); processStdOutLine(line, newline); }, token, "verify", "/uo", $"/d\"{inputPath}\"", $"\"{recoveryFilePath}\"");
            return parser.Results.ToList();
        }

        public static async Task<int> Repair(string inputPath, string recoveryFilePath, Action<string, bool> processStdOutLine, CancellationToken token)
        {
            return await ProcessHelper.RunProcessAsync(PAR2_PATH, processStdOutLine, token, "repair", "/uo", $"/d\"{inputPath}\"", $"\"{recoveryFilePath}\"");
        }

        private abstract class Par2jOutputParser<T> where T : class
        {
            /// <returns>True if this parser should continue to be used. False to move on to the next parser in the list for the next line</returns>
            protected delegate bool LineParser<U>(string line, out U? result) where U : class;

            protected bool NullParser(string line, out T? result)
            {
                result = default;
                return true;
            }

            protected abstract IList<LineParser<T>> Parsers { get; }
            public abstract IList<T> Results { get; }

            public void ProcessLine(string line, bool newline)
            {
                if (Parsers.First()(line, out var result))
                {
                    if (result != null)
                        Results.Add(result);
                }
                else
                {
                    Parsers.RemoveAt(0);
                }
            }
        }

        private class Par2jListParser : Par2jOutputParser<string>
        {
            protected override IList<LineParser<string>> Parsers { get; }
            public override IList<string> Results { get; }

            public Par2jListParser()
            {
                Parsers = new List<LineParser<string>>
                {
                    UntilInputFileList,
                    ParseInputFilename,
                    NullParser
                };
                Results = new List<string>();
            }

            private bool UntilInputFileList(string line, out string? result)
            {
                result = default;
                return !Regex.IsMatch(line, "\\s+Size\\s+Slice\\s+(?:MD5 Hash\\s+)?:\\s+Filename");
            }

            private bool ParseInputFilename(string line, out string? result)
            {
                var match = Regex.Match(line, "\\s+[\\d\\?]+\\s+[\\d\\?]+\\s+(?:[\\da-fA-F\\?]+\\s+)?:\\s+\"(.*[^/])\"");
                result = match.Success ? match.Groups[1].Value : null;
                return match.Success;
            }
        }

        private class Par2jVerifyParser : Par2jOutputParser<Par2VerifyResult>
        {
            protected override IList<LineParser<Par2VerifyResult>> Parsers { get; }
            public override IList<Par2VerifyResult> Results { get; }

            public Par2jVerifyParser()
            {
                Parsers = new List<LineParser<Par2VerifyResult>>
                {
                    UntilVerifyingList,
                    ParseVerifyFileStatus,
                    NullParser
                };
                Results = new List<Par2VerifyResult>();
            }

            private bool UntilVerifyingList(string line, out Par2VerifyResult? result)
            {
                result = default;
                return !Regex.IsMatch(line, "\\s+Size\\s+Status\\s+:\\s+Filename");
            }

            private bool ParseVerifyFileStatus(string line, out Par2VerifyResult? result)
            {
                if (Regex.IsMatch(line, "^\\s*\\d+"))
                {
                    result = default;
                    return true;
                }

                var match = Regex.Match(line, ".+\\s+([^\\s]+)\\s+:\\s+\"(.+)\"");
                result = match.Success ? new Par2VerifyResult(match.Groups[2].Value, match.Groups[1].Value) : null;
                return match.Success;
            }
        }

        public class Par2VerifyResult
        {
            public string Filename { get; }
            public string Status { get; }

            public Par2VerifyResult(string filename, string status)
            {
                Filename = filename;
                Status = status;
            }
        }
    }
}
