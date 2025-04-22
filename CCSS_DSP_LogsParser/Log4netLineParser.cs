using LogsProcessingCore.Contracts;
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CCSS_DSP_LogsParser
{
    public static class Log4netLineParser
    {
        // cache: filePath -> detected parts count
        private static readonly ConcurrentDictionary<string, int> _fileFormatCache
            = new(StringComparer.OrdinalIgnoreCase);

        // ensure warning printed only once per file
        private static readonly ConcurrentDictionary<string, bool> _warningPrinted
            = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Attempts to parse one log‐line into a ParsedLogLine.
        /// Returns null if the format isn't recognized or parsing fails.
        /// Caches the parts count per filePath to skip re-detecting the format.
        /// </summary>
        public static ParsedLogLine Parse(LogLine logLine)
        {
            var span = logLine.Line.AsSpan();
            string fileKey = logLine.FilePath;

            // detect and cache parts count for this file without capturing span in a lambda
            if (!_fileFormatCache.TryGetValue(fileKey, out int formatParts))
            {
                Span<int> tmp = stackalloc int[9];
                int cnt = 0;
                for (int i = 0; i < span.Length && cnt < tmp.Length; i++)
                    if (span[i] == '|') tmp[cnt++] = i;
                formatParts = cnt + 1;
                _fileFormatCache[fileKey] = formatParts;
            }

            // only these part-counts are supported
            var allowed = new[] { 3, 7, 8, 9, 10 };
            if (!allowed.Contains(formatParts))
            {
                // warn only once per file
                if (_warningPrinted.TryAdd(fileKey, true))
                    Console.WriteLine($"Unrecognized format for file [{fileKey}]");
                return null;
            }

            // find separators for slicing
            Span<int> pos = stackalloc int[9];
            int count = 0;
            for (int i = 0; i < span.Length && count < pos.Length; i++)
                if (span[i] == '|') pos[count++] = i;

            try
            {
                static ReadOnlySpan<char> SliceTrim(ReadOnlySpan<char> src, int start, int length)
                    => src.Slice(start, length).Trim();

                switch (formatParts)
                {
                    case 3:
                        {
                            var p0 = SliceTrim(span, 0, pos[0]);
                            var p1 = SliceTrim(span, pos[0] + 1, pos[1] - pos[0] - 1);
                            var p2 = SliceTrim(span, pos[1] + 1, span.Length - pos[1] - 1);
                            return new ParsedLogLine
                            {
                                Timestamp = ParseDate(p0),
                                Level = p1.ToString(),
                                Message = p2.ToString()
                            };
                        }

                    case 7:
                        {
                            var p0 = SliceTrim(span, 0, pos[0]);
                            var p1 = SliceTrim(span, pos[0] + 1, pos[1] - pos[0] - 1);
                            var p2 = SliceTrim(span, pos[1] + 1, pos[2] - pos[1] - 1);
                            var p3 = SliceTrim(span, pos[2] + 1, pos[3] - pos[2] - 1);
                            var p4 = SliceTrim(span, pos[3] + 1, pos[4] - pos[3] - 1);
                            var p5 = SliceTrim(span, pos[4] + 1, pos[5] - pos[4] - 1);
                            var p6 = SliceTrim(span, pos[5] + 1, span.Length - pos[5] - 1);

                            if (int.TryParse(p2, out var lnB))
                            {
                                return new ParsedLogLine
                                {
                                    Thread = p0.ToString(),
                                    Logger = p1.ToString(),
                                    CodeLineNumber = lnB,
                                    Ndc = p3.ToString(),
                                    Timestamp = ParseDate(p4),
                                    Level = p5.ToString(),
                                    Message = p6.ToString()
                                };
                            }
                            return new ParsedLogLine
                            {
                                Thread = p0.ToString(),
                                Logger = p1.ToString(),
                                MethodName = p2.ToString(),
                                CodeLineNumber = int.Parse(p3),
                                Timestamp = ParseDate(p4),
                                Level = p5.ToString(),
                                Message = p6.ToString()
                            };
                        }

                    case 8:
                        {
                            var p0 = SliceTrim(span, 0, pos[0]);
                            var p1 = SliceTrim(span, pos[0] + 1, pos[1] - pos[0] - 1);
                            var p2 = SliceTrim(span, pos[1] + 1, pos[2] - pos[1] - 1);
                            var p3 = SliceTrim(span, pos[2] + 1, pos[3] - pos[2] - 1);
                            var p4 = SliceTrim(span, pos[3] + 1, pos[4] - pos[3] - 1);
                            var p5 = SliceTrim(span, pos[4] + 1, pos[5] - pos[4] - 1);
                            var p6 = SliceTrim(span, pos[5] + 1, pos[6] - pos[5] - 1);
                            var p7 = SliceTrim(span, pos[6] + 1, span.Length - pos[6] - 1);

                            if (Version.TryParse(p0, out _))
                            {
                                return new ParsedLogLine
                                {
                                    AssemblyVersion = p0.ToString(),
                                    Thread = p1.ToString(),
                                    Logger = p2.ToString(),
                                    CodeLineNumber = int.Parse(p3),
                                    Ndc = p4.ToString(),
                                    Timestamp = ParseDate(p5),
                                    Level = p6.ToString(),
                                    Message = p7.ToString()
                                };
                            }
                            return new ParsedLogLine
                            {
                                Thread = p0.ToString(),
                                Logger = p1.ToString(),
                                MethodName = p2.ToString(),
                                CodeLineNumber = int.Parse(p3),
                                Ndc = p4.ToString(),
                                Timestamp = ParseDate(p5),
                                Level = p6.ToString(),
                                Message = p7.ToString()
                            };
                        }

                    case 9:
                        {
                            var p0 = SliceTrim(span, 0, pos[0]);
                            var p1 = SliceTrim(span, pos[0] + 1, pos[1] - pos[0] - 1);
                            var p2 = SliceTrim(span, pos[1] + 1, pos[2] - pos[1] - 1);
                            var p3 = SliceTrim(span, pos[2] + 1, pos[3] - pos[2] - 1);
                            var p4 = SliceTrim(span, pos[3] + 1, pos[4] - pos[3] - 1);
                            var p5 = SliceTrim(span, pos[4] + 1, pos[5] - pos[4] - 1);
                            var p6 = SliceTrim(span, pos[5] + 1, pos[6] - pos[5] - 1);
                            var p7 = SliceTrim(span, pos[6] + 1, pos[7] - pos[6] - 1);
                            var p8 = SliceTrim(span, pos[7] + 1, span.Length - pos[7] - 1);

                            return new ParsedLogLine
                            {
                                Thread = p0.ToString(),
                                Logger = p1.ToString(),
                                MethodName = p2.ToString(),
                                CodeLineNumber = int.Parse(p3),
                                Timestamp = ParseDate(p4),
                                Level = p5.ToString(),
                                Operator = p6.ToString(),
                                MachineName = p7.ToString(),
                                Message = p8.ToString()
                            };
                        }

                    case 10:
                        {
                            var p0 = SliceTrim(span, 0, pos[0]);
                            var p1 = SliceTrim(span, pos[0] + 1, pos[1] - pos[0] - 1);
                            var p2 = SliceTrim(span, pos[1] + 1, pos[2] - pos[1] - 1);
                            var p3 = SliceTrim(span, pos[2] + 1, pos[3] - pos[2] - 1);
                            var p4 = SliceTrim(span, pos[3] + 1, pos[4] - pos[3] - 1);
                            var p5 = SliceTrim(span, pos[4] + 1, pos[5] - pos[4] - 1);
                            var p6 = SliceTrim(span, pos[5] + 1, pos[6] - pos[5] - 1);
                            var p7 = SliceTrim(span, pos[6] + 1, pos[7] - pos[6] - 1);
                            var p8 = SliceTrim(span, pos[7] + 1, pos[8] - pos[7] - 1);
                            var p9 = SliceTrim(span, pos[8] + 1, span.Length - pos[8] - 1);

                            return new ParsedLogLine
                            {
                                Thread = p0.ToString(),
                                Logger = p1.ToString(),
                                MethodName = p2.ToString(),
                                CodeLineNumber = int.Parse(p3),
                                Timestamp = ParseDate(p4),
                                Level = p5.ToString(),
                                Operator = p6.ToString(),
                                MachineName = p7.ToString(),
                                HostName = p8.ToString(),
                                Message = p9.ToString()
                            };
                        }

                    default:
                        return null;
                }
            }
            catch
            {
                return null;
            }
        }

        private static DateTime ParseDate(ReadOnlySpan<char> text)
        {
            if (DateTime.TryParseExact(
                text,
                new[] { "yyyy-MM-dd HH:mm:ss,fff", "HH:mm:ss,fff", "yyyy/MM/dd HH:mm:ss" },
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dt))
            {
                return dt;
            }
            return DateTime.Parse(text.ToString(), CultureInfo.InvariantCulture);
        }
    }
}
