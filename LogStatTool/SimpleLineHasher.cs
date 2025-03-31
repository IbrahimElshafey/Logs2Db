using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace LogStatTool;

public class SimpleLineHasher : ILogLineHasher
{
    private readonly LineOptimizationOptions _options;
    private readonly Dictionary<string, Regex> _compiledPatterns;
    public SimpleLineHasher(LineOptimizationOptions options)
    {
        _options = options;
        if (options?.ReplacmentPatterns?.Any() is true)
        {
            _compiledPatterns = new();
            foreach (var pattern in options.ReplacmentPatterns)
            {
                _compiledPatterns.Add(pattern.Key, new Regex(pattern.Key, RegexOptions.Compiled));
            }
        }
    }

    /// <summary>
    /// Processes a log line, normalizes it on the fly and computes its FNV‑1a hash,
    /// returning the hash as a byte array.
    /// Returns null if the line fails filtering.
    /// </summary>
    public byte[]? ComputeLineHash(string rawLine)
    {
        if (string.IsNullOrEmpty(rawLine))
            return null;

        rawLine = CheckReplacments(rawLine);
        // Check the first CheckPrefixFilterLength characters for the PrefixFilter using ReadOnlySpan.
        int checkLen = Math.Min(_options.CheckPrefixFilterLength, rawLine.Length);
        ReadOnlySpan<char> firstPart = rawLine.AsSpan(0, checkLen);
        if (Regex.IsMatch(firstPart.ToString(), _options.PrefixFilter, RegexOptions.IgnoreCase) is false)
            return null;

        // Truncate the line to MaxLineLength characters.
        if (rawLine.Length > _options.MaxLineLength)
            rawLine = rawLine.Substring(0, _options.MaxLineLength);

        ulong hash = ComputeFNV1aHashOnTheFly(rawLine.AsSpan());
        return BitConverter.GetBytes(hash);
    }

    private string CheckReplacments(string rawLine)
    {
        foreach (var pattern in _options.ReplacmentPatterns)
        {
            rawLine = _compiledPatterns[pattern.Key].Replace(rawLine, pattern.Value);
        }
        return rawLine;
    }

    /// <summary>
    /// Computes the FNV‑1a hash by scanning through the line's characters, 
    /// tokenizing on the fly and updating the hash for each valid token (and a space delimiter between tokens).
    /// </summary>
    private static ulong ComputeFNV1aHashOnTheFly(ReadOnlySpan<char> lineSpan)
    {
        const ulong fnvOffsetBasis = 1469598103934665603UL;
        const ulong fnvPrime = 1099511628211UL;
        ulong hash = fnvOffsetBasis;
        bool firstToken = true;
        int tokenStart = -1;

        for (int i = 0; i < lineSpan.Length; i++)
        {
            if (!char.IsWhiteSpace(lineSpan[i]))
            {
                if (tokenStart == -1)
                    tokenStart = i;
            }
            else
            {
                if (tokenStart != -1)
                {
                    ReadOnlySpan<char> token = lineSpan.Slice(tokenStart, i - tokenStart);
                    if (IsValidToken(token))
                    {
                        // Insert delimiter between tokens.
                        if (!firstToken)
                            hash = UpdateFNV1aHash(hash, ' ');
                        else
                            firstToken = false;

                        // Update the hash with the token.
                        hash = UpdateFNV1aHash(hash, token);
                    }
                    tokenStart = -1;
                }
            }
        }

        // Process any trailing token.
        if (tokenStart != -1)
        {
            ReadOnlySpan<char> token = lineSpan.Slice(tokenStart);
            if (IsValidToken(token))
            {
                if (!firstToken)
                    hash = UpdateFNV1aHash(hash, ' ');
                hash = UpdateFNV1aHash(hash, token);
            }
        }

        return hash;
    }
    /// <summary>
    /// Updates the FNV‑1a hash with each character from the given token.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong UpdateFNV1aHash(ulong hash, ReadOnlySpan<char> token)
    {
        const ulong fnvPrime = 1099511628211UL;
        for (int i = 0; i < token.Length; i++)
        {
            hash ^= token[i];
            hash *= fnvPrime;
        }
        return hash;
    }

    /// <summary>
    /// Overload for updating the hash with a single character.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong UpdateFNV1aHash(ulong hash, char c)
    {
        const ulong fnvPrime = 1099511628211UL;
        hash ^= c;
        hash *= fnvPrime;
        return hash;
    }


    /// <summary>
    /// Returns true if the token is valid:
    /// - It does not contain any digits,
    /// - It is not longer than 40 characters,
    /// - And it contains only letters, underscores, or hyphens.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsValidToken(ReadOnlySpan<char> token)
    {
        if (token.Length > 40)
            return false;

        for (int i = 0; i < token.Length; i++)
        {
            char c = token[i];
            if (char.IsDigit(c))
                return false;
            if (!(char.IsLetter(c) || c == '_' || c == '-'))
                return false;
        }
        return true;
    }
}
