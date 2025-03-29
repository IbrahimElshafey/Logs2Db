select GateIP,Min(Date) as Start, Max(Date) as END
From Transactions
Group by GateIP