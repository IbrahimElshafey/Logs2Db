using System;

namespace Logs2Db.App.Models.Config
{
    public class Script
    {
        public string Name { get; set; } = "";
        public string Sql { get; set; } = "";
        public int Order { get; set; }
    }
}
