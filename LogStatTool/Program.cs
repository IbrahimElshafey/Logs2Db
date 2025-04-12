// See https://aka.ms/new-console-template for more information
using DocumentFormat.OpenXml.Presentation;
using LogStatTool;
using LogStatTool.Base;
using LogStatTool.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks.Dataflow;

internal class Program
{
    public static async Task Main(string[] args)
    {
        Console.BufferHeight = 100;
        switch (1)
        {
            case 1:
                await TestLineProducer();
                break;
            case 2:
                await FindTopRepeatedLinesRefactored();
                break;
            default:
                Console.WriteLine("Invalid option");
                break;
        }
        Console.ReadLine();
    }
    static async Task TestLineProducer()
    {
        var stopwatch = Stopwatch.StartNew(); // Start measuring time
        var initialMemory = GC.GetAllocatedBytesForCurrentThread(); // Capture initial memory usage

        var config = await LoadConfigurationAsync(@$".\HashAggregatorConfigFiles\bes.json");
        var linesProdcucer = new LogFileLineProducer(
            config.HashAggregatorOptions.LogFilesOptions,
            config.HashAggregatorOptions.ProduceLinesDataflowConfiguration);
        var progress = new Progress<float>(p =>
            {
                //Console.WriteLine($"Reading progress: {p:F2}%");
            }
        );
        var linesBlock = linesProdcucer.Build(progress, CancellationToken.None);
        int linesCount = 0;
        var countLinesBlock = new ActionBlock<string>(
            line =>
            {
                Interlocked.Increment(ref linesCount);
                line = null;
                //Console.WriteLine($"####Lines count is {linesCount}");
            },
            new ExecutionDataflowBlockOptions { BoundedCapacity = -1 });
        linesBlock.LinkTo(countLinesBlock, new DataflowLinkOptions { PropagateCompletion = true });
        await linesProdcucer.PostAllFilePathsAsync();
        linesBlock.Complete();
        await countLinesBlock.Completion;

        var finalMemory = GC.GetAllocatedBytesForCurrentThread(); // Capture final memory usage
        stopwatch.Stop(); // Stop measuring time

        Console.WriteLine($"Time consumed: {stopwatch.Elapsed}");
        Console.WriteLine($"Memory used: {(finalMemory - initialMemory)/1024/1024} bytes");

        Console.WriteLine(GC.GetAllocatedBytesForCurrentThread());
        GC.Collect();
        Console.WriteLine(GC.GetAllocatedBytesForCurrentThread());
        Console.WriteLine(JsonConvert.SerializeObject(GC.GetGCMemoryInfo(), Formatting.Indented));
        Console.ForegroundColor = ConsoleColor.Green;
        GC.Collect();
        Console.WriteLine($"Total processed lines are {linesCount}");
    }

    public static async Task FindTopRepeatedLinesRefactored()
    {

        Dictionary<int, string> configFiles = new()
        {
            { 1, "DSP" },
            { 2, "UDA" },
            { 3, "Driver" },
            { 4, "BES" },
            { 5, "DIS" },
        };
        var config = await LoadConfigurationAsync(@$".\HashAggregatorConfigFiles\{configFiles[4]}.json");



        // Create a line hasher
        LogStatTool.Base.ILogLineProcessor<ulong?> hasher = new SimpleLineHasher(config.LineOptimizationOptions);

        if (config.HashAggregatorOptions.GenerateResultFilePerFolder)
        {
            var folders = Directory.GetDirectories(config.HashAggregatorOptions.LogFilesOptions.LogFilesFolder);
            foreach (var folder in folders)
            {
                config.HashAggregatorOptions.LogFilesOptions.LogFilesFolder = folder;
                await ProcessFilesInFolder(config, hasher);
            }
        }
        else
        {
            await ProcessFilesInFolder(config, hasher);
        }

        Console.WriteLine("All done!");
        Console.ReadLine();
    }

    private static async Task ProcessFilesInFolder((HashAggregatorOptions HashAggregatorOptions, LineOptimizationOptions LineOptimizationOptions) config, LogStatTool.Base.ILogLineProcessor<ulong?> hasher)
    {
        config.HashAggregatorOptions.Hasher = hasher;
        var folderName = Path.GetFileName(config.HashAggregatorOptions.LogFilesOptions.LogFilesFolder);
        //append date to folder name use change extension to remove the extension
        config.HashAggregatorOptions.ResultsFilePath = string.Format("{0}{1}-{2}.xlsx",
            Path.ChangeExtension(config.HashAggregatorOptions.ResultsFilePath, null),
            folderName,
            DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        // Create the aggregator pipeline. We'll auto-save results to file, 
        // and open that file once complete.
        var aggregator = new HashAggregatorPipeline(config.HashAggregatorOptions);
        /*
            logFilesOptions: logOptions,
            hasher: hasher,
            concurrency: 8,
            bulkReadSize: 100,
            resultsFilePath: $"results-{Guid.NewGuid()}.xlsx",
            openResultFile: true
        */

        // Optionally track reading progress
        var progress = new Progress<float>(p =>
            Console.WriteLine($"Reading progress: {p:F2}%")
        );

        // Build and run pipeline
        await aggregator.BuildAndRunAsync(progress);

        // If we want an in-memory sorted list:
        var sortedResults = await aggregator.GetOrderedResultsAsync();

        var groupFile = Path.ChangeExtension(config.HashAggregatorOptions.ResultsFilePath, null) + "-Groups.xlsx";
        var groupedLines = new SequentialGcpWithMinLengthGrouper(
            resultsFilePath: groupFile,
            saveResult: true,
            groupPredicate: x => x.OriginalLines.Count > 0)
            .BuildLineGroups(sortedResults);
    }

    private static async Task<(HashAggregatorOptions HashAggregatorOptions, LineOptimizationOptions LineOptimizationOptions)> LoadConfigurationAsync(string filePath)
    {
        using var stream = File.OpenText(filePath);
        using var reader = new JsonTextReader(stream);
        reader.SupportMultipleContent = true;
        var jsonDoc = await JObject.LoadAsync(reader);

        var getLogFilesOptions = jsonDoc["HashAggregatorOptions"].ToObject<HashAggregatorOptions>();
        var lineOptimizationOptions = jsonDoc["LineOptimizationOptions"].ToObject<LineOptimizationOptions>();

        return (getLogFilesOptions, lineOptimizationOptions);
    }

}



