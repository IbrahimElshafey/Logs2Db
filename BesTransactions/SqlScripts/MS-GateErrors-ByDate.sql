-- grouped by date
SELECT 
    date(LogDate) AS 'اليوم',
    SUM(CASE WHEN FailureReason = 'DocumentReadFailure' THEN 1 ELSE 0 END) AS "فشل في قراءة المستند",
    SUM(CASE WHEN FailureReason = 'ReaderIneligibleDocument' THEN 1 ELSE 0 END) AS "بيانات الوثيقة غير صالحة",
    SUM(CASE WHEN FailureReason = 'PersonDetectedOutsideExitDoor' THEN 1 ELSE 0 END) AS "إستشعار وجود شخص أمام بوابة الخروج",
    SUM(CASE WHEN FailureReason = 'ClearanceActionTimeout' THEN 1 ELSE 0 END) AS "انتهاء الوقت المسموح لإجراءات تخليص السفر",
    SUM(CASE WHEN FailureReason = 'Tailgating' THEN 1 ELSE 0 END) AS "إستشعار وجود أكثر من شخص داخل البوابة",
    SUM(CASE WHEN FailureReason = 'VisionCameraBlocked' THEN 1 ELSE 0 END) AS "الكاميرا محجوبة (كاميرا استشعار المسافرين)",
    SUM(CASE WHEN FailureReason = 'FaceCaptureFailure' THEN 1 ELSE 0 END) AS "فشل في التقاط الوجه بعد ثلاث محاولات",
    SUM(CASE WHEN FailureReason = 'DocumentNotSupported' THEN 1 ELSE 0 END) AS "الوثيقة غير مدعومة (هوية خليجية غير السعودية والكويت)",
    SUM(CASE WHEN FailureReason = 'EmergencyActivated' THEN 1 ELSE 0 END) AS "تفعيل الطوارئ"
FROM Transactions
WHERE FailureReason IN (
    'DocumentReadFailure',
    'ReaderIneligibleDocument',
    'PersonDetectedOutsideExitDoor',
    'ClearanceActionTimeout',
    'Tailgating',
    'VisionCameraBlocked',
    'FaceCaptureFailure',
    'DocumentNotSupported',
    'EmergencyActivated'
)
AND LogDate BETWEEN '2025-03-11 23:00:00.000' AND '2025-03-18 08:00:00.000'
GROUP BY date(LogDate)
ORDER BY date(LogDate);