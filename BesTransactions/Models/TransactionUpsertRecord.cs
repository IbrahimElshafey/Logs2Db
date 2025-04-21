namespace BesTransactions.Models
{
    internal record TransactionUpsertRecord(
        int TxId,
        string? GateIP,
        string? FailureReason,
        int? GateId,
        string? DocumentNumber,
        string? Nationality,
        DateTime? FirstSeenDate,
        bool? IsAbxCompleted,
        string? LogFilePath,
        bool? IsAbxEligible,
        bool? IsAbxFaceVerified,
        int? GateEventsCount, 
        int? TransactionEventsCount 
    );

}
