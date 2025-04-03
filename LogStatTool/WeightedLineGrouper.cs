namespace LogStatTool;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

public class WeightedLineGrouper
{
    /// <summary>
    /// Builds groups from a list of (Representative, Count) lines by:
    ///  1) Tokenizing each line.
    ///  2) Building an inverted index with line counts.
    ///  3) Computing a token weight = (globalSumForToken) + log(lineCount + 1).
    ///  4) Excluding tokens below a given threshold.
    ///  5) Grouping lines that have the same final token set, summing their counts.
    ///
    /// Returns a List of LinesGroup objects.
    /// </summary>
    /// <param name="lines">The aggregated lines, each with a text and a count of occurrences.</param>
    /// <param name="weightThreshold">Minimum weight needed for a token to remain in a line.</param>
    public List<LinesGroup> BuildLineGroups(
        List<(string Representative, int Count)> lines,
        double weightThreshold = 5.0)
    {
        if (lines == null || lines.Count == 0)
        {
            return new List<LinesGroup>();
        }

        // STEP 1) Tokenize each line
        // We'll store an array of (original, count, tokens)
        var lineTokenData = new (string original, int count, HashSet<string> tokens)[lines.Count];
        for (int i = 0; i < lines.Count; i++)
        {
            var (text, cnt) = lines[i];
            var tokens = Tokenize(text);
            lineTokenData[i] = (text, cnt, tokens);
        }

        // STEP 2) Build inverted index (tokenGlobalCount)
        // For each token, track the sum of line counts of lines containing this token
        var tokenGlobalCount = new ConcurrentDictionary<string, int>();

        foreach (var (original, cnt, tokens) in lineTokenData)
        {
            foreach (var t in tokens)
            {
                tokenGlobalCount.AddOrUpdate(
                    t,
                    cnt,
                    (key, oldVal) => oldVal + cnt);
            }
        }

        // STEP 3) For each line, exclude tokens whose weight < weightThreshold
        // weight = tokenGlobalCount[t] + log(lineCount + 1)
        for (int i = 0; i < lineTokenData.Length; i++)
        {
            var (original, cnt, tokens) = lineTokenData[i];
            var finalTokens = new HashSet<string>();

            foreach (var t in tokens)
            {
                int sumOfCounts = tokenGlobalCount[t];
                double weight = sumOfCounts + Math.Log(cnt + 1);

                if (weight >= weightThreshold)
                {
                    finalTokens.Add(t);
                }
            }

            lineTokenData[i] = (original, cnt, finalTokens);
        }

        // STEP 4) Group lines that share the same final token set
        // We'll define the "signature" of a line as the sorted tokens joined by a space
        var groupsMap = new Dictionary<string, List<(string, int)>>();

        foreach (var (original, cnt, tokens) in lineTokenData)
        {
            if (tokens.Count == 0)
            {
                // If no tokens remain, treat signature as ""
                var emptySig = "";
                if (!groupsMap.TryGetValue(emptySig, out var emptyList))
                {
                    emptyList = new List<(string, int)>();
                    groupsMap[emptySig] = emptyList;
                }
                emptyList.Add((original, cnt));
            }
            else
            {
                var sortedTokens = tokens.OrderBy(x => x).ToArray();
                string signature = string.Join(" ", sortedTokens);

                if (!groupsMap.TryGetValue(signature, out var listForSignature))
                {
                    listForSignature = new List<(string, int)>();
                    groupsMap[signature] = listForSignature;
                }
                listForSignature.Add((original, cnt));
            }
        }

        // STEP 5) Build the final LinesGroup objects
        var result = new List<LinesGroup>();
        foreach (var kvp in groupsMap)
        {
            var signature = kvp.Key;
            var linesList = kvp.Value; // all lines that share this final token set
            int sumCount = linesList.Sum(x => x.Item2);

            // If signature is empty, let's pick the first original line as a representative
            // Otherwise, use the signature itself
            string representative = string.IsNullOrEmpty(signature) && linesList.Count > 0
                ? linesList[0].Item1
                : signature;

            // Create a LinesGroup
            var group = new LinesGroup(
                representativeLine: representative,
                totalCount: sumCount,
                originalLines: linesList
            );
            result.Add(group);
        }

        return result;
    }

    /// <summary>
    /// A simple tokenizer that:
    ///  1) Splits on whitespace
    ///  2) Trims punctuation from edges
    ///  3) Converts to lower
    ///  4) Excludes empty or extremely long tokens
    /// </summary>
    private HashSet<string> Tokenize(string line)
    {
        var tokens = line
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(tok => tok.Trim().ToLowerInvariant().Trim('.', ',', ';', ':', '"', '\''))
            .Where(t => t.Length > 0 && t.Length < 50)
            .ToHashSet();

        return tokens;
    }
}
