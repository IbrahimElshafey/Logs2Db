
namespace CCSS_DSP_LogsParser;

public class ParsedLogLine
{
    public int Id { get; set; }
    public DateTime? Timestamp { get; set; }
    public string? Thread { get; set; }
    public string? Logger { get; set; }
    public string? Level { get; set; }
    public string? Message { get; set; }
    public string? FileName { get; set; }
    public string? MethodName { get; set; }
    public int? CodeLineNumber { get; set; }
    public string? Ndc { get; set; }
    public string? Operator { get; set; }
    public string? MachineName { get; set; }
    public string? HostName { get; set; }
    public string? AssemblyVersion { get; set; }

    public string? LineHash { get; set; }
    public string? Source { get; set; }
    public string? LogFileName { get; set; }
}

