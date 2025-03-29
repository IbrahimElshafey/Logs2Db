-- Group transactions by failure reason and count the occurrences
Select FailureReason,count() as 'Occurrences'
from Transactions
where IsAbxCompleted=0 or IsAbxCompleted is null
GROUP by FailureReason
order by Occurrences desc



