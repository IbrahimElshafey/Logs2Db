namespace LogStatTool
{
    public interface ILogLineProcessor<T>
    {
        T ProcessLine(string rawLine);
    }
}
