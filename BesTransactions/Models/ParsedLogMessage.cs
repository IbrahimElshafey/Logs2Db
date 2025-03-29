using LogParserEFCoreDemo;

namespace BesTransactions.Models
{
    internal record ParsedLogMessage(Models.TransactionEventRecord? Event, TransactionUpsertRecord? TxUpsert);
}
