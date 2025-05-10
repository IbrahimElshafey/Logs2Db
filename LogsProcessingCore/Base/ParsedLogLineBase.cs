namespace LogsProcessingCore.Base;

public class ParsedLogLineBase
{
    public DateTime? Timestamp { get; set; }
    public string? Level { get; set; }
    public string? Message { get; set; }
    public string? FilePath { get; set; }
    public int? LogLineNumber { get; set; }
    public string? Source { get; set; }
}
