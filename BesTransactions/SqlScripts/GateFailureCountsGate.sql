SELECT 
      GateIP,
      SUM(CASE WHEN FailureReason = 'DocumentReadFailure' AND (IsAbxCompleted IS NULL OR IsAbxCompleted <> 1) THEN 1 ELSE 0 END) AS DocumentReadFailure,
      SUM(CASE WHEN FailureReason = 'ReaderIneligibleDocument' AND (IsAbxCompleted IS NULL OR IsAbxCompleted <> 1) THEN 1 ELSE 0 END) AS ReaderIneligibleDocument,
      SUM(CASE WHEN FailureReason = 'GateIsNotClear' AND (IsAbxCompleted IS NULL OR IsAbxCompleted <> 1) THEN 1 ELSE 0 END) AS GateIsNotClear,
      SUM(CASE WHEN FailureReason = 'PersonDetectedOutsideExitDoor' AND (IsAbxCompleted IS NULL OR IsAbxCompleted <> 1) THEN 1 ELSE 0 END) AS PersonDetectedOutsideExitDoor,
      SUM(CASE WHEN FailureReason = 'ClearanceActionTimeout' AND (IsAbxCompleted IS NULL OR IsAbxCompleted <> 1) THEN 1 ELSE 0 END) AS ClearanceActionTimeout,
      SUM(CASE WHEN FailureReason = 'Tailgating' AND (IsAbxCompleted IS NULL OR IsAbxCompleted <> 1) THEN 1 ELSE 0 END) AS Tailgating,
      SUM(CASE WHEN FailureReason = 'VisionCameraBlocked' AND (IsAbxCompleted IS NULL OR IsAbxCompleted <> 1) THEN 1 ELSE 0 END) AS VisionCameraBlocked,
      SUM(CASE WHEN FailureReason = 'FaceCaptureFailure' AND (IsAbxCompleted IS NULL OR IsAbxCompleted <> 1) THEN 1 ELSE 0 END) AS FaceCaptureFailure,
      SUM(CASE WHEN FailureReason = 'Unknown' AND (IsAbxCompleted IS NULL OR IsAbxCompleted <> 1) THEN 1 ELSE 0 END) AS Unknown,
      SUM(CASE WHEN FailureReason = 'DocumentNotSupported' AND (IsAbxCompleted IS NULL OR IsAbxCompleted <> 1) THEN 1 ELSE 0 END) AS DocumentNotSupported,
      SUM(CASE WHEN FailureReason = 'EmergencyActivated' AND (IsAbxCompleted IS NULL OR IsAbxCompleted <> 1) THEN 1 ELSE 0 END) AS EmergencyActivated,
      SUM(CASE WHEN FailureReason = 'CommunicationError' AND (IsAbxCompleted IS NULL OR IsAbxCompleted <> 1) THEN 1 ELSE 0 END) AS CommunicationError,
      SUM(CASE 
            WHEN ( (FailureReason IS NULL) 
                   OR FailureReason NOT IN (
                        'DocumentReadFailure',
                        'ReaderIneligibleDocument',
                        'GateIsNotClear',
                        'BackedendFailure',
                        'PersonDetectedOutsideExitDoor',
                        'ClearanceActionTimeout',
                        'Tailgating',
                        'VisionCameraBlocked',
                        'FaceCaptureFailure',
                        'BcsIneligibleDocument',
                        'Unknown',
                        'DocumentNotSupported',
                        'EmergencyActivated',
                        'CompleteCrossingError'
                   )
                 )
                 AND (IsAbxCompleted IS NULL OR IsAbxCompleted = 0)
            THEN 1 ELSE 0 END) AS NullAndOtherFailure
FROM Transactions
WHERE LogDate BETWEEN '2025-03-17 23:00:00.000' AND '2025-03-18 08:00:00.000'
GROUP BY GateIP;
