SELECT 
    T.TxId,
    T.GateIP,
    T.FailureReason,
    T.DocumentNumber,
    T.Date,
    COUNT(TE.Id) AS eventsCount
FROM Transactions T
LEFT JOIN TransactionEvents TE ON T.TxId = TE.TxId
GROUP BY T.TxId
HAVING COUNT(TE.Id) = 3;
