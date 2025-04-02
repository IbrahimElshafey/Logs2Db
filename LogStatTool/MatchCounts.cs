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
