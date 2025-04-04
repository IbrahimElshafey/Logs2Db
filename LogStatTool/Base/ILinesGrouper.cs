namespace LogStatTool.Base;
using ClosedXML.Excel;

using DocumentFormat.OpenXml.Spreadsheet;
using LogStatTool.Contracts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

public interface ILinesGrouper
{
    List<LinesGroup> BuildLineGroups(List<(string Representative, int Count)> lines);
}
