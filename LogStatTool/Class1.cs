using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace LogStatTool;

public class LogFilesAggregatorDataflow
{
    private readonly int _bulkReadSize;
    private readonly int _concurrency;
    private readonly ILogLineProcessor<byte[]?> _hasher;

    public LogFilesAggregatorDataflow(ILogLineProcessor<byte[]?> hasher, int concurrency, int bulkReadSize = 100)
    {
        if (concurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(concurrency), "Concurrency must be > 0");

        _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
        _concurrency = concurrency;
        _bulkReadSize = bulkReadSize > 0 ? bulkReadSize : throw new ArgumentOutOfRangeException(nameof(bulkReadSize));
    }

    public async Task<IDictionary<byte[], MatchCounts>> AggregateLogFilesAsync(string[] logFiles, IProgress<int>? progress = null)
    {
        // Final thread-safe dictionary for results.
        var globalResults = new ConcurrentDictionary<byte[], MatchCounts>(new ByteArrayComparer());

        // Block to buffer lines read from files.
        var readLinesBufferBlock = new BufferBlock<string>(new DataflowBlockOptions
        {
            BoundedCapacity = DataflowBlockOptions.Unbounded
        });

        // Block to process each line and compute its hash.
        var lineHashBlock = new TransformBlock<string, (byte[]? Hash, string Line)>(line =>
        {
            var hash = _hasher.ProcessLine(line);
            return (hash, line);
        },
        new ExecutionDataflowBlockOptions
        {
            MaxDegreeOfParallelism = _concurrency
        });

        // Block to aggregate the processed results into the global dictionary.
        var aggregateHashesBlock = new ActionBlock<(byte[]? Hash, string Line)>(result =>
        {
            if (result.Hash != null)
            {
                globalResults.AddOrUpdate(
                    result.Hash,
                    (result.Line, 1),
                    (_, oldVal) => (oldVal.Representative, oldVal.Count + 1));
            }
        },
        new ExecutionDataflowBlockOptions
        {
            MaxDegreeOfParallelism = _concurrency
        });

        // Link the blocks to form a pipeline.
        readLinesBufferBlock.LinkTo(lineHashBlock, new DataflowLinkOptions { PropagateCompletion = true });
        lineHashBlock.LinkTo(aggregateHashesBlock, new DataflowLinkOptions { PropagateCompletion = true });

        // Producer: Read files and post lines to the pipeline.
        foreach (var file in logFiles)
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
            using var reader = new StreamReader(fs);
            var bulkLines = new List<string>(_bulkReadSize);
            string? line;
            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                bulkLines.Add(line);
                if (bulkLines.Count >= _bulkReadSize)
                {
                    foreach (var bulkLine in bulkLines)
                    {
                        await readLinesBufferBlock.SendAsync(bulkLine).ConfigureAwait(false);
                    }
                    bulkLines.Clear();
                }
            }
            // Post any remaining lines.
            if (bulkLines.Count > 0)
            {
                foreach (var bulkLine in bulkLines)
                {
                    await readLinesBufferBlock.SendAsync(bulkLine).ConfigureAwait(false);
                }
            }
            progress?.Report(1);
        }

        // Signal completion to the pipeline.
        readLinesBufferBlock.Complete();

        // Wait for the entire pipeline to finish processing.
        await aggregateHashesBlock.Completion.ConfigureAwait(false);

        return globalResults;
    }
}
