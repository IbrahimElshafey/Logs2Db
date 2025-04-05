using LogStatTool;
using LogStatTool.Base;

public class HashAggregatorOptions
{
    public GetLogFilesOptions LogFilesOptions { get; set; }
    public int Concurrency { get; set; } = 4;
    public int BulkReadLinesSize { get; set; } = 50;
    public string ResultsFilePath { get; set; } = $"results-{Guid.NewGuid()}.xlsx";
    public bool OpenResultFile { get; set; } = true;
    public ILogLineProcessor<ulong?> Hasher { get; set; }
}