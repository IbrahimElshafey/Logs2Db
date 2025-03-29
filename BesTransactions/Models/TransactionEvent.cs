namespace BesTransactions.Models
{
    public class TransactionEvent
    {
        public int Id { get; set; }
        public int TxId { get; set; }
        public string? Name { get; set; }
        public string? Type { get; set; }
        public int GateId { get; set; }
        public DateTime LogDate { get; set; }
    }
}
