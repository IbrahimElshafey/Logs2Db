using LogStatTool.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace LogStatTool.Base;

public class LogFileLineProducer
{
    private readonly LogFilesOptions _options;
    private readonly ProduceLinesDataflowConfiguration _dataflowConfiguration;

    /// <summary>
    /// Creates a pipeline that: 1) Enumerates files in <see cref="_options"/>. 2) Reads lines from these files in
    /// parallel. 3) Exposes an ISourceBlock&lt;string&gt; that emits all lines.
    /// </summary>
    /// <param name="options">Specifies folder, search pattern, etc.</param>
    /// <param name="concurrency">Max number of files processed in parallel</param>
    /// <param name="bulkReadSize">Number of lines read in a chunk before yielding.</param>
    public LogFileLineProducer(LogFilesOptions options, ProduceLinesDataflowConfiguration dataflowConfiguration = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if(dataflowConfiguration == null)
            dataflowConfiguration = new();

        _dataflowConfiguration = dataflowConfiguration;
    }

    /// <summary>
    /// Builds the Dataflow pipeline and returns an ISourceBlock&lt;string&gt;  which emits lines from the log files. 
    /// Make sure to await <see cref="Completion"/> once you've linked downstream blocks and posted all files.
    /// </summary>
    public ISourceBlock<string> Build(IProgress<float>? progress = null, CancellationToken cancellationToken = default)
    {
        // 1) Create the blocks.

        // A. BufferBlock => file paths
        var filePathsBlock = new BufferBlock<string>(
            new DataflowBlockOptions { BoundedCapacity = _dataflowConfiguration.PathsBoundedCapacity, });

        // B. TransformManyBlock => read lines from each file in parallel
        var readFileBlock = new TransformManyBlock<string, string>(
            filePath => ReadFileByChunksAsync(filePath, progress, cancellationToken),
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = _dataflowConfiguration.PathToLinesParallelism,
                BoundedCapacity = _dataflowConfiguration.PathToLinesBoundedCapacity,
            });

        // 2) Link them
        filePathsBlock.LinkTo(readFileBlock, new DataflowLinkOptions { PropagateCompletion = true });

        // 3) Return readFileBlock as the “source” block 
        // that downstream can link from:
        LinesBlock = readFileBlock;
        FilePathsBlock = filePathsBlock;

        return readFileBlock;
    }

    /// <summary>
    /// Once you have built the pipeline, this is the block  you can use to post file paths in a streaming manner.
    /// Typically, you'll do that from an async producer task.
    /// </summary>
    private ITargetBlock<string>? FilePathsBlock { get; set; }

    /// <summary>
    /// The “root” source block that emits strings (log lines).
    /// </summary>
    private ISourceBlock<string>? LinesBlock { get; set; }

    /// <summary>
    /// We expose a Task you can await to know when the reading pipeline completes. Usually, you'll call <see
    /// cref="FilePathsBlock.Complete()"/> after  posting all paths, and then await this property.
    /// </summary>
    public Task Completion => LinesBlock?.Completion ?? Task.CompletedTask;

    /// <summary>
    /// Actually enumerates files from the folder/pattern, and posts them into the pipeline (FilePathsBlock).  This is
    /// where you do your "producer" role: enumerating the file paths and sending them  into the pipeline for reading.
    /// </summary>
    public async Task PostAllFilePathsAsync(CancellationToken cancellationToken = default)
    {
        if(FilePathsBlock == null)
            throw new InvalidOperationException("Call Build() first.");

        // Enumerate the file paths
        IEnumerable<string> filePaths = Directory.EnumerateFiles(
            _options.LogFilesFolder,
            _options.SearchPattern,
            _options.EnumerationOptions);

        if(string.IsNullOrWhiteSpace(_options.RegexPathFilter))
        {
            var pathFilterRegex = new Regex(
                _options.RegexPathFilter ?? string.Empty,
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
            filePaths = filePaths.Where(x => pathFilterRegex.IsMatch(x));
        }

        var filesCount = filePaths.Count();
        if(filesCount == 0)
        {
            Console.WriteLine("No files found to process.");
            FilePathsBlock.Complete();
            return;
        } else
        {
            _totalFilesCount = filesCount;
            Console.WriteLine($"Start processing [{filesCount}] files in folder [{_options.LogFilesFolder}]");
        }

        // Post each file path
        foreach(var path in filePaths)
        {
            //Console.WriteLine(path);
            cancellationToken.ThrowIfCancellationRequested();
            await FilePathsBlock.SendAsync(path, cancellationToken).ConfigureAwait(false);
        }

        // Indicate we're done posting
        FilePathsBlock.Complete();
    }

    // For calculating % progress
    private int _filesProcessedSoFar = 0;
    private int _totalFilesCount = 0;

    /// <summary>
    /// This method reads a single file in chunks, yielding lines. Called by TransformManyBlock for each file path.
    /// </summary>
    private async IAsyncEnumerable<string> ReadFileByChunksAsync(string filePath, IProgress<float>? progress, [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        int bufferSize = _dataflowConfiguration.BulkReadSize; // Used as byte buffer size
        byte[] buffer = new byte[bufferSize];
        char[] charBuffer = new char[Encoding.UTF8.GetMaxCharCount(bufferSize)];
        var decoder = Encoding.UTF8.GetDecoder();

        var currentLine = new StringBuilder();
        int bytesRead;

        using var fs = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            bytesRead = await fs.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if(bytesRead == 0)
                break;

            int charsDecoded = decoder.GetChars(buffer, 0, bytesRead, charBuffer, 0, flush: false);

            for(int i = 0; i < charsDecoded; i++)
            {
                char c = charBuffer[i];

                // If newline, yield the line built so far
                if(c == '\n')
                {
                    yield return currentLine.ToString();
                    currentLine.Clear();
                }
 // Skip carriage return, or else accumulate the char
 else if(c != '\r')
                {
                    currentLine.Append(c);
                }
            }
        } while (bytesRead > 0);

        if(currentLine.Length > 0)
        {
            yield return currentLine.ToString();
            currentLine.Clear();
        }

        var soFar = Interlocked.Increment(ref _filesProcessedSoFar);
        float percent = soFar / (float)_totalFilesCount * 100f;
        progress?.Report(percent);
    }
}