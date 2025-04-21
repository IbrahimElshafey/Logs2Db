namespace LogsProcessingCore.Base
{
    public interface ILogLineProcessor<T>
    {
        T ProcessLine(string rawLine);
    }
}
