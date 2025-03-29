using BesTransactions.Models;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace LogParserEFCoreDemo
{
    internal class Program
    {
        private static string dpSuffix = Guid.NewGuid().ToString();

        private static readonly Regex RegexCompleteCrossingError = new Regex(
            @"^(?<Timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+\-]\d{2}:\d{2}) .* Failed to complete crossing for txId: (?<TxId>\d+)",
            RegexOptions.Compiled);
        private static readonly Regex RegexEligibilityResult = new Regex(
            @"^(?<Timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+\-]\d{2}:\d{2}) .* Eligibility check result: (?<Result>Eligible|Ineligible)\. TxId:\s*(?<TxId>\d+), EGateId:\s*(?<GateId>\d+)",
            RegexOptions.Compiled);

        private static readonly Regex RegexFailureReason = new Regex(
            @"The FailureReason for tx (?<TxId>\d+) is set to ""(?<FailureReason>[^""]+)"" gate id (?<GateId>\d+) hallId 1",
            RegexOptions.Compiled);

        private static readonly Regex RegexGateEvent = new Regex(
            @"^(?<Timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+\-]\d{2}:\d{2}) .* Gate event of type (?<Event>\S+) for EGateId (?<GateId>\d+) for tx (?<TxId>\d+)",
            RegexOptions.Compiled);

        private static readonly Regex RegexGetTransaction = new Regex(
            @"^(?<Timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+\-]\d{2}:\d{2}) .*Get FastABC\.BES\.Core\.Models\.Entities\.Transaction of \[(?<TxId>\d+)\] by \[GateUser(?<GateIP>(\d{1,3}\.){3}\d{1,3})\]",
            RegexOptions.Compiled);

        private static readonly Regex RegexNextStep = new Regex(
            @"^(?<Timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+\-]\d{2}:\d{2}) .* Next step for txId: (?<TxId>\d+) determined as ""(?<NextAction>[^""]+)""(?: GateId (?<GateId>\d+))?",
            RegexOptions.Compiled);

        private static readonly Regex RegexPushTxEvent = new Regex(
            @"^(?<Timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+\-]\d{2}:\d{2}) .*Pushing new transaction event for TxId:\s*(?<TxId>\d+), GateId:\s*(?<GateId>\d+)",
            RegexOptions.Compiled);

        private static readonly Regex RegexTransactionDoc = new Regex(
            @"TransactionID:\s+(?<TxId>\d+)\s*\|\s*DocumentNumber:\s+(?<DocNum>\S+)\s*\|\s*Nationality\s*:\s+(?<Nationality>\S+)",
            RegexOptions.Compiled);

        // Regex definitions for your specified patterns:
        private static readonly Regex RegexTransactionEvent = new Regex(
            @"^(?<Timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+\-]\d{2}:\d{2}) .* Transaction event of type ""(?<Event>[^""]+)"" gate id (?<GateId>\d+) TxId (?<TxId>\d+)",
            RegexOptions.Compiled);

        private static readonly Regex RegexTxCompleted = new Regex(
            @"^(?<Timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+\-]\d{2}:\d{2}) .*Updating transaction after completing crossing for txId:\s*(?<TxId>\d+)",
            RegexOptions.Compiled);

        private static readonly Regex RegexFaceVerification = new Regex(
            @"^(?<Timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+\-]\d{2}:\d{2}) .*GetFaceVerificationResult - Match Status: (?<Status>true|false) TxId (?<TxId>\d+) GateId (?<GateId>\d+)",
            RegexOptions.Compiled);


        private static async Task ConsumeLogsAsync(ChannelReader<BesTransactions.Models.ParsedLogMessage> reader)
        {
            try
            {
                const int BATCH_SIZE = 500;
                var eventBuffer = new List<BesTransactions.Models.TransactionEventRecord>(BATCH_SIZE);
                var upsertBuffer = new List<BesTransactions.Models.TransactionUpsertRecord>(BATCH_SIZE);

                while (await reader.WaitToReadAsync())
                {
                    while (reader.TryRead(out var parsed))
                    {
                        if (parsed.Event != null)
                            eventBuffer.Add(parsed.Event);
                        if (parsed.TxUpsert != null)
                            upsertBuffer.Add(parsed.TxUpsert);

                        if (eventBuffer.Count >= BATCH_SIZE || upsertBuffer.Count >= BATCH_SIZE)
                        {
                            await FlushBuffersToDatabase(eventBuffer, upsertBuffer);
                        }
                    }
                }

                // Final flush
                await FlushBuffersToDatabase(eventBuffer, upsertBuffer);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception in consumer: " + ex);
                throw; // re-throw or handle it
            }
        }
        public static void CancelDBBrowserProcesses()
        {
            // Get processes by the name (exclude the .exe extension)
            var processes = Process.GetProcessesByName("DB Browser for SQLite");

            foreach (var process in processes)
            {
                try
                {
                    process.Kill();
                    Console.WriteLine($"Killed process: {process.ProcessName} (ID: {process.Id})");
                }
                catch (Exception ex)
                {
                    // Handle exceptions (for example, access denied)
                    Console.WriteLine($"Could not kill process {process.ProcessName} (ID: {process.Id}): {ex.Message}");
                }
            }
        }

        private static async Task UpdateFailureReasons(LogDbContext db)
        {
            var transactionsToUpdate = await db.Transactions
                .Where(tx => (tx.IsAbxCompleted == null || tx.IsAbxCompleted == false) &&
                             (tx.FailureReason == null || tx.FailureReason == "GateIsNotClear"))
                .ToListAsync();
            Console.WriteLine($"Update {transactionsToUpdate.Count} that have faliure reason GateIsNotClear or NULL.");
            foreach (var transaction in transactionsToUpdate)
            {
                transaction.SourceFailureReason = transaction.FailureReason;

                var events = await db.TransactionEvents
                    .Where(x => x.TxId == transaction.TxId)
                    .OrderByDescending(x => x.Id)
                    .ToListAsync();

                foreach (var txEvent in events)
                {
                    switch (txEvent.Name)
                    {
                        case "EligibilityCheckError":
                        case "EligibilityCheckIneligible":
                        case "EligibilityCheckWatchlisted":
                            transaction.FailureReason = "BcsIneligibleDocument";
                            goto EndLoop;

                        case "NextStepError":
                            transaction.FailureReason = "NextStepError";
                            goto EndLoop;

                        case "CompleteCrossingError":
                            transaction.FailureReason = "CompleteCrossingError";
                            goto EndLoop;

                        case "FaceVerificationError":
                        case "FaceCompareError":
                        case "FaceNotVerified":
                            transaction.FailureReason = "FaceVerificationFailure";
                            goto EndLoop;

                        case "FaceInvalidData":
                        case "FaceNotDetected":
                        case "PassengerFaceCovered":
                        case "PassengerNotFacingCamera":
                            transaction.FailureReason = "FaceCaptureFailure";
                            goto EndLoop;

                        case "FingerprintVerificationError":
                            transaction.FailureReason = "FingerprintVerificationFailure";
                            goto EndLoop;

                        case "DocumentSmartChipReadError":
                        case "SmartCardReadError":
                            transaction.FailureReason = "DocumentReadFailure";
                            goto EndLoop;

                        case "DocumentCertificateInvalid":
                        case "DocumentInvalidData":
                        case "SmartCardCertificateInvalid":
                        case "SmartCardInvalidData":
                        case "DocumentPassiveAuthenticationFailed":
                            transaction.FailureReason = "ReaderIneligibleDocument";
                            goto EndLoop;

                        case "DocumentNotSupported":
                            transaction.FailureReason = "DocumentNotSupported";
                            goto EndLoop;

                        case "PersonDetectedOutsideExitDoor":
                            transaction.FailureReason = "PersonDetectedOutsideExitDoor";
                            goto EndLoop;

                        case "UnhandledDeviceError":
                            transaction.FailureReason = "UnhandledDeviceError";
                            goto EndLoop;

                        case "MultiplePersonsDetected":
                            transaction.FailureReason = "Tailgating";
                            goto EndLoop;

                        case "EGateDeactivated":
                            transaction.FailureReason = "EGateDeactivated";
                            goto EndLoop;

                        case "PowerFailed":
                        case "PowerShutdown":
                            transaction.FailureReason = "PowerFailed";
                            goto EndLoop;

                        case "VisionCameraBlocked":
                            transaction.FailureReason = "VisionCameraBlocked";
                            goto EndLoop;

                        case "EmergencyButtonPressed":
                        case "EmergencyModeActivated":
                            transaction.FailureReason = "EmergencyActivated";
                            goto EndLoop;

                        case "ClearanceActionTimeout":
                            transaction.FailureReason = "ClearanceActionTimeout";
                            goto EndLoop;
                    }
                }
            EndLoop:
                db.Transactions.Update(transaction);
            }
            var updatedTransactions = await db.SaveChangesAsync();
            Console.WriteLine($"{updatedTransactions} transacation updated with the correct failure reason.");
        }


        //static async Task SetReasonIfGateIsClearOrNull(List<TransactionUpsertRecord> upsertBuffer, LogDbContext db)
        //{
        //    foreach (var transactionUpsert in upsertBuffer)
        //    {
        //        if (transactionUpsert.IsAbxCompleted == null || transactionUpsert.IsAbxCompleted == false)
        //            if (transactionUpsert.FailureReason == null || transactionUpsert.FailureReason == "GateIsNotClear")
        //            {
        //                //get transaction events
        //                var events = await db.TransactionEvents
        //                    .Where(x => x.TxId == transactionUpsert.TxId)
        //                    .OrderByDescending(x => x.LogDate)
        //                    .ToListAsync();
        //                var currentTransaction = await db.Transactions.FirstOrDefaultAsync(x => x.TxId == transactionUpsert.TxId);
        //                currentTransaction.SourceFailureReason = transactionUpsert.FailureReason;
        //                //order by date desc
        //                foreach (var txEvent in events)
        //                {
        //                    switch (txEvent.Name)
        //                    {
        //                        case "EligibilityCheckError":
        //                            currentTransaction.FailureReason = "BcsIneligibleDocument";
        //                            break;
        //                        case "NextStepError":
        //                            currentTransaction.FailureReason = "NextStepError";
        //                            break;
        //                        case "CompleteCrossingError":
        //                            currentTransaction.FailureReason = "CompleteCrossingError";
        //                            break;
        //                        case "FaceVerificationError":
        //                            currentTransaction.FailureReason = "FaceVerificationFailure";
        //                            break;
        //                        case "FaceCompareError":
        //                            currentTransaction.FailureReason = "FaceVerificationFailure";
        //                            break;
        //                        case "FingerprintVerificationError":
        //                            currentTransaction.FailureReason = "FingerprintVerificationFailure";
        //                            break;
        //                        case "DocumentSmartChipReadError":
        //                            currentTransaction.FailureReason = "DocumentReadFailure";
        //                            break;
        //                        case "DocumentCertificateInvalid":
        //                            currentTransaction.FailureReason = "ReaderIneligibleDocument";
        //                            break;
        //                        case "DocumentInvalidData":
        //                            currentTransaction.FailureReason = "ReaderIneligibleDocument";
        //                            break;
        //                        case "SmartCardReadError":
        //                            currentTransaction.FailureReason = "DocumentReadFailure";
        //                            break;
        //                        case "SmartCardCertificateInvalid":
        //                            currentTransaction.FailureReason = "ReaderIneligibleDocument";
        //                            break;
        //                        case "SmartCardInvalidData":
        //                            currentTransaction.FailureReason = "ReaderIneligibleDocument";
        //                            break;
        //                        case "EligibilityCheckIneligible":
        //                            currentTransaction.FailureReason = "BcsIneligibleDocument";
        //                            break;
        //                        case "EligibilityCheckWatchlisted":
        //                            currentTransaction.FailureReason = "BcsIneligibleDocument";
        //                            break;
        //                        case "FaceInvalidData":
        //                            currentTransaction.FailureReason = "FaceCaptureFailure";
        //                            break;
        //                        case "FaceNotVerified":
        //                            currentTransaction.FailureReason = "FaceVerificationFailure";
        //                            break;
        //                        case "PersonDetectedOutsideExitDoor":
        //                            currentTransaction.FailureReason = "PersonDetectedOutsideExitDoor";
        //                            break;
        //                        case "DocumentPassiveAuthenticationFailed":
        //                            currentTransaction.FailureReason = "ReaderIneligibleDocument";
        //                            break; ;
        //                        case "FaceNotDetected":
        //                            currentTransaction.FailureReason = "FaceCaptureFailure";
        //                            break;
        //                        case "DocumentNotSupported":
        //                            currentTransaction.FailureReason = "DocumentNotSupported";
        //                            break;
        //                        case "PassengerFaceCovered":
        //                            currentTransaction.FailureReason = "FaceCaptureFailure";
        //                            break;
        //                        case "PassengerNotFacingCamera":
        //                            currentTransaction.FailureReason = "FaceCaptureFailure";
        //                            break;
        //                        case "UnhandledDeviceError":
        //                            currentTransaction.FailureReason = "UnhandledDeviceError";
        //                            break;
        //                        case "MultiplePersonsDetected":
        //                            currentTransaction.FailureReason = "Tailgating";
        //                            break;
        //                        case "EGateDeactivated":
        //                            currentTransaction.FailureReason = "EGateDeactivated";
        //                            break;
        //                        case "PowerFailed":
        //                            currentTransaction.FailureReason = "PowerFailed";
        //                            break;
        //                        case "PowerShutdown":
        //                            currentTransaction.FailureReason = "PowerFailed";
        //                            break;
        //                        case "VisionCameraBlocked":
        //                            currentTransaction.FailureReason = "VisionCameraBlocked";
        //                            break;
        //                        case "EmergencyButtonPressed":
        //                            currentTransaction.FailureReason = "EmergencyActivated";
        //                            break;
        //                        case "EmergencyModeActivated":
        //                            currentTransaction.FailureReason = "EmergencyActivated";
        //                            break;
        //                        case "ClearanceActionTimeout":
        //                            currentTransaction.FailureReason = "ClearanceActionTimeout";
        //                            break;
        //                    }
        //                }
        //                db.Transactions.Update(currentTransaction);
        //                await db.SaveChangesAsync();
        //            }
        //    }
        //}

        private static async Task FlushBuffersToDatabase(
            List<BesTransactions.Models.TransactionEventRecord> eventBuffer,
            List<BesTransactions.Models.TransactionUpsertRecord> upsertBuffer)
        {
            try
            {
                if (eventBuffer.Count == 0 && upsertBuffer.Count == 0)
                    return;

                using var db = new BesTransactions.Models.LogDbContext(dpSuffix);

                // 1) Bulk insert transaction events
                if (eventBuffer.Count > 0)
                {
                    // Either do multiple normal inserts, or use a specialized EF bulk extension
                    foreach (var e in eventBuffer)
                    {
                        db.TransactionEvents
                            .Add(
                                new BesTransactions.Models.TransactionEvent
                                {
                                    TxId = e.TxId,
                                    Name = e.Name,
                                    Type = e.Type,
                                    GateId = e.GateId ?? 0,
                                    LogDate = e.LogDate
                                });
                    }
                    await db.SaveChangesAsync();
                    eventBuffer.Clear();
                }

                // 2) Upsert transactions in some aggregated way
                if (upsertBuffer.Count > 0)
                {
                    // Instead of iterating over upsertBuffer directly, group them by TxId.
                    var grouped = upsertBuffer.GroupBy(u => u.TxId);
                    foreach (var group in grouped)
                    {
                        // Merge: pick the first non-null values if present.
                        var merged = new BesTransactions.Models.TransactionUpsertRecord(
                            TxId: group.Key,
                            GateIP: group.Select(x => x.GateIP).FirstOrDefault(g => !string.IsNullOrEmpty(g)),
                            FailureReason: group.Select(x => x.FailureReason)
                                .FirstOrDefault(r => !string.IsNullOrEmpty(r)),
                            GateId: group.Select(x => x.GateId).FirstOrDefault(x => x.HasValue),
                            DocumentNumber: group.Select(x => x.DocumentNumber)
                                .FirstOrDefault(d => !string.IsNullOrEmpty(d)),
                            Nationality: group.Select(x => x.Nationality)
                                .FirstOrDefault(d => !string.IsNullOrEmpty(d)),
                            FirstSeenDate: group.Select(x => x.FirstSeenDate).FirstOrDefault(x => x.HasValue),
                            IsAbxCompleted: group.Select(x => x.IsAbxCompleted).FirstOrDefault(x => x.HasValue),
                            LogFilePath: group.Select(x => x.LogFilePath).FirstOrDefault(d => !string.IsNullOrEmpty(d)),
                            // For IsAbxEligible, pick the first value that has a value (true or false)
                            IsAbxEligible: group.Select(x => x.IsAbxEligible).FirstOrDefault(v => v.HasValue),
                            IsAbxFaceVerified: group.Select(x => x.IsAbxFaceVerified).FirstOrDefault(v => v.HasValue),
                            GateEventsCount: group.Select(x => x.GateEventsCount).Where(c => c.HasValue).Sum() ?? 0,
                            TransactionEventsCount: group.Select(x => x.TransactionEventsCount).Where(c => c.HasValue).Sum() ??
                                0);

                        UpsertTransaction(
                            db,
                            merged.TxId,
                            merged.GateIP,
                            merged.FailureReason,
                            merged.GateId,
                            merged.DocumentNumber,
                            merged.FirstSeenDate,
                            merged.IsAbxCompleted,
                            merged.LogFilePath,
                            merged.IsAbxEligible,
                            merged.IsAbxFaceVerified,
                            merged.GateEventsCount,
                            merged.TransactionEventsCount,
                            merged.Nationality);
                    }

                    await db.SaveChangesAsync();
                    upsertBuffer.Clear();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                throw;
            }
        }


        static async Task Main(string[] args)
        {
            try
            {
                CancelDBBrowserProcesses();
                // Parse command-line arguments in PowerShell style:
                // e.g. -LogFolder "C:\logs\bes" -DBPrefix "MS01"
                var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i].StartsWith("-") && i + 1 < args.Length)
                    {
                        // Remove the leading '-' and use the next argument as the value.
                        string key = args[i].TrimStart('-');
                        string value = args[i + 1];
                        arguments[key] = value;
                        i++; // Skip next argument since it is the value.
                    }
                }

                // Use the parsed arguments or defaults.
                var logsRoot = arguments.ContainsKey("LogFolder") ? arguments["LogFolder"] : "logs";
                dpSuffix = arguments.ContainsKey("DBSuffix") ? arguments["DBSuffix"] : dpSuffix;

                var logFiles = Directory.GetFiles(logsRoot, "*.log", SearchOption.AllDirectories);

                int totalFiles = logFiles.Length;
                if (totalFiles == 0)
                {
                    Console.WriteLine($"No .log files found in folder: {logsRoot}");
                    return;
                }
                Console.CursorVisible = false; // Hide the cursor for a cleaner UI
                                               // Ensure our database is created and migrations applied (if any).
                using (var db = new BesTransactions.Models.LogDbContext(dpSuffix))
                {
                    Console.WriteLine("Ensuring database is deleted first...");
                    await db.Database.EnsureDeletedAsync();
                    Console.WriteLine("Ensuring empty database is created...");
                    db.Database.EnsureCreated();
                }

                var channel = Channel.CreateBounded<BesTransactions.Models.ParsedLogMessage>(
                    new BoundedChannelOptions(capacity: 10_000) { FullMode = BoundedChannelFullMode.Wait });

                // Start a single consumer that will read from the channel and do DB inserts
                var consumerTask = ConsumeLogsAsync(channel.Reader);
                var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 8 };
                int filesProcessed = 0;
                await Task.Run(
                    () =>
                    {
                        Parallel.ForEach(
                            logFiles,
                            parallelOptions,
                            filePath =>
                            {
                                // Each file is handled in parallel
                                ProcessLogFile(filePath, channel.Writer).GetAwaiter().GetResult();

                                //// Update the progress bar to reflect that we've finished i+1 files
                                //int barLength = (int)(Console.WindowWidth * 0.7) - 10; // 70% of window width
                                //DrawProgressBar(i + 1, totalFiles, barLength);

                                // Atomically increment file count
                                int done = Interlocked.Increment(ref filesProcessed);
                                // (Optional) Print progress whenever a file completes
                                // Be mindful not to do heavy console output from many threads
                                Console.WriteLine(
                                    $"Finished processing file {done}/{totalFiles}: {Path.GetFileName(filePath)}");
                            });

                        // Signal we're done producing
                        channel.Writer.Complete();
                    });

                await consumerTask;

                Console.WriteLine($"Processing all files in folder [{logsRoot}] completed!");
                Console.WriteLine("Update some failure reasons with correct one [Wait for it]....");
                using (var db = new LogDbContext(dpSuffix))
                {
                    await UpdateFailureReasons(db);
                    await UpdateBackendFailureReason(db);
                    await SetFailureSide(db);
                }
                Console.WriteLine($"All Is DONE and database [BesTransactions-{dpSuffix}.db] is ready :)");
                Task.WaitAny(Task.Delay(30000), new Task(() => Console.ReadLine()));
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error occured");
                Console.Write(ex);
                Console.ReadLine();
            }
        }
        async static Task SetFailureSide(LogDbContext db)
        {
            int updatedSourceFailureReason = await db.Database.ExecuteSqlRawAsync(
                @"
                UPDATE Transactions
                SET FailureSide =
                    CASE
                        WHEN FailureReason IN (
                            'CompleteCrossingError',
                            'BcsIneligibleDocument',
                            'FingerprintVerificationFailure',
                            'NextStepError',
                            'FaceVerificationFailure'
                        ) THEN 'Abx'
                        ELSE 'Gate'
                    END
                WHERE IsAbxCompleted IS NULL OR IsAbxCompleted = 0;
                ");
            Console.WriteLine($"Rows updated to SourceFailureReason = 'BackedendFailure': {updatedSourceFailureReason}");
        }

        private static async Task UpdateBackendFailureReason(LogDbContext context)
        {
            // Update FailureReason to 'BcsIneligibleDocument' if an event 'EligibilityCheckError' exists for the transaction.
            int updatedSourceFailureReason = await context.Database.ExecuteSqlRawAsync(
                @"UPDATE Transactions
                    SET SourceFailureReason = 'BackedendFailure'
                    WHERE FailureReason = 'BackedendFailure'
                    ");
            Console.WriteLine($"Rows updated to SourceFailureReason = 'BackedendFailure': {updatedSourceFailureReason}");

            // Update FailureReason to 'BcsIneligibleDocument' if an event 'EligibilityCheckError' exists for the transaction.
            int updatedBcsIneligible = await context.Database.ExecuteSqlRawAsync(
                @"UPDATE Transactions
                    SET FailureReason = 'BcsIneligibleDocument'
                    WHERE FailureReason = 'BackedendFailure'
                      AND (IsAbxCompleted IS NULL OR IsAbxCompleted <> 0)
                      AND EXISTS (
                        SELECT 1 
                        FROM TransactionEvents TE
                        WHERE TE.TxId = Transactions.TxId
                          AND TE.Name = 'EligibilityCheckError'
                      );
                    ");
            Console.WriteLine($"Rows updated to FailureReason = 'BcsIneligibleDocument': {updatedBcsIneligible}");

            // Update FailureReason to 'FaceVerificationError' if an event 'FaceVerificationError' exists for the transaction.
            int updatedFaceVerification = await context.Database.ExecuteSqlRawAsync(
                @"UPDATE Transactions
                SET FailureReason = 'FaceVerificationFailure'
                WHERE FailureReason = 'BackedendFailure'
                  AND (IsAbxCompleted IS NULL OR IsAbxCompleted <> 1)
                  AND EXISTS (
                    SELECT 1 
                    FROM TransactionEvents TE
                    WHERE TE.TxId = Transactions.TxId
                      AND TE.Name = 'FaceVerificationError'
                  );
                ");
            Console.WriteLine($"Rows updated to FailureReason = 'FaceVerificationFailure': {updatedFaceVerification}");

            // Update remaining 'BackedendFailure' to 'CommunicationError'
            int updatedBackendFailure = await context.Database.ExecuteSqlRawAsync(
                @"UPDATE Transactions
              SET FailureReason = 'CommunicationError'
              WHERE FailureReason = 'BackedendFailure'");
            Console.WriteLine($"Rows updated to 'CommunicationError': {updatedBackendFailure}");
        }

        private static async Task ProcessLogFile(string filePath, ChannelWriter<BesTransactions.Models.ParsedLogMessage> writer)
        {
            // Example: an async, streaming approach
            // using FileStream + StreamReader
            using var fs = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                useAsync: true);
            using var reader = new StreamReader(fs);

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                // Attempt pattern matches
                // 1) Transaction event
                var match = RegexTransactionEvent.Match(line);
                if (match.Success)
                {
                    var timestampStr = match.Groups["Timestamp"].Value;
                    var logDate = DateTimeOffset.Parse(timestampStr).UtcDateTime;
                    var eventName = match.Groups["Event"].Value;
                    var gateId = int.Parse(match.Groups["GateId"].Value);
                    var txId = int.Parse(match.Groups["TxId"].Value);

                    // Write to channel
                    await writer.WriteAsync(
                        new BesTransactions.Models.ParsedLogMessage(
                            Event: new BesTransactions.Models.TransactionEventRecord(txId, eventName, "Transaction", gateId, logDate),
                            TxUpsert: new BesTransactions.Models.TransactionUpsertRecord(
                                TxId: txId,
                                GateIP: null,
                                FailureReason: null,
                                GateId: gateId,
                                DocumentNumber: null,
                                Nationality: null,
                                FirstSeenDate: logDate,
                                IsAbxCompleted: null,
                                LogFilePath: null,
                                IsAbxEligible: null,
                                IsAbxFaceVerified: null,
                                GateEventsCount: null,
                                TransactionEventsCount: 1)));
                    continue;
                }

                //push event regex
                match = RegexPushTxEvent.Match(line);
                if (match.Success)
                {
                    var timestampStr = match.Groups["Timestamp"].Value;
                    var logDate = DateTimeOffset.Parse(timestampStr).UtcDateTime;
                    var txId = int.Parse(match.Groups["TxId"].Value);
                    var gateId = int.Parse(match.Groups["GateId"].Value);

                    await writer.WriteAsync(
                        new BesTransactions.Models.ParsedLogMessage(
                            Event: new BesTransactions.Models.TransactionEventRecord(
                                txId,
                                "PushTxEvent",
                                "Custom",
                                gateId,
                                logDate),
                            TxUpsert: null));
                    continue;
                }

                // 2) Get Transaction
                match = RegexGetTransaction.Match(line);
                if (match.Success)
                {
                    var timestampStr = match.Groups["Timestamp"].Value;
                    var logDate = DateTimeOffset.Parse(timestampStr).UtcDateTime;
                    var txId = int.Parse(match.Groups["TxId"].Value);
                    var gateIP = match.Groups["GateIP"].Value;

                    await writer.WriteAsync(
                        new BesTransactions.Models.ParsedLogMessage(
                            Event: null,
                            TxUpsert: new BesTransactions.Models.TransactionUpsertRecord(
                                TxId: txId,
                                GateIP: gateIP,
                                FailureReason: null,
                                GateId: null,
                                DocumentNumber: null,
                                Nationality: null,
                                FirstSeenDate: logDate,
                                IsAbxCompleted: null,
                                LogFilePath: filePath,
                                IsAbxEligible: null,
                                IsAbxFaceVerified: null,
                                GateEventsCount: null,
                                TransactionEventsCount: null)));
                    continue;
                }

                //Eligibility Result
                match = RegexEligibilityResult.Match(line);
                if (match.Success)
                {
                    var timestampStr = match.Groups["Timestamp"].Value;
                    var logDate = DateTimeOffset.Parse(timestampStr).UtcDateTime;

                    var result = match.Groups["Result"].Value; // "Eligible" or "Ineligible"
                    var txId = int.Parse(match.Groups["TxId"].Value);
                    // If you want the gate ID too:
                    var gateId = int.Parse(match.Groups["GateId"].Value);

                    // Convert "Eligible"/"Ineligible" to a bool
                    bool isEligibleFlag = (result == "Eligible");

                    // Send it to your consumer pipeline with an upsert
                    await writer.WriteAsync(
                        new BesTransactions.Models.ParsedLogMessage(
                            Event: null,
                            TxUpsert: new BesTransactions.Models.TransactionUpsertRecord(
                                TxId: txId,
                                GateIP: null,
                                FailureReason: null,
                                GateId: gateId,
                                DocumentNumber: null,
                                Nationality: null,
                                FirstSeenDate: logDate,
                                IsAbxCompleted: null,
                                LogFilePath: filePath,
                                IsAbxEligible: isEligibleFlag,
                                IsAbxFaceVerified: null,
                                GateEventsCount: null,
                                TransactionEventsCount: null)));

                    continue;
                }

                // 3) Gate event
                match = RegexGateEvent.Match(line);
                if (match.Success)
                {
                    var timestampStr = match.Groups["Timestamp"].Value;
                    var logDate = DateTimeOffset.Parse(timestampStr).UtcDateTime;
                    var eventName = match.Groups["Event"].Value;
                    var gateId = int.Parse(match.Groups["GateId"].Value);
                    var txId = int.Parse(match.Groups["TxId"].Value);

                    // For the event
                    await writer.WriteAsync(
                        new BesTransactions.Models.ParsedLogMessage(
                            Event: new BesTransactions.Models.TransactionEventRecord(txId, eventName, "Gate", gateId, logDate),
                            TxUpsert: null));

                    // Possibly Upsert if it’s a completion event
                    bool? isCompleted = null;
                    bool? isFaceVerified = null;
                    switch (eventName)
                    {
                        case "ExitDoorOpenedOutwards":
                        case "ClearanceComplete":
                            isCompleted = true;
                            break;
                        case "FaceVerified":
                            isFaceVerified = true;
                            break;
                        case "FaceVerificationFailure":
                            isFaceVerified = false;
                            break;
                    }
                    await writer.WriteAsync(
                        new BesTransactions.Models.ParsedLogMessage(
                            Event: null,
                            TxUpsert: new BesTransactions.Models.TransactionUpsertRecord(
                                TxId: txId,
                                GateIP: null,
                                FailureReason: null,
                                GateId: gateId,
                                DocumentNumber: null,
                                Nationality: null,
                                FirstSeenDate: logDate,
                                IsAbxCompleted: isCompleted,
                                LogFilePath: null,
                                IsAbxEligible: null,
                                IsAbxFaceVerified: isFaceVerified,
                                GateEventsCount: 1,           // increment by 1
                                TransactionEventsCount: null)));
                    continue;
                }

                // 4) Next step
                match = RegexNextStep.Match(line);
                if (match.Success)
                {
                    var timestampStr = match.Groups["Timestamp"].Value;
                    var logDate = DateTimeOffset.Parse(timestampStr).UtcDateTime;
                    var txId = int.Parse(match.Groups["TxId"].Value);
                    var nextAction = match.Groups["NextAction"].Value;
                    int? gateId = null;
                    if (match.Groups["GateId"].Success)
                    {
                        gateId = int.Parse(match.Groups["GateId"].Value);
                    }

                    await writer.WriteAsync(
                        new BesTransactions.Models.ParsedLogMessage(
                            Event: new BesTransactions.Models.TransactionEventRecord(txId, $"NextAction:{nextAction}", "Abx", gateId, logDate),
                            TxUpsert: null));
                    continue;
                }

                // 5) Failure Reason
                match = RegexFailureReason.Match(line);
                if (match.Success)
                {
                    var txId = int.Parse(match.Groups["TxId"].Value);
                    var failureReason = match.Groups["FailureReason"].Value;
                    var gateId = int.Parse(match.Groups["GateId"].Value);

                    await writer.WriteAsync(
                        new BesTransactions.Models.ParsedLogMessage(
                            Event: null,
                            TxUpsert: new BesTransactions.Models.TransactionUpsertRecord(
                                TxId: txId,
                                GateIP: null,
                                FailureReason: failureReason,
                                GateId: gateId,
                                DocumentNumber: null,
                                Nationality: null,
                                FirstSeenDate: null,
                                IsAbxCompleted: null,
                                LogFilePath: null,
                                IsAbxEligible: null,
                                IsAbxFaceVerified: null,
                                GateEventsCount: null,
                                TransactionEventsCount: null)));
                    continue;
                }

                // 7) Face verification pattern
                match = RegexFaceVerification.Match(line);
                if (match.Success)
                {
                    var timestampStr = match.Groups["Timestamp"].Value;
                    var logDate = DateTimeOffset.Parse(timestampStr).UtcDateTime;
                    var statusStr = match.Groups["Status"].Value;
                    bool faceVerified = statusStr.Equals("true", StringComparison.OrdinalIgnoreCase);
                    var txId = int.Parse(match.Groups["TxId"].Value);
                    var gateId = int.Parse(match.Groups["GateId"].Value);

                    await writer.WriteAsync(
                        new BesTransactions.Models.ParsedLogMessage(
                            Event: new BesTransactions.Models.TransactionEventRecord(txId, "FaceVerification", "Abx", gateId, logDate),
                            TxUpsert: new BesTransactions.Models.TransactionUpsertRecord(
                                TxId: txId,
                                GateIP: null,
                                FailureReason: null,
                                GateId: gateId,
                                DocumentNumber: null,
                                Nationality: null,
                                FirstSeenDate: logDate,
                                LogFilePath: filePath,
                                IsAbxEligible: null,
                                IsAbxCompleted: null,
                                IsAbxFaceVerified: faceVerified,
                                GateEventsCount: null,
                                TransactionEventsCount: null
                            )));
                    continue;
                }

                // 6) Transaction & DocumentNumber
                match = RegexTransactionDoc.Match(line);
                if (match.Success)
                {
                    var txId = int.Parse(match.Groups["TxId"].Value);
                    var docNum = match.Groups["DocNum"].Value;
                    var nationality = match.Groups["Nationality"].Value;

                    await writer.WriteAsync(
                        new BesTransactions.Models.ParsedLogMessage(
                            Event: null,
                            TxUpsert: new BesTransactions.Models.TransactionUpsertRecord(
                                TxId: txId,
                                GateIP: null,
                                FailureReason: null,
                                GateId: null,
                                DocumentNumber: docNum,
                                Nationality: nationality,
                                FirstSeenDate: null,
                                IsAbxCompleted: null,
                                LogFilePath: null,
                                IsAbxEligible: null,
                                IsAbxFaceVerified: null,
                                GateEventsCount: null,
                                TransactionEventsCount: null)));
                    continue;
                }

                //abx complete fails
                // abx complete fails
                match = RegexCompleteCrossingError.Match(line);
                if (match.Success)
                {
                    var timestampStr = match.Groups["Timestamp"].Value;
                    var logDate = DateTimeOffset.Parse(timestampStr).UtcDateTime;
                    var txId = int.Parse(match.Groups["TxId"].Value);

                    await writer.WriteAsync(
                        new BesTransactions.Models.ParsedLogMessage(
                            Event: new BesTransactions.Models.TransactionEventRecord(
                                TxId: txId,
                                Name: "CompleteCrossingError",
                                Type: "Abx",
                                GateId: null,
                                LogDate: logDate),
                            TxUpsert: new BesTransactions.Models.TransactionUpsertRecord(
                                TxId: txId,
                                GateIP: null,
                                FailureReason: "CompleteCrossingError",
                                GateId: null,
                                DocumentNumber: null,
                                Nationality: null,
                                FirstSeenDate: logDate,
                                IsAbxCompleted: false,
                                LogFilePath: null,
                                IsAbxEligible: null,
                                IsAbxFaceVerified: true,
                                GateEventsCount: null,
                                TransactionEventsCount: null)));
                    continue;
                }


                //abx complete
                //abx complete fails
                // 7) Transaction Completed Pattern: Update IsAbxCompleted = true
                match = RegexTxCompleted.Match(line);
                if (match.Success)
                {
                    var timestampStr = match.Groups["Timestamp"].Value;
                    var logDate = DateTimeOffset.Parse(timestampStr).UtcDateTime;
                    var txId = int.Parse(match.Groups["TxId"].Value);

                    await writer.WriteAsync(
                        new BesTransactions.Models.ParsedLogMessage(
                            Event: null,
                            TxUpsert: new BesTransactions.Models.TransactionUpsertRecord(
                                TxId: txId,
                                GateIP: null,
                                FailureReason: null,
                                GateId: null,
                                DocumentNumber: null,
                                Nationality: null,
                                FirstSeenDate: logDate,
                                IsAbxCompleted: true,
                                LogFilePath: filePath,
                                IsAbxEligible: null,
                                IsAbxFaceVerified: true,
                                GateEventsCount: null,
                                TransactionEventsCount: null)));
                    continue;
                }
            }
        }

        ///// <summary>
        ///// Writes a progress message every N lines.
        ///// </summary>
        //private static void PrintLineProgress(int currentIndex, int totalLines, string filePath, int interval)
        //{
        //    if(currentIndex > 0 && currentIndex % interval == 0)
        //    {
        //        Console.WriteLine(
        //            $"  Processed {currentIndex} of {totalLines} lines in {Path.GetFileName(filePath)}...");
        //    }
        //}

        private static void UpsertTransaction(
            BesTransactions.Models.LogDbContext db,
            int txId,
            string? gateIP,
            string? failureReason,
            int? gateId,
            string? documentNumber = null,
            DateTime? firstSeenDate = null,
            bool? isCompleted = null,
            string? logFilePath = null,
            bool? isEligible = null,
            bool? isAbxFaceVerified = null,
            int? gateEventsCount = null,
            int? transactionEventsCount = null,
            string? nationality = null)
        {
            var existing = db.Transactions.Local.FirstOrDefault(t => t.TxId == txId);
            if (existing == null)
            {
                existing = db.Transactions.SingleOrDefault(t => t.TxId == txId);
            }
            if (existing == null)
            {
                // brand new
                var newTx = new BesTransactions.Models.Transaction
                {
                    TxId = txId,
                    GateIP = gateIP,
                    FailureReason = failureReason,
                    GateId = gateId,
                    DocumentNumber = documentNumber,
                    LogDate = firstSeenDate ?? DateTime.UtcNow,
                    LogFile = logFilePath,
                    IsAbxEligible = isEligible,
                    IsAbxCompleted = isCompleted,
                    IsAbxFaceVerified = isAbxFaceVerified,
                    GateEventsCount = gateEventsCount,
                    TransactionEventsCount = transactionEventsCount,
                    Nationality = nationality,
                };
                db.Transactions.Add(newTx);
            }
            else
            {
                // Update existing, but only if we have new data
                if (string.IsNullOrEmpty(existing.GateIP) && !string.IsNullOrEmpty(gateIP))
                    existing.GateIP = gateIP;

                if (string.IsNullOrEmpty(existing.FailureReason) && !string.IsNullOrEmpty(failureReason))
                    existing.FailureReason = failureReason;

                if (!existing.GateId.HasValue && gateId.HasValue)
                    existing.GateId = gateId.Value;

                if (string.IsNullOrEmpty(existing.DocumentNumber) && !string.IsNullOrEmpty(documentNumber))
                    existing.DocumentNumber = documentNumber;

                if (string.IsNullOrEmpty(existing.Nationality) && !string.IsNullOrEmpty(nationality))
                    existing.Nationality = nationality;

                if (existing.LogDate == default && firstSeenDate.HasValue)
                    existing.LogDate = firstSeenDate.Value;

                if (isCompleted.HasValue)
                {
                    existing.IsAbxCompleted = isCompleted.Value;
                    existing.IsAbxFaceVerified = true;
                }

                if (string.IsNullOrEmpty(existing.LogFile) && !string.IsNullOrEmpty(logFilePath))
                    existing.LogFile = logFilePath;

                if (isAbxFaceVerified.HasValue)
                    existing.IsAbxFaceVerified = isAbxFaceVerified.Value;

                if (isEligible.HasValue)
                {
                    existing.IsAbxEligible = isEligible.Value;
                }

                // Accumulate GateEventsCount
                if (gateEventsCount.HasValue)
                    existing.GateEventsCount = (existing.GateEventsCount ?? 0) + gateEventsCount.Value;
                // Accumulate TransactionEventsCount
                if (transactionEventsCount.HasValue)
                    existing.TransactionEventsCount = (existing.TransactionEventsCount ?? 0) +
                        transactionEventsCount.Value;
            }
        }
    }
}
