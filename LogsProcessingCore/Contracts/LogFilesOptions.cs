﻿// See https://aka.ms/new-console-template for more information
namespace LogsProcessingCore.Contracts;
public class LogFilesOptions
{
    public string LogFilesFolder { get; set; }
    public string SearchPattern { get; set; }
    public EnumerationOptions EnumerationOptions { get; set; }
    public string RegexPathFilter { get; set; }
}
