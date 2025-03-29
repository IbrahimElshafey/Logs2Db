WITH DateRange AS (
    SELECT 
        '2025-03-11 23:00:00.000' AS startDate,
        '2025-03-18 08:00:00.000' AS endDate
),
Totals AS (
    SELECT
        (SELECT COUNT(*) 
         FROM Transactions 
         CROSS JOIN DateRange
         WHERE LogDate BETWEEN startDate AND endDate
        ) AS TotalTransactions,
        
        (SELECT COUNT(*) 
         FROM Transactions 
         CROSS JOIN DateRange
         WHERE (IsAbxCompleted IS NULL OR IsAbxCompleted = 0)
           AND LogDate BETWEEN startDate AND endDate
        ) AS TotalFailedTransactions,
        
        (SELECT COUNT(*) 
         FROM Transactions 
         CROSS JOIN DateRange
         WHERE FailureSide = 'Gate'
           AND LogDate BETWEEN startDate AND endDate
        ) AS TotalGateFailures
)
SELECT 
    COUNT(*) AS Occurances,
    CASE 
         WHEN FailureReason IN ('Unknown', 'GateIsNotClear', 'CommunicationError') 
              OR FailureReason IS NULL 
              THEN 'غير معروف'
         WHEN FailureReason = 'DocumentReadFailure' THEN 'فشل في قراءة المستند'
         WHEN FailureReason = 'ReaderIneligibleDocument' THEN 'بيانات الوثيقة غير صالحة'
         WHEN FailureReason = 'PersonDetectedOutsideExitDoor' THEN 'إستشعار وجود شخص أمام بوابة الخروج'
         WHEN FailureReason = 'ClearanceActionTimeout' THEN 'انتهاء الوقت المسموح لإجراءات تخليص السفر'
         WHEN FailureReason = 'Tailgating' THEN 'إستشعار وجود أكثر من شخص داخل البوابة'
         WHEN FailureReason = 'VisionCameraBlocked' THEN 'الكاميرا محجوبة (كاميرا استشعار المسافرين)'
         WHEN FailureReason = 'FaceCaptureFailure' THEN 'فشل في التقاط الوجه بعد ثلاث محاولات'
         WHEN FailureReason = 'DocumentNotSupported' THEN 'الوثيقة غير مدعومة (هوية خليجية غير السعودية والكويت)'
         WHEN FailureReason = 'EmergencyActivated' THEN 'تفعيل الطوارئ'
         WHEN FailureReason = 'UnhandledDeviceError' THEN 'خطأ بجهاز في البوابة'
         WHEN FailureReason = 'EGateDeactivated' THEN 'الغاء تنشيط البوابة'
    END AS 'السبب',
    printf('%.2f%%', COUNT(*) * 100.0 / (SELECT TotalTransactions FROM Totals)) AS 'النسبة من الكل',
    printf('%.2f%%', COUNT(*) * 100.0 / (SELECT TotalFailedTransactions FROM Totals)) AS 'النسبة من كل الغير مكتمل',
    printf('%.2f%%', COUNT(*) * 100.0 / (SELECT TotalGateFailures FROM Totals)) AS 'النسبة من غير المكتملة على البوابة'
FROM Transactions
CROSS JOIN Totals
CROSS JOIN DateRange
WHERE FailureSide = 'Gate'
  AND LogDate BETWEEN startDate AND endDate
GROUP BY FailureReason
ORDER BY Occurances DESC;
