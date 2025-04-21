using System.Diagnostics;

namespace LogsProcessingCore.Implementations;
public class LinePrefixGrouper : Base.ILinesGrouper
{
    private readonly int _minAcceptablePrefixLength;

    /// <summary>
    /// Constructs a GCP grouper requiring that the final shared prefix never go below _minAcceptablePrefixLength.
    /// If the GCP is shorter, we finalize the current group and start a new one.
    /// </summary>
    /// <param name="minAcceptablePrefixLength">Minimum prefix length needed to keep lines in the same group.</param>
    public LinePrefixGrouper(int minAcceptablePrefixLength = 40)
    {
        if (minAcceptablePrefixLength <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(minAcceptablePrefixLength),
                "Minimum acceptable prefix length must be > 0.");

        _minAcceptablePrefixLength = minAcceptablePrefixLength;
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
    public List<LogsProcessingCore.Contracts.LinesGroup> BuildLineGroups(List<(string Representative, int Count)> lines)
    {
        var result = new List<LogsProcessingCore.Contracts.LinesGroup>();
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
        return result;
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
    private LogsProcessingCore.Contracts.LinesGroup CreateGroup(string prefix, List<(string Representative, int Count)> groupLines)
    {
        int totalCount = groupLines.Sum(x => x.Count);
        return new LogsProcessingCore.Contracts.LinesGroup(prefix, totalCount, groupLines);
    }
}
