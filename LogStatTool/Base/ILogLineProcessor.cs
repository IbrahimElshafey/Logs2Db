using LogStatTool;

namespace LogStatTool.Base
{
    public interface ILogLineProcessor<T>
    {
        T ProcessLine(ref string rawLine);
    }
}
