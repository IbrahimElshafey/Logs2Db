using LogStatTool.Contracts;
using LogStatTool.Helpers;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace LogStatTool.Old;

public class LogFilesAggregator
{
    private readonly int _bulkReadSize;
    private readonly int _concurrency;
    private readonly Base.ILogLineProcessor<byte[]?> _hasher;
    private readonly int? _maxQueueSize;

    /// <summary>
    /// Creates a LogFilesAggregator for parallel log processing.
    /// </summary>
    /// <param name="hasher">The hasher used for normalizing and hashing lines.</param>
    /// <param name="concurrency">Number of parallel consumer tasks.</param>
    /// <param name="maxQueueSize">Optional maximum channel size for lines (for memory control); null => unbounded.</param>
    /// <param name="bulkReadSize">Number of lines to read in one chunk before pushing to the channel (e.g., 100 or 200).</param>
    public LogFilesAggregator(Base.ILogLineProcessor<byte[]?> hasher, int concurrency, int? maxQueueSize = null, int bulkReadSize = 100)
    {
        if (concurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(concurrency), "Concurrency must be > 0");

        _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
        _concurrency = concurrency;
        _maxQueueSize = maxQueueSize;
        _bulkReadSize = bulkReadSize > 0 ? bulkReadSize : throw new ArgumentOutOfRangeException(nameof(bulkReadSize));
    }

    private static Channel<T> CreateChannel<T>(ChannelOptions options)
    {
        return options switch
        {
            BoundedChannelOptions bounded => Channel.CreateBounded<T>(bounded),
            UnboundedChannelOptions unbounded => Channel.CreateUnbounded<T>(unbounded),
            _ => throw new ArgumentException("Invalid channel options type")
        };
    }

    /// <summary>
    /// Reads all files in the given folder (and subfolders), in parallel using Channels.
    /// Lines from each file are read in bulk (chunks of _bulkReadSize).
    /// Once a file is fully processed, progress?.Report(1) is called.
    /// 
    /// Aggregates results in a ConcurrentDictionary:
    ///   hashKey -> (representativeLine, count).
    /// </summary>
    /// <param name="folderPath">Root folder containing logs.</param>
    /// <param name="progress">Optional progress reporter: we call .Report(1) per file.</param>
    public async Task<IDictionary<byte[], MatchCounts>>
        AggregateLogFilesAsync(string[] logFiles, IProgress<int>? progress = null)
    {
        // 1) Create a bounded or unbounded channel based on maxQueueSize
        ChannelOptions channelOptions = _maxQueueSize.HasValue
            ? new BoundedChannelOptions(_maxQueueSize.Value) { SingleWriter = true, SingleReader = false }
            : new UnboundedChannelOptions { SingleWriter = true, SingleReader = false };
        var linesChannel = CreateChannel<string>(channelOptions);

        // 2) Thread-safe dictionary for final results
        var globalResults = new ConcurrentDictionary<byte[], MatchCounts>(new ByteArrayComparer());

        // 3) Producer task: enumerates files, reads lines in bulk, writes them to channel
        var producerTask = Task.Run(async () =>
        {
            try
            {
                foreach (var file in logFiles)
                {
                    int linesInCurrentFile = 0;  // optional tracking if needed

                    using var fs = new FileStream(file, new FileStreamOptions
                    {
                        Access = FileAccess.Read,
                        Mode = FileMode.Open,
                        Options = FileOptions.SequentialScan,
                        BufferSize = 81920 // tweak for performance
                    });

                    using var streamReader = new StreamReader(fs);
                    var bulkLines = new List<string>(_bulkReadSize);

                    while (true)
                    {
                        bulkLines.Clear();
                        // Read up to _bulkReadSize lines
                        for (int i = 0; i < _bulkReadSize; i++)
                        {
                            var line = streamReader.ReadLine();
                            if (line == null) break;

                            bulkLines.Add(line);
                        }

                        // If no more lines, break out of loop
                        if (bulkLines.Count == 0)
                            break;

                        linesInCurrentFile += bulkLines.Count;

                        // Push these lines to the channel
                        foreach (var l in bulkLines)
                        {
                            await linesChannel.Writer.WriteAsync(l).ConfigureAwait(false);
                        }
                    }

                    // Done with this file -> progress?.Report(1)
                    progress?.Report(1);
                }
            }
            finally
            {
                // Signal that no more lines are coming
                linesChannel.Writer.Complete();
            }
        });

        // 4) Consumer tasks: read lines from the channel, compute hashes, store in dictionary
        var consumerTasks = new Task[_concurrency];
        for (int i = 0; i < _concurrency; i++)
        {
            consumerTasks[i] = Task.Run(async () =>
            {
                while (await linesChannel.Reader.WaitToReadAsync().ConfigureAwait(false))
                {
                    while (linesChannel.Reader.TryRead(out var line))
                    {
                        var hashBytes = _hasher.ProcessLine(ref line);
                        if (hashBytes == null)
                            continue;


                        // Merge results
                        globalResults.AddOrUpdate(
                            hashBytes,
                            (line, 1),
                            (_, oldVal) => (oldVal.Representative, oldVal.Count + 1));
                    }
                }
            });
        }

        // 5) Wait for producer + all consumers
        await Task.WhenAll(producerTask, Task.WhenAll(consumerTasks)).ConfigureAwait(false);

        // Return final dictionary
        return globalResults;
    }
}
