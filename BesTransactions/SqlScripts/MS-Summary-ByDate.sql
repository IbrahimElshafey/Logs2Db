--- same per date 
SELECT 
    date(LogDate) AS 'اليوم',
    SUM(CASE WHEN IsAbxCompleted = 1 THEN 1 ELSE 0 END) AS "العمليات الناجحة",
    SUM(CASE 
          WHEN (IsAbxCompleted IS NULL OR IsAbxCompleted = 0)
           AND FailureReason IN (
                'CompleteCrossingError',
                'BcsIneligibleDocument',
                'FaceVerificationFailure'
          )
          THEN 1 ELSE 0 END) AS "العمليات المرفوضه من النظام الخلفي",
    SUM(CASE 
          WHEN (IsAbxCompleted IS NULL OR IsAbxCompleted = 0)
           AND FailureReason IN (
                'DocumentReadFailure',
                'ReaderIneligibleDocument',
                'GateIsNotClear',
                'PersonDetectedOutsideExitDoor',
                'ClearanceActionTimeout',
                'Tailgating',
                'VisionCameraBlocked',
                'FaceCaptureFailure',
                'Unknown',
                'DocumentNotSupported',
                'EmergencyActivated',
                'CommunicationError'
          )
          THEN 1 ELSE 0 END) AS "العمليات المرفوضه من البوابة",
    SUM(CASE WHEN (IsAbxCompleted IS NULL OR IsAbxCompleted = 0) THEN 1 ELSE 0 END) AS "إجمالي العمليات المرفوضة",
    COUNT(*) AS "الاجمالي"
FROM Transactions
WHERE LogDate BETWEEN '2025-03-11 23:00:00.000' AND '2025-03-18 08:00:00.000'
GROUP BY date(LogDate)
ORDER BY date(LogDate);