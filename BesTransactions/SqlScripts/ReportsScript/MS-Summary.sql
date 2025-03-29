WITH AggData AS (
    SELECT 
        SUM(CASE WHEN IsAbxCompleted = 1 THEN 1 ELSE 0 END) AS successful,
        SUM(CASE WHEN (IsAbxCompleted IS NULL OR IsAbxCompleted = 0) 
                 AND FailureSide = 'Abx' THEN 1 ELSE 0 END) AS abx_fail,
        SUM(CASE WHEN (IsAbxCompleted IS NULL OR IsAbxCompleted = 0) 
                 AND FailureSide = 'Gate' THEN 1 ELSE 0 END) AS gate_fail,
        SUM(CASE WHEN (IsAbxCompleted IS NULL OR IsAbxCompleted = 0) THEN 1 ELSE 0 END) AS total_fail,
        COUNT(*) AS total_count
    FROM Transactions
    WHERE LogDate BETWEEN '2025-03-11 23:00:00.000' AND '2025-03-18 08:00:00.000'
)
SELECT
    '11-18 March' AS [Type],
    successful AS [العمليات الناجحة],
    abx_fail AS [العمليات المرفوضه من النظام الخلفي],
    gate_fail AS [العمليات المرفوضه من البوابة],
    total_fail AS [إجمالي العمليات المرفوضة],
    total_count AS [الاجمالي]
FROM AggData

UNION ALL

SELECT
    'النسبة المئوية' AS [Type],
    printf('%.2f%%', successful * 100.0 / total_count) AS [العمليات الناجحة],
    printf('%.2f%%', abx_fail * 100.0 / total_count) AS [العمليات المرفوضه من النظام الخلفي],
    printf('%.2f%%', gate_fail * 100.0 / total_count) AS [العمليات المرفوضه من البوابة],
    printf('%.2f%%', total_fail * 100.0 / total_count) AS [إجمالي العمليات المرفوضة],
    printf('%.2f%%', 100.0) AS [الاجمالي]
FROM AggData;
