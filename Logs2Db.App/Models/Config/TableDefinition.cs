using System;
using System.Collections.Generic;

namespace Logs2Db.App.Models.Config
{
    public class TableDefinition
    {
        public string Name { get; set; } = "";
        public List<SqliteColumnDefinition> Columns { get; set; } = new();
    }
}
