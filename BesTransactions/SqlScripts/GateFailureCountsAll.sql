SELECT 
      GateIP,
      COUNT(*) AS TotalTx,
      SUM(CASE WHEN IsAbxCompleted IS NULL OR IsAbxCompleted <> 1 THEN 1 ELSE 0 END) AS TotalFailures,
      SUM(CASE WHEN FailureReason = 'CompleteCrossingError' AND (IsAbxCompleted IS NULL OR IsAbxCompleted <> 1) THEN 1 ELSE 0 END) AS CompleteCrossingError,
      SUM(CASE WHEN FailureReason = 'BackedendFailure' AND (IsAbxCompleted IS NULL OR IsAbxCompleted <> 1) THEN 1 ELSE 0 END) AS BackedendFailure,
      SUM(CASE WHEN FailureReason = 'BcsIneligibleDocument' AND (IsAbxCompleted IS NULL OR IsAbxCompleted <> 1) THEN 1 ELSE 0 END) AS BcsIneligibleDocument
FROM Transactions
GROUP BY GateIP;
