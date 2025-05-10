-- get DSPGeneralError in CCSS groups
SELECT
  '#' || LineHash      AS LineHash,
  MIN(Message)         AS MessageSample,
  COUNT(*)             AS Counts
FROM ParsedLogLines
WHERE Message LIKE '%Error Code=DSPGeneralError%'
  AND Source    = 'CCSS'
  AND Timestamp > '2025-02-01'
GROUP BY LineHash
ORDER BY Counts DESC;


-- get DSPGeneralError in CCSS all lines
select * 
FROM ParsedLogLines
WHERE Message LIKE '%Error Code=DSPGeneralError%'
  AND Source    = 'CCSS'
  AND Timestamp > '2025-02-01'
  AND Level = 'ERROR'