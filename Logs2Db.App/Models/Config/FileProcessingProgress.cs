using System;

namespace Logs2Db.App.Models.Config
{
    public class FileProcessingProgress
    {
        public string FileRelativePath { get; set; } = "";
        public bool IsProcessed { get; set; }
        public DateTime ProcessingDate { get; set; }
    }
}
