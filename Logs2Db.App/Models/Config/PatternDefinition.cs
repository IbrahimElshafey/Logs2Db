using System.Collections.Generic;

namespace Logs2Db.App.Models.Config
{
    public class PatternDefinition
    {
        public string Name { get; set; } = "";

        public string RegexPattern { get; set; } = "";

        // Maps group name → (TableName, ColumnName).
        public Dictionary<string, (string TableName, string ColumnName)> GroupMappings { get; set; } = new();

        public string GeneratedParseMethod { get; set; } = "";
    }
}
