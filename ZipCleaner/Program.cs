using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace ZipArchiveCleaner;

class Program
{
    static async Task<int> Main(string[] args)
    {
        args = [@"G:\~temp\DSP-ErrorCode-Round2\Server 1963 Logs - Copy.zip", "2024-12-01"];
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: ZipArchiveCleaner <zipPath> <cutoffDate>\nExample: ZipArchiveCleaner myfiles.zip 2025-01-01");
            return 1;
        }

        string zipPath = args[0];
        if (!File.Exists(zipPath))
        {
            Console.Error.WriteLine($"Error: File not found: {zipPath}");
            return 1;
        }

        if (!DateTime.TryParse(args[1], out DateTime cutoffDate))
        {
            Console.Error.WriteLine($"Error: Invalid date format: {args[1]}");
            return 1;
        }

        try
        {
            // Open the zip archive in Update mode with asynchronous FileStream
            await using var zipStream = new FileStream(
                zipPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);

            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Update);

            // Find entries older than the cutoff date
            var toRemoveFiles = archive.Entries
                .Where(e => !e.FullName.EndsWith("/") && e.LastWriteTime.Date < cutoffDate.Date)
                .ToList();

            Console.WriteLine($"Found {toRemoveFiles.Count} file entries older than {cutoffDate:yyyy-MM-dd}.");

            // Delete old file entries
            foreach (var entry in toRemoveFiles)
            {
                Console.WriteLine($"Deleting file: {entry.FullName} (LastWrite: {entry.LastWriteTime:yyyy-MM-dd})");
                await Task.Run(() => entry.Delete()).ConfigureAwait(false);
            }

            // Find empty directory entries (explicit folders) and delete them
            var dirEntries = archive.Entries
                .Where(e => e.FullName.EndsWith("/"))
                .ToList();

            foreach (var dirEntry in dirEntries)
            {
                // Check if any remaining entry resides under this directory
                bool hasChild = archive.Entries.Any(e => e.FullName != dirEntry.FullName && e.FullName.StartsWith(dirEntry.FullName, StringComparison.Ordinal));
                if (!hasChild)
                {
                    Console.WriteLine($"Deleting empty folder entry: {dirEntry.FullName}");
                    await Task.Run(() => dirEntry.Delete()).ConfigureAwait(false);
                }
            }

            Console.WriteLine("Cleanup complete.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"An error occurred: {ex.Message}");
            return 1;
        }
    }
}