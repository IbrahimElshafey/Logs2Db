// See https://aka.ms/new-console-template for more information
using CCSS_DSP_LogsParser;

using (var db = new LogsDbContext())
{
    Console.WriteLine("Ensuring database is deleted first...");
    await db.Database.EnsureDeletedAsync();
    Console.WriteLine("Ensuring empty database is created...");
    await db.Database.EnsureCreatedAsync();
}
//var ccssLogProcessor = new Log4NetLogs2Db(@"G:\~temp\DSP-ErrorCode-Round2\CCSS", "CCSS");
var ccssLogProcessor = new Log4NetLogs2Db(@"V:\CCSS", "CCSS");
await ccssLogProcessor.ParseLogs();

//ccssLogProcessor = new Log4NetLogs2Db(@"G:\~temp\DSP-ErrorCode-Round2\Server 1963 Logs - Copy", "DSP");
//await ccssLogProcessor.ParseLogs();
//ccssLogProcessor = new Log4NetLogs2Db(@"G:\~temp\DSP-ErrorCode-Round2\Server 1946 Logs - Copy", "DSP");
//await ccssLogProcessor.ParseLogs();

ccssLogProcessor = new Log4NetLogs2Db(@"V:\DSP-All-Logs", "DSP");
await ccssLogProcessor.ParseLogs();
Console.WriteLine("Processing finished");
Console.ReadLine();
