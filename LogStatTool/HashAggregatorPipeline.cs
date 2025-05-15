using ClosedXML.Excel;
using LogsProcessingCore.Contracts;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks.Dataflow;

namespace LogStatTool;

public class HashAggregatorPipeline
{
    private readonly LogsProcessingCore.Base.ILogLineProcessor<ulong?> _hasher;
    private readonly LogStatTool.HashAggregatorOptions _options;


    // Holds (hash => (representative logLine, count))
    private readonly ConcurrentDictionary<ulong?, (string Representative, int Count)> _globalResults
        = new ConcurrentDictionary<ulong?, (string, int)>();

    // We'll store the overall pipeline's final Task, so we know when it's done.
    private Task? _runTask;

        
    public HashAggregatorPipeline(LogStatTool.HashAggregatorOptions options)
    {

        if (options.LogFilesOptions == null)
            throw new ArgumentNullException(nameof(options.LogFilesOptions));
        _hasher = options.Hasher ?? throw new ArgumentNullException(nameof(options.Hasher));

        if (options.HashLinesDataflowConfiguration.Parallelism <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.HashLinesDataflowConfiguration.Parallelism));
        if (options.HashLinesDataflowConfiguration.BoundedCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.HashLinesDataflowConfiguration.BoundedCapacity));
        _options = options;
    }

    /// <summary>
    /// Builds and runs the entire pipeline:
    ///   1) Reads lines in parallel (LogFileLineProducer).
    ///   2) Hashes each logLine.
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
        var logLinesProducer = new LogsProcessingCore.Base.LogFileLineProducer(
            options: _options.LogFilesOptions,
            dataflowConfiguration: _options.ProduceLinesDataflowConfiguration
        );

        // The source block that emits lines
        var linesBlock = logLinesProducer.Build(progress, cancellationToken);

        // 2) Create a TransformBlock to hash each logLine
        var hashLinesBlock = new ActionBlock<LogLine>(
            logLine =>
            {
                // Add this code snippet to periodically print the InputCount and OutputCount every 500ms.
                //var printTask = Task.Run(async () =>
                //{
                //    while (!cancellationToken.IsCancellationRequested)
                //    {
                //        var linesBlockInputCount = (logLinesProducer.LinesBlock as TransformManyBlock<string, string>)?.InputCount ?? 0;
                //        var linesBlockOutputCount = (logLinesProducer.LinesBlock as TransformManyBlock<string, string>)?.OutputCount ?? 0;
                //        Console.WriteLine($"Periodic Check: InputCount={linesBlockInputCount}, OutputCount={linesBlockOutputCount}");
                //        await Task.Delay(500, cancellationToken);
                //    }
                //});

                // Ensure the printTask is awaited or handled properly in your pipeline's lifecycle.
                var hash = _hasher.ProcessLine(logLine.Line);
                if (hash != null)
                {
                    _globalResults.AddOrUpdate(
                        hash,
                        _ => (logLine.Line, 1),
                        (_, oldVal) => (oldVal.Representative, oldVal.Count + 1)
                    );
                }
            },
            new ExecutionDataflowBlockOptions
            {
                //BoundedCapacity = 1000,
                BoundedCapacity = _options.HashLinesDataflowConfiguration.BoundedCapacity,
                MaxDegreeOfParallelism = _options.HashLinesDataflowConfiguration.Parallelism,
            });


        // 4) Link them: source -> hash -> aggregate
        linesBlock.LinkTo(hashLinesBlock, new DataflowLinkOptions { PropagateCompletion = true });
        //hashLinesBlock.LinkTo(aggregateBlock, new DataflowLinkOptions { PropagateCompletion = true });

        // 5) We define the overall run task
        _runTask = Task.Run(async () =>
        {
            // a) Start enumerating file paths => triggers logLine emission
            await logLinesProducer.PostAllFilePathsAsync(cancellationToken);

            // b) Wait for the reading pipeline to finish
            //await logLinesProducer.Completion.ConfigureAwait(false);
            await hashLinesBlock.Completion.ConfigureAwait(false);
            // d) If requested, save results to file
            if (string.IsNullOrWhiteSpace(_options.ResultsFilePath) is false)
            {
                SaveResultsToFile();
            }

            // e) If user wants to open the file
            if (_options.OpenResultFile)
            {
                OpenResultsFile();
            }
        }, cancellationToken);

        return _runTask;
    }

    /// <summary>
    /// Returns an in-memory data of (logLine, count), sorted descending by count (then by logLine).
    /// Will await the pipeline first if it isn't completed.
    /// </summary>
    public async Task<List<(string line, int count)>> GetOrderedResultsAsync()
    {
        if (_runTask == null)
            throw new InvalidOperationException("You must call BuildAndRunAsync() first.");

        await _runTask.ConfigureAwait(false);
        return ProduceResults();
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
            .OrderByDescending(x => x.Count)
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
        //    JsonSerializer.Serialize(new { Line = x.logLine, Count = x.count })
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
        workbook.SaveAs(_options.ResultsFilePath);

        Console.WriteLine($"Results saved to Excel file: {_options.ResultsFilePath}");
    }

    private void OpenResultsFile()
    {
        if (_options.ResultsFilePath == null)
        {
            Console.WriteLine("No results file available to open.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.GetFullPath(_options.ResultsFilePath),
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unable to open file: {_options.ResultsFilePath}\nError: {ex.Message}");
        }
    }
}
