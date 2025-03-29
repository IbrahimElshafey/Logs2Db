-- interesting cases where failure reason is not clear
Select TxId,	GateIP,	FailureReason,	GateId,	DocumentNumber,	LogDate,	IsAbxEligible,	IsAbxFaceVerified,	IsAbxCompleted
from Transactions
WHERE 
	(IsAbxCompleted=0 or IsAbxCompleted is null) 
    and (
        FailureReason in ('CommunicationError','GateIsNotClear','Unknown') 
        or FailureReason is null)
	and LogDate > '2025-03-11 24:59:59.999'
Order by FailureReason