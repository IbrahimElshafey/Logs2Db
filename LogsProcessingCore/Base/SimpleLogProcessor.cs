using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using LogsProcessingCore.Contracts;

namespace LogsProcessingCore.Base
{
    /// <summary>
    /// Reads a log file asynchronously, processes each line into <typeparamref name="T"/>,
    /// and streams results through a bounded channel for concurrent consumption.
    /// Uses buffer pooling and span-based parsing to minimize allocations and maximize throughput.
    /// </summary>
    /// <typeparam name="T">The type produced for each log line.</typeparam>
    public class FileChannelReader<T>
    {
        private readonly string _filePath;
        private readonly BoundedChannelOptions _channelOptions;
        private readonly int _bufferSize;

        /// <summary>
        /// Initializes a new instance of <see cref="FileChannelReader{T}"/>.
        /// </summary>
        /// <param name="filePath">Path to the log file to read.</param>
        /// <param name="channelCapacity">Max items buffered in the channel.</param>
        /// <param name="bufferSize">Byte buffer size for FileStream reads.</param>
        public FileChannelReader(string filePath, int channelCapacity = 1000, int bufferSize = 64 * 1024)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _bufferSize = bufferSize;
            _channelOptions = new BoundedChannelOptions(channelCapacity)
            {
                SingleWriter = true,
                SingleReader = false,
                FullMode = BoundedChannelFullMode.Wait
            };
        }

        /// <summary>
        /// Starts async processing: reads file in pooled byte buffers, decodes into char buffers,
        /// splits lines via ReadOnlySpan&lt;char&gt;, wraps each in LogLineSpan, and writes to channel.
        /// </summary>
        public async Task<ChannelReader<T>> ProcessLogFileAsync(Func<LogLineSpan, T> processingFunction)
        {
            if (processingFunction == null) throw new ArgumentNullException(nameof(processingFunction));

            var channel = Channel.CreateBounded<T>(_channelOptions);
            var writer = channel.Writer;

            _ = Task.Run(async () =>
            {
                var bytePool = ArrayPool<byte>.Shared;
                var charPool = ArrayPool<char>.Shared;
                byte[] byteBuffer = bytePool.Rent(_bufferSize);
                var decoder = Encoding.UTF8.GetDecoder();
                char[] leftoverBuffer = Array.Empty<char>();
                int leftoverCount = 0;
                int lineIndex = 0;

                try
                {
                    await using var fs = new FileStream(
                        _filePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        _bufferSize,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);

                    int bytesRead;
                    while ((bytesRead = await fs.ReadAsync(byteBuffer, 0, _bufferSize)) > 0)
                    {
                        int neededChars = decoder.GetCharCount(byteBuffer, 0, bytesRead);
                        char[] charBuffer = charPool.Rent(neededChars);
                        int charCount = decoder.GetChars(byteBuffer, 0, bytesRead, charBuffer, 0);

                        // Combine leftover and new chars into a single span
                        ReadOnlyMemory<char> combined;
                        if (leftoverCount > 0)
                        {
                            char[] temp = charPool.Rent(leftoverCount + charCount);
                            Array.Copy(leftoverBuffer, 0, temp, 0, leftoverCount);
                            Array.Copy(charBuffer, 0, temp, leftoverCount, charCount);
                            combined = new ReadOnlyMemory<char>(temp, 0, leftoverCount + charCount);
                            charPool.Return(temp);
                        }
                        else
                        {
                            combined = new ReadOnlyMemory<char>(charBuffer, 0, charCount);
                        }

                        // Scan for lines
                        int start = 0;
                        for (int i = 0; i < combined.Length; i++)
                        {
                            if (combined.Span[i] == '\n')
                            {
                                int length = i - start;
                                if (length > 0 && combined.Span[start + length - 1] == '\r') length--;

                                var lineSpan = combined.Slice(start, length);
                                var logSpan = new LogLineSpan(lineSpan.ToArray().AsMemory(), lineIndex++, _filePath);
                                var item = processingFunction(logSpan);
                                await writer.WriteAsync(item);

                                start = i + 1;
                            }
                        }

                        // Save leftover
                        leftoverCount = combined.Length - start;
                        if (leftoverCount > 0)
                        {
                            leftoverBuffer = charPool.Rent(leftoverCount);
                            combined.Slice(start, leftoverCount).CopyTo(leftoverBuffer);
                        }

                        charPool.Return(charBuffer);
                        decoder.Reset();
                    }

                    // Final leftover line
                    if (leftoverCount > 0)
                    {
                        var finalSpan = new ReadOnlySpan<char>(leftoverBuffer, 0, leftoverCount);
                        var logSpan = new LogLineSpan(finalSpan.ToArray().AsMemory(), lineIndex++, _filePath);
                        var item = processingFunction(logSpan);
                        await writer.WriteAsync(item);
                    }

                    writer.Complete();
                }
                catch (Exception ex)
                {
                    writer.Complete(ex);
                }
                finally
                {
                    decoder.Reset();
                    bytePool.Return(byteBuffer);
                    if (leftoverBuffer.Length > 0) charPool.Return(leftoverBuffer);
                }
            });

            await Task.Yield();
            return channel.Reader;
        }
    }
}