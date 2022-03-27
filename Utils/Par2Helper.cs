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
            // par2j excludes hidden files when searching with a "*" wildcard, so they must be included individually.
            var hiddenFiles = new DirectoryInfo(inputPath).EnumerateFilesOrEmpty("*", SearchOption.AllDirectories)
                .Where(f => f.Attributes.HasFlag(FileAttributes.Hidden))
                .Select(f => $"\"{f.FullName}\"");

            return await ProcessHelper.RunProcessAsync(PAR2_PATH, processStdOutLine, token, "create", "/uo", $"/rr{redundancy:0.##}", $"\"{recoveryFilePath}\"", $"\"{Path.Combine(inputPath, "*")}\"", string.Join(' ', hiddenFiles));
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
            await ProcessHelper.RunProcessAsync(PAR2_PATH, (line, newline) => parser.ProcessLine(line, newline, processStdOutLine), token, "verify", "/uo", $"/d\"{inputPath}\"", $"\"{recoveryFilePath}\"");
            return parser.Results.ToList();
        }

        public static async Task<int> Repair(string inputPath, string recoveryFilePath, Action<string, bool> processStdOutLine, CancellationToken token)
        {
            return await ProcessHelper.RunProcessAsync(PAR2_PATH, processStdOutLine, token, "repair", "/uo", $"/d\"{inputPath}\"", $"\"{recoveryFilePath}\"");
        }

        private abstract class Par2jOutputParser<T> where T : class
        {
            [Flags]
            protected enum LineType
            {
                Parsed = 1,
                Printable = 2 // If not set, this line would not normally be seen in console output, e.g. because it would be overwritten by a subsequent line.
            };

            /// <returns>True if this parser should continue to be used. False to move on to the next parser in the list for the next line</returns>
            protected delegate LineType LineParser<U>(string line, out U? result) where U : class;

            protected readonly LineParser<T> NullParser = (string line, out T? result) =>
            {
                result = default;
                return LineType.Parsed | LineType.Printable;
            };

            protected abstract IList<LineParser<T>> Parsers { get; }
            public abstract IList<T> Results { get; }

            public void ProcessLine(string line, bool newline)
            {
                ProcessLine(line, newline, callback: null);
            }

            public void ProcessLine(string line, bool newline, Action<string, bool>? callback)
            {
                var lineType = Parsers.First()(line, out var result);

                if (lineType.HasFlag(LineType.Parsed))
                {
                    if (result != null)
                        Results.Add(result);
                }
                else
                {
                    Parsers.RemoveAt(0);
                }

                if ((lineType.HasFlag(LineType.Printable) || newline) && callback != null)
                {
                    callback(line, newline);
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

            private readonly LineParser<string> UntilInputFileList = (string line, out string? result) =>
            {
                result = default;
                return LineType.Printable | (!Regex.IsMatch(line, "\\s+Size\\s+Slice\\s+(?:MD5 Hash\\s+)?:\\s+Filename") ? LineType.Parsed : 0);
            };

            private readonly LineParser<string> ParseInputFilename = (string line, out string? result) =>
            {
                var match = Regex.Match(line, "\\s+[\\d\\?]+\\s+[\\d\\?]+\\s+(?:[\\da-fA-F\\?]+\\s+)?:\\s+\"(.*[^/])\"");
                result = match.Success ? match.Groups[1].Value : null;
                return LineType.Printable | (match.Success ? LineType.Parsed : 0);
            };
        }

        private class Par2jVerifyParser : Par2jOutputParser<Par2VerifyResult>
        {
            protected override IList<LineParser<Par2VerifyResult>> Parsers { get; }
            public override IList<Par2VerifyResult> Results { get; }

            public Par2jVerifyParser()
            {
                Parsers = new List<LineParser<Par2VerifyResult>>
                {
                    UntilLoadingPar,
                    ParseLoadingPar,
                    UntilVerifyingList,
                    ParseVerifyFileStatus,
                    NullParser
                };
                Results = new List<Par2VerifyResult>();
            }

            private readonly LineParser<Par2VerifyResult> UntilLoadingPar = (string line, out Par2VerifyResult? result) =>
            {
                result = default;
                return LineType.Printable | (!Regex.IsMatch(line, "\\s+Packet\\s+Slice\\s+Status\\s+:\\s+Filename") ? LineType.Parsed : 0);
            };

            private readonly LineParser<Par2VerifyResult> ParseLoadingPar = (string line, out Par2VerifyResult? result) =>
            {
                result = default;
                var progressMatch = Regex.Match(line, "^\\s*(\\d+\\.?\\d*)(%)?(?:[^:]*)(:)?");

                return (progressMatch.Groups[2].Success || progressMatch.Groups[3].Success ? LineType.Printable : 0) | (progressMatch.Success ? LineType.Parsed : 0);
            };

            private readonly LineParser<Par2VerifyResult> UntilVerifyingList = (string line, out Par2VerifyResult? result) =>
            {
                result = default;
                return LineType.Printable | (!Regex.IsMatch(line, "\\s+Size\\s+Status\\s+:\\s+Filename") ? LineType.Parsed : 0);
            };

            private readonly LineParser<Par2VerifyResult> ParseVerifyFileStatus = (string line, out Par2VerifyResult? result) =>
            {
                var progressMatch = Regex.Match(line, "^\\s*(\\d+\\.?\\d*)(%)?");
                if (progressMatch.Success)
                {
                    result = default;
                    return (progressMatch.Groups[2].Success ? LineType.Printable : 0) | LineType.Parsed;
                }

                var completedMatch = Regex.Match(line, ".+\\s+([^\\s]+)\\s+:\\s+\"(.+)\"");
                result = completedMatch.Success ? new Par2VerifyResult(completedMatch.Groups[2].Value, completedMatch.Groups[1].Value) : null;
                return LineType.Printable | (completedMatch.Success ? LineType.Parsed : 0);
            };
        }

        public record Par2VerifyResult(string Filename, string Status);
    }
}
