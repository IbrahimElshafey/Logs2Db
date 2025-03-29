WITH Totals AS (
    SELECT 
        (SELECT COUNT(*) FROM Transactions Where LogDate BETWEEN '2025-03-11 23:00:00.000' AND '2025-03-18 08:00:00.000') AS TotalTransactions,
        (SELECT COUNT(*) FROM Transactions WHERE IsAbxCompleted IS NULL OR IsAbxCompleted = 0 and LogDate BETWEEN '2025-03-11 23:00:00.000' AND '2025-03-18 08:00:00.000') AS TotalFailedTransactions,
        (SELECT COUNT(*) FROM Transactions WHERE FailureSide = 'Abx' and LogDate BETWEEN '2025-03-11 23:00:00.000' AND '2025-03-18 08:00:00.000') AS TotalGateFailures
)
SELECT 
    COUNT(*) AS Occurances,
    CASE 
         WHEN FailureReason IS NULL THEN 'غير معروف'
         WHEN FailureReason = 'BcsIneligibleDocument' THEN 'غير مؤهل للسفر'
         WHEN FailureReason = 'CompleteCrossingError' THEN 'فشل اتمام العبور'
         WHEN FailureReason = 'FaceVerificationFailure' THEN 'فشل التحقق من الوجه'
    END AS ArabicFailureReason,
    printf('%.2f%%', COUNT(*) * 100.0 / (SELECT TotalTransactions FROM Totals)) AS [% All],
    printf('%.2f%%', COUNT(*) * 100.0 / (SELECT TotalFailedTransactions FROM Totals)) AS [% All Failures],
    printf('%.2f%%', COUNT(*) * 100.0 / (SELECT TotalGateFailures FROM Totals)) AS [% ABX failures]
FROM Transactions
CROSS JOIN Totals
WHERE FailureSide = 'Abx'
GROUP BY FailureReason
ORDER BY Occurances DESC;