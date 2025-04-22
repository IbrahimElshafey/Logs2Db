// See https://aka.ms/new-console-template for more information
using ClosedXML.Excel;
using LogsProcessingCore.Contracts;
using LogsProcessingCore.Implementations;
using LogStatTool;


using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Globalization;
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
            case 3:
                await TestLineProducerChannel();
                break;
            default:
                Console.WriteLine("Invalid option");
                break;
        }
        Console.ReadLine();
    }
    static async Task TestLineProducerChannel()
    {
        var stopwatch = Stopwatch.StartNew(); // Start measuring time
        var initialMemory = GC.GetAllocatedBytesForCurrentThread(); // Capture initial memory usage

        var config = await LoadConfigurationAsync(@$".\HashAggregatorConfigFiles\bes.json");
        var linesProdcucer = new LogsProcessingCore.Base.LogFileLineProducerChannel(
            config.HashAggregatorOptions.LogFilesOptions,
            config.HashAggregatorOptions.ProduceLinesDataflowConfiguration);
        var progress = new Progress<float>(p =>
        {
            Console.WriteLine($"Reading progress: {p:F2}%");
        }
        );
        var reader = linesProdcucer.Build(progress, CancellationToken.None);
        _ = linesProdcucer.PostAllFilePathsAsync();

        //read lines from the channel
        int linesCount = 0;
        await foreach (var line in reader.ReadAllAsync())
        {
            Interlocked.Increment(ref linesCount);
        }
        var finalMemory = GC.GetAllocatedBytesForCurrentThread(); // Capture final memory usage
        stopwatch.Stop(); // Stop measuring time

        Console.WriteLine($"Time consumed: {stopwatch.Elapsed}");
        Console.WriteLine($"Memory used: {(finalMemory - initialMemory) / 1024 / 1024} bytes");

        Console.WriteLine(GC.GetAllocatedBytesForCurrentThread());
        GC.Collect();
        Console.WriteLine(GC.GetAllocatedBytesForCurrentThread());
        Console.WriteLine(JsonConvert.SerializeObject(GC.GetGCMemoryInfo(), Formatting.Indented));
        Console.ForegroundColor = ConsoleColor.Green;
        GC.Collect();
        Console.WriteLine($"Total processed lines are {linesCount}");
    }

    static async Task TestLineProducer()
    {
        var stopwatch = Stopwatch.StartNew(); // Start measuring time
        var initialMemory = GC.GetAllocatedBytesForCurrentThread(); // Capture initial memory usage

        var config = await LoadConfigurationAsync(@$".\HashAggregatorConfigFiles\bes.json");
        var linesProdcucer = new LogsProcessingCore.Base.LogFileLineProducer(
            config.HashAggregatorOptions.LogFilesOptions,
            config.HashAggregatorOptions.ProduceLinesDataflowConfiguration);
        var progress = new Progress<float>(p =>
            {
                Console.WriteLine($"Reading progress: {p:F2}%");
            }
        );
        var linesBlock = linesProdcucer.Build(progress, CancellationToken.None);
        int linesCount = 0;
        var countLinesBlock = new ActionBlock<LogLine>(
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
        Console.WriteLine($"Memory used: {(finalMemory - initialMemory) / 1024 / 1024} bytes");

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
            { 6, "CCSS" },
        };
        var config = await LoadConfigurationAsync(@$".\HashAggregatorConfigFiles\{configFiles[6]}.json");



        // Create a line hasher
        LogsProcessingCore.Base.ILogLineProcessor<ulong?> hasher = new SimpleLineHasher(config.LineOptimizationOptions);

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

    private static async Task ProcessFilesInFolder((HashAggregatorOptions HashAggregatorOptions, LogsProcessingCore.Contracts.LineOptimizationOptions LineOptimizationOptions) config, LogsProcessingCore.Base.ILogLineProcessor<ulong?> hasher)
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
        var groupedLines = new LinePrefixGrouper(40).BuildLineGroups(sortedResults);
        // Save the grouped lines to a file
        var orderedGroups = groupedLines
            .Where(group => group.OriginalLines.Count > 2)
            .OrderByDescending(x => x.TotalCounts)
            .ToList();
        SaveLinesGroupsToFile(orderedGroups, groupFile);
    }
    private static void SaveLinesGroupsToFile(List<LogsProcessingCore.Contracts.LinesGroup> groups, string groupsFile)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Grouped Lines");

        int currentRow = 1;

        foreach (var group in groups)
        {
            // Section header
            sheet.Cell(currentRow, 1).Value = $"== {group.RepresintiveLine} (Total: {group.TotalCounts}) ==";
            sheet.Range(currentRow, 1, currentRow, 2).Merge();
            sheet.Row(currentRow).Style.Font.Bold = true;
            sheet.Row(currentRow).Style.Fill.BackgroundColor = XLColor.LightGray;
            currentRow++;

            // Column headers
            sheet.Cell(currentRow, 1).Value = "Representative Line";
            sheet.Cell(currentRow, 2).Value = "Count";
            sheet.Row(currentRow).Style.Font.Bold = true;
            currentRow++;

            // Data rows
            foreach (var (rep, count) in group.OriginalLines)
            {
                sheet.Cell(currentRow, 1).Value = rep;
                sheet.Cell(currentRow, 2).Value = count;
                currentRow++;
            }

            // Blank line between groups
            currentRow++;
        }

        // Wrap text in first column and limit width
        var col1 = sheet.Column(1);
        col1.Width = 100;
        col1.Style.Alignment.WrapText = true;
        if (col1.Width > 500) col1.Width = 500;

        sheet.Column(2).AdjustToContents();

        // Freeze first row (topmost group header)
        sheet.SheetView.FreezeRows(1);

        var filePath = string.IsNullOrWhiteSpace(groupsFile) ?
            $"GroupedLines_{Guid.NewGuid()}.xlsx" :
            groupsFile;
        workbook.SaveAs(filePath);

        Process.Start(new ProcessStartInfo
        {
            FileName = Path.GetFullPath(filePath),
            UseShellExecute = true
        });
    }
    private static async Task<(HashAggregatorOptions HashAggregatorOptions, LogsProcessingCore.Contracts.LineOptimizationOptions LineOptimizationOptions)> LoadConfigurationAsync(string filePath)
    {
        using var stream = File.OpenText(filePath);
        using var reader = new JsonTextReader(stream);
        reader.SupportMultipleContent = true;
        var jsonDoc = await JObject.LoadAsync(reader);

        var getLogFilesOptions = jsonDoc["HashAggregatorOptions"].ToObject<HashAggregatorOptions>();
        var lineOptimizationOptions = jsonDoc["LineOptimizationOptions"].ToObject<LogsProcessingCore.Contracts.LineOptimizationOptions>();

        return (getLogFilesOptions, lineOptimizationOptions);
    }

}



