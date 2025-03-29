namespace LogStatTool
{
    public interface ILogLineHasher
    {
        byte[]? ComputeLineHash(string rawLine);
    }
}
