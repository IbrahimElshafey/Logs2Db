namespace LogsProcessingCore.Base;
using System.Collections.Generic;

public interface ILinesGrouper
{
    List<Contracts.LinesGroup> BuildLineGroups(List<(string Representative, int Count)> lines);
}
