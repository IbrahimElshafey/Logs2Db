
using LogsProcessingCore.Base;

namespace CCSS_DSP_LogsParser;
public class ParsedLogLine : ParsedLogLineBase
{
    public int Id { get; set; }
    public string? Thread { get; set; }
    public string? Logger { get; set; }
    public string? MethodName { get; set; }
    public int? CodeLineNumber { get; set; }
    public string? Ndc { get; set; }
    public string? Operator { get; set; }
    public string? MachineName { get; set; }
    public string? HostName { get; set; }
    public string? AssemblyVersion { get; set; }
    public string? LineHash { get; set; }
}

