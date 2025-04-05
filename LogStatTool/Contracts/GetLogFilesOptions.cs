// See https://aka.ms/new-console-template for more information

public class GetLogFilesOptions
{
    public string LogFilesFolder { get; set; }
    public string SearchPattern { get; set; }
    public EnumerationOptions EnumerationOptions { get; set; }
    public string PathFilter { get; set; }
}
