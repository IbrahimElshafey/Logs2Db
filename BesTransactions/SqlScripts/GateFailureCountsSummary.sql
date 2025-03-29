SELECT 
    GateIP,
    COUNT(*) AS TotalTransactions,
    SUM(CASE WHEN IsAbxCompleted IS NULL OR IsAbxCompleted = 0 THEN 1 ELSE 0 END) AS TotalFailures,
    SUM(CASE WHEN IsAbxCompleted = 1 THEN 1 ELSE 0 END) AS TotalSuccess,
    SUM(CASE 
          WHEN (IsAbxCompleted IS NULL OR IsAbxCompleted = 0)
           AND (FailureReason IS NULL 
                OR FailureReason IN (
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
                ))
          THEN 1 ELSE 0 END) AS TotalFailedGate,
    SUM(CASE 
          WHEN (IsAbxCompleted IS NULL OR IsAbxCompleted = 0)
           AND FailureReason IN (
                'BackedendFailure',
                'CompleteCrossingError',
                'BcsIneligibleDocument',
                'FaceVerificationFailure'
          )
          THEN 1 ELSE 0 END) AS TotalFailedAbx,
    SUM(CASE 
          WHEN (FailureReason = 'BackedendFailure' OR IsAbxEligible IS NOT NULL)
          THEN 1 ELSE 0 END) AS TotalSentToAbx
  FROM Transactions
WHERE LogDate BETWEEN '2025-03-11 23:00:00.000' AND '2025-03-18 08:00:00.000'
  GROUP BY GateIP