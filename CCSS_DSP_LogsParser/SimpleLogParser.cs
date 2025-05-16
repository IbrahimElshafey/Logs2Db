using LogsProcessingCore.Base;
using LogsProcessingCore.Contracts;
using LogsProcessingCore.Implementations;

namespace CCSS_DSP_LogsParser
{
    internal partial class SimpleLogParser
    {
        private readonly FileChannelReader<ParsedLogLine> _lineReader;
        private readonly string _sourceSystem;
        public SimpleLogParser(string logFilePath, string sourceSystem)
        {
            _sourceSystem = sourceSystem;
            _lineReader = new FileChannelReader<ParsedLogLine>(logFilePath, channelCapacity: 500, bufferSize: 64 * 1024);
        }

        public async IAsyncEnumerable<ParsedLogLine> ParseLogsAsync(CancellationToken cancellationToken = default)
        {
            // Pass ProcessLogLine as the processing function
            var reader = await _lineReader.ProcessLogFileAsync(ProcessLogLine);
            await foreach (var parsed in reader.ReadAllAsync(cancellationToken))
            {
                if (parsed is not null)
                {
                    yield return parsed;
                }
            }
        }

        // Changed signature: takes LogLineSpan, returns ParsedLogLine?
        private ParsedLogLine? ProcessLogLine(LogLineSpan logSpan)
        {
            var raw = logSpan.Line.Span;
            //if (!raw.Contains("| ERROR |".AsSpan(), StringComparison.Ordinal) &&
            //    !raw.Contains("| WARN  |".AsSpan(), StringComparison.Ordinal))
            //    return null;

            var parsed = Log4netLineParser.Parse(logSpan);
            if (parsed == null)
                return null;

            parsed.LineHash = ComputeHash(parsed.Message);
            parsed.FilePath = logSpan.FilePath;
            parsed.Source = _sourceSystem;
            parsed.LogLineNumber = logSpan.LineIndex;
            return parsed;
        }

        

        private static string ComputeHash(string input)
        {
            var options = new LineOptimizationOptions
            {
                CheckPrefixFilterLength = 0,
                MaxLineLength = 1000,
                PrefixFilter = string.Empty,
                ReplacmentPatterns = new Dictionary<string, string>()
            };
            var hasher = new SimpleLineHasher(options);
            var hash = hasher.ProcessLine(input);
            return hash?.ToString() ?? string.Empty;
        }
    }
}
