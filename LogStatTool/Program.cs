// See https://aka.ms/new-console-template for more information
using LogStatTool;
using System.Globalization;
using System.Text;

internal class Program
{
    public static async Task Main(string[] args)
    {
        // 4) Aggregate logs
        string folderPath = @"V:\EgateLogs\Ameer-26032025-Logs\Arrevil Logs-2025-03-26\Monitoring_Station";
        var logFiles = Directory.GetFiles(folderPath, "*.log", SearchOption.AllDirectories);
        logFiles = logFiles.Where(f => f.Contains("\\dis\\", StringComparison.OrdinalIgnoreCase)).ToArray();
        // 1) Create a line hasher (FNV-1a, for example)
        var lineOptions = new LineOptimizationOptions
        {
            MaxLineLength = 300,
            CheckPrefixFilterLength = 50,
            PrefixFilter = "[WRN]"//"[ERR]"
        };
        ILogLineHasher hasher = new SimpleLineHasher(lineOptions);

        // 2) Create LogAggregator
        //    concurrency: e.g. 4 parallel consumers
        //    maxQueueSize: e.g. 10,000 (or null for unbounded)
        //    bulkReadSize: read 200 lines at a time
        var aggregator = new LogAggregator(hasher, concurrency: 4, maxQueueSize: 10_000, bulkReadSize: 200);

        // 3) An optional progress reporter: track # of files processed
        int filesProcessed = 0;
        var progress = new Progress<int>(increment =>
        {
            filesProcessed += increment;
            Console.WriteLine($"Finished reading one more file. Total files so far: {filesProcessed}/{logFiles.Length}");
        });


        var results = await aggregator.AggregateLogFilesAsync(logFiles, progress);

        // 5) Show top 10 repeated patterns
        var top10 = results.OrderByDescending(kvp => kvp.Value.Count);
        foreach (var (hashKey, (representative, count)) in top10)
        {
            Console.WriteLine($"Count={count}\t\t\t{representative.Substring(31)}");
        }

        Console.WriteLine($"Done! Processed {filesProcessed} files in total.");
        Console.ReadLine();
    }

}



