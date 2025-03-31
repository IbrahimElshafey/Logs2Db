using System;

namespace LogStatTool
{
    public class LineOptimizationOptions
    {
        public int MaxLineLength { get; set; } = 300;
        public int CheckPrefixFilterLength { get; set; } = 50;
        public string PrefixFilter { get; set; } = "[Err]";

        public Dictionary<string, string> ReplacmentPatterns { get; set; } = new();
    }
}
