namespace BesTransactions.Models
{
    public class Transaction
    {
        public int TxId { get; set; }
        public string? GateIP { get; set; }
        public string? FailureReason { get; set; }
        public string? FailureSide { get; set; }
        public int? GateId { get; set; }
        public string? DocumentNumber { get; set; }
        public string? Nationality { get; set; }
        public DateTime LogDate { get; set; }
        public string? LogFile { get; set; }
        public bool? IsAbxEligible { get; set; }
        public bool? IsAbxFaceVerified { get; set; }
        public bool? IsAbxCompleted { get; set; }
        public string? SourceFailureReason { get; set; } = "Same As FailureReason";
        public int? GateEventsCount { get; set; }
        public int? TransactionEventsCount { get; set; }

    }
}
