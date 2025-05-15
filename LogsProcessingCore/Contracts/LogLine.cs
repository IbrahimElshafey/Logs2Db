
namespace LogsProcessingCore.Contracts
{
    public record LogLine(string Line, int LineIndex, string FilePath);
    public record LogLineSpan(ReadOnlyMemory<char> Line, int LineIndex, string FilePath);
}