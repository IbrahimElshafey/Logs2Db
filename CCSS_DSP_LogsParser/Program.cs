// See https://aka.ms/new-console-template for more information
using CCSS_DSP_LogsParser;
using System.Collections.Concurrent;
using System.Threading.Channels;

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
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        // Process CCSS logs
        await ProcessDirectoryAsync(@"V:\CCSS", "CCSS");
        // Process DSP logs
        await ProcessDirectoryAsync(@"V:\DSP-All-Logs", "DSP");
        stopwatch.Stop();
        Console.WriteLine($"Processing finished in {stopwatch.Elapsed}");
        Console.ReadLine();
    }
    private static async Task ProcessDirectoryAsync(string directoryPath, string sourceSystem)
    {
        var logFiles = Directory.EnumerateFiles(directoryPath, "*.log", SearchOption.AllDirectories).ToList();
        int totalFiles = logFiles.Count;
        int processed = 0;
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

        var channel = Channel.CreateBounded<ParsedLogLine>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = true
        });

        // Consumer Task
        var consumer = Task.Run(async () =>
        {
            await foreach (var item in channel.Reader.ReadAllAsync())
            {
             
            }
            //var batch = new List<ParsedLogLine>(1000);
            //await foreach (var item in channel.Reader.ReadAllAsync())
            //{
            //    batch.Add(item);
            //    if (batch.Count >= 1000)
            //    {
            //        await SaveBatchAsync(batch);
            //        batch.Clear();
            //    }
            //}

            //if (batch.Count > 0)
            //    await SaveBatchAsync(batch);
        });

        // Producer logic
        await Parallel.ForEachAsync(logFiles, parallelOptions, async (file, ct) =>
        {
            int current = Interlocked.Increment(ref processed);
            double percent = current * 100.0 / totalFiles;
            Console.WriteLine($"Progress: {percent:F2}% ({current}/{totalFiles}) - Processing file: {file}");

            var parser = new SimpleLogParser(file, sourceSystem);
            var logLines = await parser.ParseLogsAsync(ct);
            foreach (var logLine in logLines)
            {
                await channel.Writer.WriteAsync(logLine, ct);
            }
        });

        // Signal completion
        channel.Writer.Complete();
        await consumer;

        Console.WriteLine("All log lines saved to database.");
    }
    //private static async Task SaveBatchAsync(List<ParsedLogLine> batch)
    //{
    //    using var db = new LogsDbContext(skipEnsureCreated: true);
    //    db.ParsedLogLines.AddRange(batch);
    //    await db.SaveChangesAsync();
    //}
    //private static async Task ProcessDirectoryAsyncO(string directoryPath, string sourceSystem)
    //{
    //    var logFiles = Directory.EnumerateFiles(directoryPath, "*.log", SearchOption.AllDirectories).ToList();
    //    int totalFiles = logFiles.Count;
    //    int processed = 0;
    //    var parallelOptions = new ParallelOptions
    //    {
    //        MaxDegreeOfParallelism = Environment.ProcessorCount,
    //        TaskScheduler = TaskScheduler.Default
    //    };
    //    var allDbItems = new ConcurrentBag<ParsedLogLine>();
    //    await Parallel.ForEachAsync(logFiles, parallelOptions, async (file, ct) =>
    //    {
    //        int current = Interlocked.Increment(ref processed);
    //        double percent = current * 100.0 / totalFiles;
    //        Console.WriteLine($"Progress: {percent:F2}% ({current}/{totalFiles}) - Processing file: {file}");
    //        var parser = new SimpleLogParser(file, sourceSystem);
    //        //var logLines = await parser.ParseLogsAsync(ct);
    //        //foreach (var logLine in logLines)
    //        //{
    //        //    allDbItems.Add(logLine);
    //        //}
    //        await foreach (var logLine in parser.ParseLogsAsync2(ct))
    //        {
    //            allDbItems.Add(logLine);
    //        }
    //    });List<ParsedLogLine>
    //    using (var db = new LogsDbContext())
    //    {
    //        Console.WriteLine($"Saving {allDbItems.Count} log lines to database...");
    //        db.ParsedLogLines.AddRange(allDbItems);
    //        await db.SaveChangesAsync();
    //        db.ChangeTracker.Clear();
    //        Console.WriteLine("All log lines saved to database.");
    //    }
    //    GC.Collect();
    //}

    //private static async Task TplDataFlow()
    //{
    //    var ccssLogProcessor = new Log4NetLogs2Db(@"V:\CCSS", "CCSS");
    //    await ccssLogProcessor.ParseLogs();
    //    ccssLogProcessor = new Log4NetLogs2Db(@"V:\DSP-All-Logs", "DSP");
    //    await ccssLogProcessor.ParseLogs();
    //}
}