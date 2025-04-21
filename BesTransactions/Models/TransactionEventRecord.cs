namespace BesTransactions.Models
{
    internal record TransactionEventRecord(int TxId, string Name, string Type, int? GateId, DateTime LogDate);
}
