SELECT 
      GateIP,
      SUM(CASE WHEN FailureReason = 'CompleteCrossingError' AND (IsAbxCompleted IS NULL OR IsAbxCompleted <> 1) THEN 1 ELSE 0 END) AS CompleteCrossingError,
      SUM(CASE WHEN FailureReason = 'BcsIneligibleDocument' AND (IsAbxCompleted IS NULL OR IsAbxCompleted <> 1) THEN 1 ELSE 0 END) AS BcsIneligibleDocument,
      SUM(CASE WHEN FailureReason = 'FaceVerificationFailure' AND (IsAbxCompleted IS NULL OR IsAbxCompleted <> 1) THEN 1 ELSE 0 END) AS FaceVerificationFailure
FROM Transactions
WHERE LogDate BETWEEN '2025-03-17 23:00:00.000' AND '2025-03-18 08:00:00.000'
GROUP BY GateIP;
