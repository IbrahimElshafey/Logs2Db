using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using LogsProcessingCore.Base;
using LogsProcessingCore.Contracts;
using LogsProcessingCore.Implementations;

namespace CCSS_DSP_LogsParser;

internal partial class Log4NetLogs2Db
{
    private readonly LogFileLineProducer _logLinesProducer;
    private readonly string _sourceSystem;
    private readonly IProgress<float>? _progressPercent;
    private readonly LogsDbContext _dbContext;
    [GeneratedRegex(@"^\s*(?<CodeLineNumber>\d+)\s*\|\s*(?<Logger>[^|]+?)\s*\|\s*(?<Thread>[^|]*)\s*\|\s*(?<Numeric>\d+)\s*\|\s*(?<Timestamp>\d{4}-\d{2}-\d{2}\s\d{2}:\d{2}:\d{2},\d+)\s*\|\s*(?<Level>\w+)\s*\|\s*(?<User>[^|]*)\s*\|\s*(?<Machine>[^|]*)\s*\|\s*(?<Host>[^|]*)\s*\|\s*(?<Message>.*)$", RegexOptions.Compiled)]
    private static partial Regex LogRegex();

    public Log4NetLogs2Db(string folderPath, string sourceSystem)
    {
        _sourceSystem = sourceSystem;
        _progressPercent = new Progress<float>(percent =>
        {
            Console.WriteLine($"Progress: {percent}%");
        });
        var logFilesOptions = new LogFilesOptions
        {
            LogFilesFolder = folderPath,
            SearchPattern = "*",
            EnumerationOptions = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                ReturnSpecialDirectories = false,
                AttributesToSkip = FileAttributes.System | FileAttributes.Hidden
            },
        };
        var dataflowConfig = new ProduceLinesDataflowConfiguration
        {
            PathsBoundedCapacity = 2,
            PathToLinesParallelism = 5,
            PathToLinesBoundedCapacity = 100,
            BulkReadSize = 1024 * 1024,
        };
        _dbContext = new LogsDbContext();
        _logLinesProducer = new LogFileLineProducer(logFilesOptions, dataflowConfig);
    }

    public async Task ParseLogs()
    {
        // Block to parse and filter lines
        var parseBlock = new TransformBlock<LogLine, ParsedLogLine>(ProcessLogLine, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = Environment.ProcessorCount });

        // Remove nulls
        var filterBlock = new TransformManyBlock<ParsedLogLine, ParsedLogLine>(parsed =>
            parsed is null ? Array.Empty<ParsedLogLine>() : new[] { parsed });

        // Batch into groups of 100
        var batchBlock = new BatchBlock<ParsedLogLine>(100);

        // Save to database
        var saveBlock = new ActionBlock<ParsedLogLine[]>(SaveBatch,
            new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1 });

        // Link blocks with propagation
        _logLinesProducer.Build(_progressPercent).LinkTo(parseBlock, new DataflowLinkOptions { PropagateCompletion = true });
        parseBlock.LinkTo(filterBlock, new DataflowLinkOptions { PropagateCompletion = true });
        filterBlock.LinkTo(batchBlock, new DataflowLinkOptions { PropagateCompletion = true });
        batchBlock.LinkTo(saveBlock, new DataflowLinkOptions { PropagateCompletion = true });
        _ = _logLinesProducer.PostAllFilePathsAsync(CancellationToken.None);
        await saveBlock.Completion;
    }

    private async Task SaveBatch(ParsedLogLine[] batch)
    {
        _dbContext.ParsedLogLines.AddRange(batch);
        await _dbContext.SaveChangesAsync();
    }

    private ParsedLogLine? ProcessLogLine(LogLine logLine)
    {
        var rawLine = logLine.Line.AsSpan();
        if (!rawLine.Contains("| ERROR |".AsSpan(), StringComparison.Ordinal) &&
            !rawLine.Contains("| WARN  |".AsSpan(), StringComparison.Ordinal))
            return null;
        var parsed = Log4netLineParser.Parse(logLine);
        if (parsed is null)
            return null;

        parsed.LineHash = ComputeHash(parsed.Message);
        parsed.LogFileName = Path.GetFileName(logLine.FilePath);
        parsed.Source = _sourceSystem;
        return parsed;
    }

    private static string ComputeHash(string input)
    {
        var lineOptimalizationOptions = new LineOptimizationOptions
        {
            CheckPrefixFilterLength = 0,
            MaxLineLength = 1000,
            PrefixFilter = string.Empty,
            ReplacmentPatterns = new Dictionary<string, string>()
        };
        var simpleLineHasher = new SimpleLineHasher(lineOptimalizationOptions);
        var hash = simpleLineHasher.ProcessLine(input);
        return hash?.ToString() ?? string.Empty;
    }
}
