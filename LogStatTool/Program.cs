// See https://aka.ms/new-console-template for more information
using LogStatTool;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

internal class Program
{
    public static async Task Main(string[] args)
    {
        //await FindTopRepeatedLines();
        await FindTopRepeatedLinesRefactored();
    }

    private static async Task FindTopRepeatedLinesRefactored()
    {
        var logOptions = new GetLogFilesOptions
        {
            LogFilesFolder = @"V:\DSP-Logs\63 Logs",
            SearchPattern = "*.*",
            EnumerationOptions = new EnumerationOptions
            {
                RecurseSubdirectories = true
            },
            //Filter = f => f.Contains("\\dis\\", StringComparison.OrdinalIgnoreCase)
        };

        // Create a line hasher
        ILogLineProcessor<byte[]?> hasher = new SimpleLineHasher(
            new LineOptimizationOptions
            {
                MaxLineLength = 20000,
                CheckPrefixFilterLength = 135,
                PrefixFilter = "ERROR|WARN",
                ReplacmentPatterns =
                {
                {"O=.+,OU=.+,CN=.+,E=.+\\.com","<Certificate>"},
                {@"\s+\d+\s+\|.+\d+\s+\|",""}
                }
            }
        );

        // Create the aggregator pipeline. We'll auto-save results to file, 
        // and open that file once complete.
        var aggregator = new HashAggregatorPipeline(
            logFilesOptions: logOptions,
            hasher: hasher,
            concurrency: 4,
            bulkReadSize: 200,
            resultsFilePath: $"results-{Guid.NewGuid()}.txt",
            openResultFile: true
        );

        // Optionally track reading progress
        var progress = new Progress<float>(p =>
            Console.WriteLine($"Reading progress: {p:F2}%")
        );

        // Build and run pipeline
        await aggregator.BuildAndRunAsync(progress);

        // If we want an in-memory sorted list:
        var sortedResults = await aggregator.GetOrderedResultsAsync();
        Console.WriteLine($"Top 10 lines:");
        foreach (var (line, count) in sortedResults.Take(10))
        {
            Console.WriteLine($"  {count,6}  | {line}");
        }

        // Or if we want to feed results into another Dataflow pipeline:
        //   aggregator.GetResultsBlock() -> link to further transforms
        // For example:
        /*
        var resultsBlock = aggregator.GetResultsBlock();
        var nextBlock = new ActionBlock<(string line, int count)>(tuple =>
        {
            // e.g. do Jaccard similarity or other grouping
        });
        resultsBlock.LinkTo(nextBlock, new DataflowLinkOptions { PropagateCompletion = true });
        // Wait for nextBlock to complete
        await nextBlock.Completion;
        */

        Console.WriteLine("All done!");
        Console.ReadLine();
    }

    public static async Task FindTopRepeatedLines()
    {
        Process? openResultProcess = null;
        try
        {
            // 4) Aggregate logs
            //string folderPath = @"V:\DSP-Logs\63 Logs";
            //var logFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);
            //logFiles = logFiles.Where(f => f.Contains("\\dis\\", StringComparison.OrdinalIgnoreCase)).ToArray();
            // 1) Create a line hasher (FNV-1a, for example)
            var lineOptions = new LineOptimizationOptions
            {
                MaxLineLength = 20000,
                CheckPrefixFilterLength = 135,
                PrefixFilter = "ERROR|WARN",
                ReplacmentPatterns =
                {
                    {"O=.+,OU=.+,CN=.+,E=.+\\.com","<Certificate>"} ,
                    {@"\s+\d+\s+\|.+\d+\s+\|",""} ,
                }
            };
            ILogLineProcessor<byte[]?> hasher = new SimpleLineHasher(lineOptions);

            // 2) Create LogFilesAggregator
            //    concurrency: e.g. 4 parallel consumers
            //    maxQueueSize: e.g. 10,000 (or null for unbounded)
            //    bulkReadSize: read 200 lines at a time
            //var aggregator = new LogFilesAggregator(hasher, concurrency: 4, maxQueueSize: 10_000, bulkReadSize: 200);
            var aggregator = new LogFilesAggregatorDataflow(hasher, concurrency: 4, bulkReadSize: 200);

            // 3) An optional progress reporter: track # of files processed
            var progress = new Progress<float>(percentage =>
            {
                Console.WriteLine($"Processing {percentage}% of files till now.");
            });


            var results = await aggregator.AggregateLogFilesAsync(
                new GetLogFilesOptions
                {
                    LogFilesFolder = @"V:\DSP-Logs\63 Logs",
                    SearchPattern = "*.*",
                    EnumerationOptions = new EnumerationOptions
                    {
                        RecurseSubdirectories = true
                    },
                    //Filter = f => f.Contains("\\dis\\", StringComparison.OrdinalIgnoreCase)
                }, progress);

            // 5) Show top 10 repeated patterns
            var orderdList = results
                .OrderByDescending(kvp => kvp.Value.Count)
                .ThenByDescending(kvp => kvp.Value.Representative);

            //create a new file to write the results
            string path = $"results-{Guid.NewGuid()}.txt";
            await File.WriteAllLinesAsync(path, orderdList.Select(x => JsonSerializer.Serialize(x.Value)));
            //open file in default editor
            //Process.Start("notepad.exe", path);

            Console.WriteLine($"Done! All files are processed.");
            openResultProcess = Process.Start(new ProcessStartInfo
            {
                FileName = Path.GetFullPath(path),
                UseShellExecute = true
            });
            Console.ReadLine();
        }
        finally
        {
            openResultProcess?.Dispose();
        }
    }
}



