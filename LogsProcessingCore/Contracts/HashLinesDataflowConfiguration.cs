
namespace LogsProcessingCore.Contracts
{
    public class HashLinesDataflowConfiguration
    {
        public int BoundedCapacity { get; set; } = 1000;
        public int Parallelism { get; set; } = 8;

    }
}
