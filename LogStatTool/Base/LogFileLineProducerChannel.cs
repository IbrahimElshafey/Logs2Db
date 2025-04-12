using LogStatTool.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;

namespace LogStatTool.Base
{

    public class LogFileLineProducerChannel
    {
        private readonly LogFilesOptions _options;
        private readonly ProduceLinesDataflowConfiguration _config;

        // These fields track the total #files vs how many we've finished reading:
        private int _filesProcessedSoFar = 0;
        private int _totalFilesCount = 0;

        // Channels
        private Channel<string>? _filePathsChannel;
        private Channel<string>? _linesChannel;

        // The main consumer tasks that read file paths and produce lines:
        private Task? _readingCompletionTask;

        /// <summary>
        /// Constructor. (Same parameters as before, but no more Dataflow usage internally.)
        /// </summary>
        public LogFileLineProducerChannel(LogFilesOptions options, ProduceLinesDataflowConfiguration? dataflowConfiguration = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _config = dataflowConfiguration ?? new ProduceLinesDataflowConfiguration();
        }

        /// <summary>
        /// Builds the pipeline, creating two channels:
        ///  1) A channel for file paths (written by PostAllFilePathsAsync).
        ///  2) A channel for lines, which is read by your downstream consumer.
        ///
        /// Spawns background tasks (bounded by PathToLinesParallelism) that:
        ///    - read paths from the file-paths channel,
        ///    - read each file line-by-line,
        ///    - write lines to the lines channel.
        ///
        /// You can read log lines from <see cref="Lines" /> and await
        /// <see cref="Completion" /> to know when it’s done.
        /// </summary>
        public ChannelReader<string> Build(IProgress<float>? progress = null, CancellationToken cancellationToken = default)
        {
            // Create a bounded channel for file paths:
            _filePathsChannel = Channel.CreateBounded<string>(_config.PathsBoundedCapacity);

            // Create a bounded channel for lines:
            _linesChannel = Channel.CreateBounded<string>(_config.PathToLinesBoundedCapacity);
            //_linesChannel = Channel.CreateUnbounded<string>();

            // Start N worker tasks, each reading file paths and producing lines:
            var workers = new List<Task>();
            for (int i = 0; i < _config.PathToLinesParallelism; i++)
            {
                workers.Add(Task.Run(() => ReadPathsAndProduceLinesAsync(progress, cancellationToken)));
            }

            // When all workers finish, complete the _linesChannel so readers know no more lines are coming:
            _readingCompletionTask = Task.WhenAll(workers)
                .ContinueWith(_ =>
                {
                    _linesChannel.Writer.TryComplete();
                }, cancellationToken);

            // Return the reader side for consumers to read lines:
            return _linesChannel.Reader;
        }

        /// <summary>
        /// Exposes a Task that completes once all file paths have been processed
        /// and all lines have been written to the lines channel.
        /// </summary>
        public Task Completion => _readingCompletionTask ?? Task.CompletedTask;

        /// <summary>
        /// The reader for lines. You can either call <see cref="Build" /> and capture
        /// its return value, or use this property after building.
        /// </summary>
        public ChannelReader<string>? Lines => _linesChannel?.Reader;

        /// <summary>
        /// Enumerates the log folder and sends each file path into the file-paths channel.
        /// After posting all, completes the channel.
        /// </summary>
        public async Task PostAllFilePathsAsync(CancellationToken cancellationToken = default)
        {
            if (_filePathsChannel == null)
                throw new InvalidOperationException("Call Build() first.");

            // Enumerate the file paths
            IEnumerable<string> filePaths = Directory.EnumerateFiles(
                _options.LogFilesFolder,
                _options.SearchPattern,
                _options.EnumerationOptions);

            // Apply a regex filter if configured
            if (!string.IsNullOrWhiteSpace(_options.RegexPathFilter))
            {
                var pathFilterRegex = new Regex(
                    _options.RegexPathFilter,
                    RegexOptions.Compiled | RegexOptions.IgnoreCase);

                filePaths = filePaths.Where(x => pathFilterRegex.IsMatch(x));
            }

            // Count how many files we have
            var filesArray = filePaths.ToArray();
            _totalFilesCount = filesArray.Length;

            if (_totalFilesCount == 0)
            {
                Console.WriteLine("No files found to process.");
                _filePathsChannel.Writer.Complete();
                return;
            }
            else
            {
                Console.WriteLine($"Start processing [{_totalFilesCount}] files in folder [{_options.LogFilesFolder}]");
            }

            // Send each path to the channel
            foreach (var path in filesArray)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _filePathsChannel.Writer.WriteAsync(path, cancellationToken)
                    .ConfigureAwait(false);
            }

            // We’re done sending paths
            _filePathsChannel.Writer.Complete();
        }

        /// <summary>
        /// Called by each worker task: reads paths from _filePathsChannel,
        /// reads lines from each file, and writes lines to _linesChannel.
        /// </summary>
        private async Task ReadPathsAndProduceLinesAsync(IProgress<float>? progress, CancellationToken cancellationToken)
        {
            if (_filePathsChannel == null || _linesChannel == null)
                return;

            try
            {
                var reader = _filePathsChannel.Reader;
                while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    // Dequeue every available path
                    while (reader.TryRead(out var filePath))
                    {
                        // Read lines from that file
                        await foreach (var line in ReadFileByChunksAsync(filePath, cancellationToken)
                            .ConfigureAwait(false))
                        {
                            await _linesChannel.Writer.WriteAsync(line, cancellationToken)
                                .ConfigureAwait(false);
                        }

                        // File done, update progress
                        var soFar = Interlocked.Increment(ref _filesProcessedSoFar);
                        float percent = soFar / (float)_totalFilesCount * 100f;
                        progress?.Report(percent);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // If canceled, attempt to signal no more data
                _linesChannel.Writer.TryComplete(new TaskCanceledException());
            }
        }

        /// <summary>
        /// Reads a single file in chunks of _config.BulkReadSize bytes,
        /// yielding lines one-by-one via IAsyncEnumerable.
        /// </summary>
        private async IAsyncEnumerable<string> ReadFileByChunksAsync(
            string filePath,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken)
        {
            int bufferSize = _config.BulkReadSize; // Byte buffer size
            byte[] buffer = new byte[bufferSize];
            char[] charBuffer = new char[Encoding.UTF8.GetMaxCharCount(bufferSize)];
            var decoder = Encoding.UTF8.GetDecoder();

            var currentLine = new StringBuilder();

            using var fs = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int bytesRead = await fs.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);
                if (bytesRead == 0)
                    break;

                int charsDecoded = decoder.GetChars(buffer, 0, bytesRead, charBuffer, 0, flush: false);

                for (int i = 0; i < charsDecoded; i++)
                {
                    char c = charBuffer[i];

                    // If newline, yield the line built so far
                    if (c == '\n')
                    {
                        yield return currentLine.ToString();
                        currentLine.Clear();
                    }
                    else if (c != '\r')
                    {
                        // Accumulate normal character (skip CR)
                        currentLine.Append(c);
                    }
                }
            }

            // Yield any trailing line if present
            if (currentLine.Length > 0)
            {
                yield return currentLine.ToString();
                currentLine.Clear();
            }
        }
    }
}
