namespace LogStatTool;
using ClosedXML.Excel;

using DocumentFormat.OpenXml.Spreadsheet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

public class SequentialGcpWithMinLengthGrouper
{
    private readonly int _minAcceptablePrefixLength;
    private readonly bool _saveResult;

    /// <summary>
    /// Constructs a GCP grouper requiring that the final shared prefix never go below _minAcceptablePrefixLength.
    /// If the GCP is shorter, we finalize the current group and start a new one.
    /// </summary>
    /// <param name="minAcceptablePrefixLength">Minimum prefix length needed to keep lines in the same group.</param>
    public SequentialGcpWithMinLengthGrouper(int minAcceptablePrefixLength = 40, bool saveResult = false)
    {
        if (minAcceptablePrefixLength <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(minAcceptablePrefixLength),
                "Minimum acceptable prefix length must be > 0.");

        _minAcceptablePrefixLength = minAcceptablePrefixLength;
        _saveResult = saveResult;
    }

    /// <summary>
    /// Groups lines (string Representative, int Count) as follows:
    ///  1) Sort by Representative text (lexicographically).
    ///  2) Start a group with the first line as the prefix.
    ///  3) For each next line:
    ///     - Compute GCP(currentPrefix, nextLine).
    ///     - If GCP.Length >= _minAcceptablePrefixLength, stay in current group (update prefix = GCP).
    ///     - Else, finalize current group, start a new group with nextLine.
    ///  4) Each group has:
    ///     - RepresintiveLine = the final prefix
    ///     - TotalCounts = sum of all line counts in that group
    ///     - OriginalLines = the list of lines that share that prefix
    /// </summary>
    public List<LinesGroup> BuildLineGroups(List<(string Representative, int Count)> lines)
    {
        var result = new List<LinesGroup>();
        if (lines == null || lines.Count == 0)
            return result;

        // 1) Sort lines by Representative
        var sorted = lines.OrderBy(x => x.Representative).ToList();

        // Initialize the first group with the first line's entire text
        string currentPrefix = sorted[0].Representative;
        var currentGroupLines = new List<(string Representative, int Count)>
        {
            (sorted[0].Representative, sorted[0].Count)
        };

        // 2) Iterate over the remaining lines
        for (int i = 1; i < sorted.Count; i++)
        {
            var (nextLine, nextCount) = sorted[i];

            // Compute GCP with the current prefix
            string gcp = GreatestCommonPrefix(currentPrefix, nextLine);

            if (gcp.Length >= _minAcceptablePrefixLength)
            {
                // The group prefix remains >= our min length
                currentPrefix = gcp;
                currentGroupLines.Add((nextLine, nextCount));
            }
            else
            {
                // If GCP is too short, finalize the current group
                var group = CreateGroup(currentPrefix, currentGroupLines);
                result.Add(group);

                // Start a new group with nextLine as prefix
                currentPrefix = nextLine;
                currentGroupLines = new List<(string Representative, int Count)>
                {
                    (nextLine, nextCount)
                };
            }
        }

        // 3) Finalize the last group
        if (currentGroupLines.Count > 0)
        {
            var finalGroup = CreateGroup(currentPrefix, currentGroupLines);
            result.Add(finalGroup);
        }

        if (_saveResult)
        {
            SaveResultToFile(result.Where(x => x.OriginalLines.Count > 1).OrderByDescending(x=>x.TotalCounts).ToList());
        }
        return result;
    }

    private void SaveResultToFile(List<LinesGroup> groups)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Grouped Lines");

        int currentRow = 1;

        foreach (var group in groups)
        {
            // Section header
            sheet.Cell(currentRow, 1).Value = $"== {group.RepresintiveLine} (Total: {group.TotalCounts}) ==";
            sheet.Range(currentRow, 1, currentRow, 2).Merge();
            sheet.Row(currentRow).Style.Font.Bold = true;
            sheet.Row(currentRow).Style.Fill.BackgroundColor = XLColor.LightGray;
            currentRow++;

            // Column headers
            sheet.Cell(currentRow, 1).Value = "Representative Line";
            sheet.Cell(currentRow, 2).Value = "Count";
            sheet.Row(currentRow).Style.Font.Bold = true;
            currentRow++;

            // Data rows
            foreach (var (rep, count) in group.OriginalLines)
            {
                sheet.Cell(currentRow, 1).Value = rep;
                sheet.Cell(currentRow, 2).Value = count;
                currentRow++;
            }

            // Blank line between groups
            currentRow++;
        }

        // Wrap text in first column and limit width
        var col1 = sheet.Column(1);
        col1.Width = 100;
        col1.Style.Alignment.WrapText = true;
        if (col1.Width > 500) col1.Width = 500;

        sheet.Column(2).AdjustToContents();

        // Freeze first row (topmost group header)
        sheet.SheetView.FreezeRows(1);

        var filePath = $"GroupedLines_{Guid.NewGuid()}.xlsx";
        workbook.SaveAs(filePath);

        Process.Start(new ProcessStartInfo
        {
            FileName = Path.GetFullPath(filePath),
            UseShellExecute = true
        });
    }

    /// <summary>
    /// Finds the greatest common prefix between two strings.
    /// Returns "" if there's no shared beginning character.
    /// </summary>
    private string GreatestCommonPrefix(string s1, string s2)
    {
        int minLen = Math.Min(s1.Length, s2.Length);
        int idx = 0;
        while (idx < minLen && s1[idx] == s2[idx])
        {
            idx++;
        }
        return s1.Substring(0, idx);
    }

    /// <summary>
    /// Builds a LinesGroup with the final prefix as the representative line,
    /// sum of counts as TotalCounts, and OriginalLines as the line set.
    /// </summary>
    private LinesGroup CreateGroup(string prefix, List<(string Representative, int Count)> groupLines)
    {
        int totalCount = groupLines.Sum(x => x.Count);
        return new LinesGroup(prefix, totalCount, groupLines);
    }
}
