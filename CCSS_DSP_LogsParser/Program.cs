// See https://aka.ms/new-console-template for more information
using CCSS_DSP_LogsParser;
using System.Collections.Concurrent;

internal class Program
{
    private static async Task Main(string[] args)
    {
        using (var db = new LogsDbContext())
        {
            Console.WriteLine("Ensuring database is deleted first...");
            await db.Database.EnsureDeletedAsync();
            Console.WriteLine("Ensuring empty database is created...");
            await db.Database.EnsureCreatedAsync();
        }
        //await TplDataFlow();
        await SimpleParser();
        Console.WriteLine("Processing finished");
        Console.ReadLine();
    }

    private static async Task SimpleParser()
    {
        // Process CCSS logs
        await ProcessDirectoryAsync(@"V:\CCSS", "CCSS");
        // Process DSP logs
        await ProcessDirectoryAsync(@"V:\DSP-All-Logs", "DSP");

        Console.WriteLine("Processing finished");
        Console.ReadLine();
    }

    private static async Task ProcessDirectoryAsync(string directoryPath, string sourceSystem)
    {
        var logFiles = Directory.EnumerateFiles(directoryPath, "*.log", SearchOption.AllDirectories).ToList();
        int totalFiles = logFiles.Count;
        int processed = 0;
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
        var allDbItems = new ConcurrentBag<ParsedLogLine>();
        await Parallel.ForEachAsync(logFiles, parallelOptions, async (file, ct) =>
        {
            int current = Interlocked.Increment(ref processed);
            double percent = current * 100.0 / totalFiles;
            Console.WriteLine($"Progress: {percent:F2}% ({current}/{totalFiles}) - Processing file: {file}");
            var parser = new SimpleLogParser(file, sourceSystem);
            var logLines = await parser.ParseLogsAsync(ct);
            foreach (var logLine in logLines)
            {
                allDbItems.Add(logLine);
            }
        });
        using (var db = new LogsDbContext())
        {
            Console.WriteLine($"Saving {allDbItems.Count} log lines to database...");
            db.ParsedLogLines.AddRange(allDbItems);
            await db.SaveChangesAsync();
            Console.WriteLine("All log lines saved to database.");
        }
    }

    private static async Task TplDataFlow()
    {
        var ccssLogProcessor = new Log4NetLogs2Db(@"V:\CCSS", "CCSS");
        await ccssLogProcessor.ParseLogs();
        ccssLogProcessor = new Log4NetLogs2Db(@"V:\DSP-All-Logs", "DSP");
        await ccssLogProcessor.ParseLogs();
    }
}