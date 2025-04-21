
namespace LogsProcessingCore.Contracts;

public record struct MatchCounts(string Representative, int Count)
{
    public static implicit operator (string Representative, int Count)(Contracts.MatchCounts value)
    {
        return (value.Representative, value.Count);
    }

    public static implicit operator Contracts.MatchCounts((string Representative, int Count) value)
    {
        return new Contracts.MatchCounts(value.Representative, value.Count);
    }
}


