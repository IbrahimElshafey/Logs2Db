using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace LogStatTool.Base;

public class LogFileLineProducer
{
    private readonly GetLogFilesOptions _options;
    private readonly int _concurrency;
    private readonly int _bulkReadSize;

    /// <summary>
    /// Creates a pipeline that:
    /// 1) Enumerates files in <see cref="_options"/>.
    /// 2) Reads lines from these files in parallel.
    /// 3) Exposes an ISourceBlock&lt;string&gt; that emits all lines.
    /// </summary>
    /// <param name="options">Specifies folder, search pattern, etc.</param>
    /// <param name="concurrency">Max number of files processed in parallel</param>
    /// <param name="bulkReadSize">Number of lines read in a chunk before yielding.</param>
    public LogFileLineProducer(
        GetLogFilesOptions options,
        int concurrency = 4,
        int bulkReadSize = 100)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (concurrency <= 0) throw new ArgumentOutOfRangeException(nameof(concurrency));
        if (bulkReadSize <= 0) throw new ArgumentOutOfRangeException(nameof(bulkReadSize));

        _concurrency = concurrency;
        _bulkReadSize = bulkReadSize;
    }

    /// <summary>
    /// Builds the Dataflow pipeline and returns an ISourceBlock&lt;string&gt; 
    /// which emits lines from the log files.
    /// 
    /// Make sure to await <see cref="Completion"/> once you've linked downstream blocks
    /// and posted all files.
    /// </summary>
    public ISourceBlock<string> Build(
        IProgress<float>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // 1) Create the blocks.

        // A. BufferBlock => file paths
        var filePathsBlock = new BufferBlock<string>(new DataflowBlockOptions
        {
            BoundedCapacity = 10,
        });

        // B. TransformManyBlock => read lines from each file in parallel
        var readFileBlock = new TransformManyBlock<string, string>(
            filePath => ReadFileByChunksAsync(filePath, progress, cancellationToken),
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = _concurrency,
                //BoundedCapacity = _bulkReadSize / _concurrency,
                BoundedCapacity = 5,
                //MaxMessagesPerTask = 2,
            });

        // 2) Link them
        filePathsBlock.LinkTo(
            readFileBlock,
            new DataflowLinkOptions { PropagateCompletion = true });

        // 3) Return readFileBlock as the “source” block 
        // that downstream can link from:
        SourceBlock = readFileBlock;
        FilePathsBlock = filePathsBlock;

        return readFileBlock;
    }

    /// <summary>
    /// Once you have built the pipeline, this is the block 
    /// you can use to post file paths in a streaming manner.
    /// Typically, you'll do that from an async producer task.
    /// </summary>
    public ITargetBlock<string>? FilePathsBlock { get; private set; }

    /// <summary>
    /// The “root” source block that emits strings (log lines).
    /// </summary>
    public ISourceBlock<string>? SourceBlock { get; private set; }

    /// <summary>
    /// We expose a Task you can await to know when the reading pipeline completes.
    /// Usually, you'll call <see cref="FilePathsBlock.Complete()"/> after 
    /// posting all paths, and then await this property.
    /// </summary>
    public Task Completion => SourceBlock?.Completion ?? Task.CompletedTask;

    /// <summary>
    /// Actually enumerates files from the folder/pattern,
    /// and posts them into the pipeline (FilePathsBlock).
    /// 
    /// This is where you do your "producer" role:
    /// enumerating the file paths and sending them 
    /// into the pipeline for reading.
    /// </summary>
    public async Task PostAllFilePathsAsync(CancellationToken cancellationToken = default)
    {
        if (FilePathsBlock == null)
            throw new InvalidOperationException("Call Build() first.");

        // Enumerate the file paths
        IEnumerable<string> filePaths = Directory.EnumerateFiles(
            _options.LogFilesFolder,
            _options.SearchPattern,
            _options.EnumerationOptions);

        if (_options.PathFilter != null)
        {
            filePaths = filePaths.Where(x => x.Contains(_options.PathFilter, StringComparison.OrdinalIgnoreCase));
        }

        var filesCount = filePaths.Count();
        if (filesCount == 0)
        {
            Console.WriteLine("No files found to process.");
            FilePathsBlock.Complete();
            return;
        }
        else
        {
            _totalFilesCount = filesCount;
            Console.WriteLine($"Start processing [{filesCount}] file");
        }

        // Post each file path
        foreach (var path in filePaths)
        {
            //Console.WriteLine(path);
            cancellationToken.ThrowIfCancellationRequested();
            await FilePathsBlock.SendAsync(path, cancellationToken)
                                .ConfigureAwait(false);
        }

        // Indicate we're done posting
        FilePathsBlock.Complete();
    }

    // For calculating % progress
    private int _filesProcessedSoFar = 0;
    private int _totalFilesCount = 0;

    /// <summary>
    /// This method reads a single file in chunks, yielding lines.
    /// Called by TransformManyBlock for each file path.
    /// </summary>
    private async IAsyncEnumerable<string> ReadFileByChunksAsync(
        string filePath,
        IProgress<float>? progress,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
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
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line is null) break;

            linesBuffer.Add(line);

            // Yield whenever we fill a chunk:
            if (linesBuffer.Count >= _bulkReadSize)
            {
                foreach (var chunkLine in linesBuffer)
                {
                    yield return chunkLine;
                }
                linesBuffer.Clear();
            }
        }

        // leftover lines
        if (linesBuffer.Count > 0)
        {
            foreach (var leftover in linesBuffer)
            {
                yield return leftover;
            }
            linesBuffer.Clear();
        }

      

        var soFar = Interlocked.Increment(ref _filesProcessedSoFar);
        float percent = soFar / (float)_totalFilesCount * 100f;
        progress?.Report(percent);
    }
}
