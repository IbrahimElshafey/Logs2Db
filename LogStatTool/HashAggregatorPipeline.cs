using ClosedXML.Excel;
using LogStatTool.Base;
using LogStatTool.Helpers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace LogStatTool;

public class HashAggregatorPipeline
{
    private readonly GetLogFilesOptions _logFilesOptions;
    private readonly Base.ILogLineProcessor<ulong?> _hasher;
    private readonly int _concurrency;
    private readonly int _bulkReadSize;

    // Whether to write results to a file automatically, and optionally open it.
    private readonly bool _openResultFile;
    private readonly string? _resultsFilePath; // if null, we'll generate a unique filename

    // Holds (hash => (representative line, count))
    private readonly ConcurrentDictionary<ulong?, (string Representative, int Count)> _globalResults
        = new ConcurrentDictionary<ulong?, (string, int)>();

    // We'll store the overall pipeline's final Task, so we know when it's done.
    private Task? _runTask;

    // This block will emit final results if the user wants them as a dataflow block
    private BufferBlock<(string line, int count)>? _outputBlock;

    public HashAggregatorPipeline(HashAggregatorOptions options)
    {
        GetLogFilesOptions logFilesOptions = options.LogFilesOptions;
        Base.ILogLineProcessor<ulong?> hasher = options.Hasher;
        int concurrency = options.Concurrency;
        int bulkReadSize = options.BulkReadLinesSize;
        string? resultsFilePath = options.ResultsFilePath;
        bool openResultFile = options.OpenResultFile;

        _logFilesOptions = logFilesOptions ?? throw new ArgumentNullException(nameof(logFilesOptions));
        _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));

        if (concurrency <= 0) throw new ArgumentOutOfRangeException(nameof(concurrency));
        if (bulkReadSize <= 0) throw new ArgumentOutOfRangeException(nameof(bulkReadSize));

        _concurrency = concurrency;
        _bulkReadSize = bulkReadSize;
        _openResultFile = openResultFile;
        _resultsFilePath = resultsFilePath;
    }

    /// <summary>
    /// Builds and runs the entire pipeline:
    ///   1) Reads lines in parallel (LogFileLineProducer).
    ///   2) Hashes each line.
    ///   3) Accumulates into _globalResults.
    /// On completion, optionally saves and/or opens the results file.
    /// 
    /// Call this once. Then you can retrieve results via GetOrderedResults() or GetResultsBlock().
    /// </summary>
    /// <param name="progress">Optional progress for file enumeration/reading (0-100%).</param>
    /// <param name="cancellationToken"></param>
    /// <returns>A Task you can await. The pipeline completes when it finishes reading + aggregating.</returns>
    public Task BuildAndRunAsync(IProgress<float>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_runTask != null)
        {
            throw new InvalidOperationException("Pipeline has already been built or run.");
        }

        // 1) Set up the file-reading pipeline internally
        var logLinesProducer = new LogFileLineProducer(
            options: _logFilesOptions,
            concurrency: _concurrency,
            bulkReadSize: _bulkReadSize
        );

        // The source block that emits lines
        var linesBlock = logLinesProducer.Build(progress, cancellationToken);

        // 2) Create a TransformBlock to hash each line
        var hashLinesBlock = new TransformBlock<string, ulong?>(
            line =>
            {
                var hash = _hasher.ProcessLine(ref line);
                if (hash != null)
                {
                    _globalResults.AddOrUpdate(
                        hash,
                        _ => (line, 1),
                        (_, oldVal) => (oldVal.Representative, oldVal.Count + 1)
                    );
                }

                return hash;
            },
            new ExecutionDataflowBlockOptions
            {
                //BoundedCapacity = 1000,
                BoundedCapacity = -1,
                MaxDegreeOfParallelism = _concurrency
            });


        // 4) Link them: source -> hash -> aggregate
        linesBlock.LinkTo(hashLinesBlock, new DataflowLinkOptions { PropagateCompletion = true });
        //hashLinesBlock.LinkTo(aggregateBlock, new DataflowLinkOptions { PropagateCompletion = true });

        // 5) We define the overall run task
        _runTask = Task.Run(async () =>
        {
            // a) Start enumerating file paths => triggers line emission
            await logLinesProducer.PostAllFilePathsAsync(cancellationToken);

            // b) Wait for the reading pipeline to finish
            await logLinesProducer.Completion.ConfigureAwait(false);
            //await hashLinesBlock.Completion.ConfigureAwait(false);
            // d) If requested, save results to file
            if (string.IsNullOrWhiteSpace(_resultsFilePath) is false)
            {
                SaveResultsToFile();
            }

            // e) If user wants to open the file
            if (_openResultFile)
            {
                OpenResultsFile();
            }

            // f) If the user wants a dataflow block with final results,
            //    we populate it now, then Complete it.
            if (_outputBlock != null)
            {
                var finalOrdered = ProduceResults();
                foreach (var item in finalOrdered)
                {
                    // item is (string line, int count)
                    _outputBlock.Post(item);
                }
                _outputBlock.Complete();
                //free all resources
                _outputBlock = null;
                _globalResults.Clear();
            }
        }, cancellationToken);

        return _runTask;
    }

    /// <summary>
    /// Returns an in-memory data of (line, count), sorted descending by count (then by line).
    /// Will await the pipeline first if it isn't completed.
    /// </summary>
    public async Task<List<(string line, int count)>> GetOrderedResultsAsync()
    {
        if (_runTask == null)
            throw new InvalidOperationException("You must call BuildAndRunAsync() first.");

        await _runTask.ConfigureAwait(false);
        return ProduceResults();
    }

    /// <summary>
    /// Returns an ISourceBlock that will emit the final (line, count) results,
    /// in sorted order, *after* the pipeline finishes.
    /// You can link this block to further Dataflow blocks for additional processing.
    /// 
    /// Note: The messages (line, count) will only start appearing *after* the entire 
    /// aggregation pipeline completes. Then they are posted all at once, and this block completes.
    /// </summary>
    public ISourceBlock<(string line, int count)> GetResultsBlock()
    {
        if (_runTask == null)
            throw new InvalidOperationException("You must call BuildAndRunAsync() first.");

        if (_outputBlock == null)
        {
            // We create a BufferBlock that we'll fill after aggregator finishes
            _outputBlock = new BufferBlock<(string line, int count)>();
        }

        // If the pipeline has *already* finished, we fill _outputBlock immediately
        // or we do so in the continuation step of BuildAndRunAsync.
        // The code in BuildAndRunAsync() already handles filling + completing _outputBlock.

        return _outputBlock;
    }

    //-----------------------------------------------------------------------------------------
    // PRIVATE HELPER METHODS
    //-----------------------------------------------------------------------------------------

    private List<(string line, int count)> ProduceResults()
    {
        // Convert dictionary to data, sort it
        // _globalResults is (ulong => (string, int))
        var list = _globalResults.Values
            //.OrderByDescending(x => x.Representative)
            //.ThenByDescending(x => x.Count)
            .Select(x => (x.Representative, x.Count))
            .ToList();

        return list;
    }

    private void SaveResultsToFile()
    {
        Console.WriteLine("Start to save results to Excel sheet.");
        // If user provided a custom path, use it; otherwise generate a unique one.
        //string filePath = _resultsFilePath ?? $"results-{Guid.NewGuid()}.txt";

        var data = ProduceResults();
        //await File.WriteAllLinesAsync(_resultsFilePath, data.Select(x =>
        //    JsonSerializer.Serialize(new { Line = x.line, Count = x.count })
        //));
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Report");

        // Set headers
        worksheet.Cell(1, 1).Value = "Representative Line";
        worksheet.Cell(1, 2).Value = "Count";

        // Freeze the first row
        worksheet.SheetView.FreezeRows(1);

        // Fill data
        for (int i = 0; i < data.Count; i++)
        {
            worksheet.Cell(i + 2, 1).Value = data[i].line;
            worksheet.Cell(i + 2, 2).Value = data[i].count;
        }

        // Set wrapping and max width for the "Representative Line" column
        var col1 = worksheet.Column(1);
        col1.Width = 100; // Set initial width
        col1.Style.Alignment.WrapText = true;
        if (col1.Width > 500) col1.Width = 500;

        // Auto-adjust "Count" column
        worksheet.Column(2).AdjustToContents();

        // Optional: Make header bold
        worksheet.Row(1).Style.Font.Bold = true;

        // Save to file
        workbook.SaveAs(_resultsFilePath);

        Console.WriteLine($"Results saved to Excel file: {_resultsFilePath}");
    }

    private void OpenResultsFile()
    {
        if (_resultsFilePath == null)
        {
            Console.WriteLine("No results file available to open.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.GetFullPath(_resultsFilePath),
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unable to open file: {_resultsFilePath}\nError: {ex.Message}");
        }
    }
}
