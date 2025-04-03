namespace LogStatTool;

public record struct MatchCounts(string Representative, int Count)
{
    public static implicit operator (string Representative, int Count)(MatchCounts value)
    {
        return (value.Representative, value.Count);
    }

    public static implicit operator MatchCounts((string Representative, int Count) value)
    {
        return new MatchCounts(value.Representative, value.Count);
    }
}

public class LinesGroup
{
    public string RepresintiveLine { get; private set; }
    public string TotalCounts { get; private set; }
    public List<(string Representative, int Count)> OriginalLines { get; set; }

    public LinesGroup(string representativeLine, int totalCount, List<(string Representative, int Count)> originalLines)
    {
        RepresintiveLine = representativeLine;
        TotalCounts = totalCount.ToString();
        OriginalLines = originalLines;
    }
}


