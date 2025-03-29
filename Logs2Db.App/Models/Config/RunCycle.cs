using System;
using System.Collections.Generic;

namespace Logs2Db.App.Models.Config
{
    public class RunCycle
    {
        public string FolderPath { get; set; } = "";
        public int Progress { get; set; }
        public List<FileProcessingProgress> FilesProgress { get; set; } = new();
    }
}
