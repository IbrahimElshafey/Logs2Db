namespace LogStatTool
{
    public interface ILogLineProcessor<T>
    {
        T ProcessLine(ref string rawLine);
    }
}
