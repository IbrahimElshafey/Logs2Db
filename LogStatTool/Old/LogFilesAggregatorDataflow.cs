using DocumentFormat.OpenXml.Wordprocessing;
using LogStatTool.Contracts;
using LogStatTool.Helpers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace LogStatTool.Old;

public class LogFilesAggregatorDataflow
{
    private readonly int _bulkReadSize;
    private readonly int _concurrency;
    private readonly Base.ILogLineProcessor<ulong?> _hasher;

    public LogFilesAggregatorDataflow(
        Base.ILogLineProcessor<ulong?> hasher,
        int concurrency,
        int bulkReadSize = 100)
    {
        if (concurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(concurrency), "Concurrency must be > 0");

        _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
        _concurrency = concurrency;
        _bulkReadSize = bulkReadSize > 0
            ? bulkReadSize
            : throw new ArgumentOutOfRangeException(nameof(bulkReadSize));
    }

    public async Task<IDictionary<ulong?, MatchCounts>> AggregateLogFilesAsync(
        GetLogFilesOptions getLogsFileOptions,
        IProgress<float>? progress = null)
    {
        // Thread-safe dictionary for final results
        var globalResults = new ConcurrentDictionary<ulong?, MatchCounts>();

        // ----------------------------------------------------------------
        // 1) Enumerate all matching files for counting only (first pass)
        // ----------------------------------------------------------------
        IEnumerable<string> filePaths = Directory.EnumerateFiles(
            getLogsFileOptions.LogFilesFolder,
            getLogsFileOptions.SearchPattern,
            getLogsFileOptions.EnumerationOptions);

        if (getLogsFileOptions.PathFilter != null)
        {
            filePaths = filePaths.Where(x => x.Contains(getLogsFileOptions.PathFilter, StringComparison.OrdinalIgnoreCase));
        }

        // Count how many files we have in total (no .ToList())
        int totalFilesCount = filePaths.Count();
        if (totalFilesCount == 0)
        {
            Console.WriteLine("No files found to process.");
            return globalResults;
        }

        // We'll track how many file paths we've sent into the pipeline so far
        int filesSentSoFar = 0;

        // ----------------------------------------------------------------
        // BLOCK #1: BufferBlock for file paths
        // ----------------------------------------------------------------
        var filePathsBlock = new BufferBlock<string>(new DataflowBlockOptions
        {
            BoundedCapacity = DataflowBlockOptions.Unbounded
        });

        // ----------------------------------------------------------------
        // BLOCK #2: TransformManyBlock => reads each file, yields lines
        // ----------------------------------------------------------------
        var readFileBlock = new TransformManyBlock<string, string>(
        filePath => ReadFileByChunksAsync(filePath, progress, totalFilesCount),
        new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = 1000,
            MaxDegreeOfParallelism = _concurrency
        });
        // ----------------------------------------------------------------
        // BLOCK #3: TransformBlock => Hash each line
        // ----------------------------------------------------------------
        var hashLinesBlock = new TransformBlock<string, (ulong? Hash, string Line)>(
            line =>
            {
                var hash = _hasher.ProcessLine(ref line);
                return (hash, line);
            },
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = 1000,
                MaxDegreeOfParallelism = _concurrency
            });

        // ----------------------------------------------------------------
        // BLOCK #4: ActionBlock => Aggregate hashed lines
        // ----------------------------------------------------------------
        var aggregateBlock = new ActionBlock<(ulong? Hash, string Line)>(
            hashed =>
            {
                if (hashed.Hash != null)
                {
                    globalResults.AddOrUpdate(
                        hashed.Hash,
                        (hashed.Line, 1),
                        (_, oldVal) => (oldVal.Representative, oldVal.Count + 1));
                }
            },
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = 1000,
                MaxDegreeOfParallelism = _concurrency
            });

        // ---------------------------------------------------------------
        // 3) Link the blocks
        // ---------------------------------------------------------------
        filePathsBlock.LinkTo(readFileBlock, new DataflowLinkOptions { PropagateCompletion = true });
        readFileBlock.LinkTo(hashLinesBlock, new DataflowLinkOptions { PropagateCompletion = true });
        hashLinesBlock.LinkTo(aggregateBlock, new DataflowLinkOptions { PropagateCompletion = true });

        // ---------------------------------------------------------------
        // 4) Producer Task: post file paths into filePathsBlock
        //    *without* building a big list in memory
        //    *report progress for each file path posted
        // ---------------------------------------------------------------
        var producerTask = Task.Run(async () =>
        {
            foreach (var filePath in filePaths)
            {
                // Post file path into pipeline
                await filePathsBlock.SendAsync(filePath).ConfigureAwait(false);
            }
            filePathsBlock.Complete();
        });

        // Wait for producer to finish
        await producerTask.ConfigureAwait(false);
        // Wait for the entire pipeline to finish
        await aggregateBlock.Completion.ConfigureAwait(false);

        return globalResults;
    }

    private async IAsyncEnumerable<string> ReadFileByChunksAsync(
        string filePath,
        IProgress<float>? progress,
        int totalFilesCount,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        using var fs = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        using var reader = new StreamReader(fs);
        var linesBuffer = new List<string>(_bulkReadSize);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            //await reader.ReadAsync()
            //await reader.ReadBlockAsync()
            if (line is null)
                break; // end of file

            linesBuffer.Add(line);

            // Once we hit the bulk size, yield them out
            if (linesBuffer.Count >= _bulkReadSize)
            {
                foreach (var chunkLine in linesBuffer)
                {
                    yield return chunkLine;
                }
                linesBuffer.Clear(); // Reset for next chunk
            }
        }

        // Yield leftover lines if any
        if (linesBuffer.Count > 0)
        {
            foreach (var leftoverLine in linesBuffer)
            {
                yield return leftoverLine;
            }
            linesBuffer.Clear();
        }

        ReportProgress(progress, totalFilesCount);
    }

    private int filesSentSoFar = 0;
    private void ReportProgress(IProgress<float>? progress, int totalFiles)
    {
        int sent = Interlocked.Increment(ref filesSentSoFar);
        var percent = sent / (double)totalFiles * 100.0;
        progress?.Report((float)percent);
    }
}