
namespace LogsProcessingCore.Contracts
{
    public class ProduceLinesDataflowConfiguration
    {
        public int PathsBoundedCapacity { get; set; } = 3;
        public int BulkReadSize { get; set; } = 1024 * 2024;
        public int PathToLinesParallelism { get; set; } = 8;
        public int PathToLinesBoundedCapacity { get; set; } = 10;
    }
}
