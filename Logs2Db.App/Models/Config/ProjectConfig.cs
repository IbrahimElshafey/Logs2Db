using System.Collections.Generic;

namespace Logs2Db.App.Models.Config
{
    public class ProjectConfig
    {
        public List<PatternDefinition> Patterns { get; set; } = new();
        public List<TableDefinition> Tables { get; set; } = new();
        public List<Script> PostProcessingScripts { get; set; } = new();
        public List<RunCycle> RunCycles { get; set; } = new();
    }
}
