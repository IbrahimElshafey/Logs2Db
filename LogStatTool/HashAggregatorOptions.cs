using LogsProcessingCore.Contracts;
using LogsProcessingCore;

namespace LogStatTool;
public class HashAggregatorOptions
{
    public LogFilesOptions LogFilesOptions { get; set; }
    public ProduceLinesDataflowConfiguration ProduceLinesDataflowConfiguration { get; set; }
    public HashLinesDataflowConfiguration HashLinesDataflowConfiguration { get; set; }
    public string ResultsFilePath { get; set; } = $"results-{Guid.NewGuid()}.xlsx";
    public bool OpenResultFile { get; set; } = true;
    public LogsProcessingCore.Base.ILogLineProcessor<ulong?> Hasher { get; set; }
    public bool GenerateResultFilePerFolder { get; set; } = false;
}