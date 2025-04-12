using LogStatTool;

namespace LogStatTool.Base
{
    public interface ILogLineProcessor<T>
    {
        T ProcessLine(string rawLine);
    }
}
