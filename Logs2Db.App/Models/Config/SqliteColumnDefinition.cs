using System;

namespace Logs2Db.App.Models.Config
{
    public class SqliteColumnDefinition
    {
        public string Name { get; set; } = "";
        public SqliteDataType DataType { get; set; } = SqliteDataType.Text;
    }
}
