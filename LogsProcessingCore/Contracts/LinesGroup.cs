
namespace LogsProcessingCore.Contracts
{
    public class LinesGroup
    {
        public string RepresintiveLine { get; private set; }
        public int TotalCounts { get; private set; }
        public List<(string Representative, int Count)> OriginalLines { get; set; }

        public LinesGroup(string representativeLine, int totalCount, List<(string Representative, int Count)> originalLines)
        {
            RepresintiveLine = representativeLine;
            TotalCounts = totalCount;
            OriginalLines = originalLines;
        }
    }
}


