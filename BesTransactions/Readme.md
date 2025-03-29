* After build you can use it to create the DB like:
`
.\BesTransactions.exe -LogFolder "F:\20250319-Logs\Departure\BES\BES-MS1" -DBSuffix "MS1-departure-20250319"
.\BesTransactions.exe -LogFolder "F:\20250319-Logs\Departure\BES\BES-MS2" -DBSuffix "MS2-departure-20250319"
.\BesTransactions.exe -LogFolder "F:\20250319-Logs\Arrevil Logs\BES\BES-MS1" -DBSuffix "MS1-arrival-20250319"
.\BesTransactions.exe -LogFolder "F:\20250319-Logs\Arrevil Logs\BES\BES-MS2" -DBSuffix "MS2-arrival-20250319"
.\BesTransactions.exe -LogFolder "C:\Users\Administrator\Desktop\~Temp\KFIA-Logs-20250313\MS\BES\107" -DBSuffix "MS107-OldLogs"
.\BesTransactions.exe -LogFolder "C:\Users\Administrator\Desktop\~Temp\KFIA-Logs-20250313\MS\BES\113" -DBSuffix "MS113-OldLogs"
`

* This will create databases file with name `BesTransactions-<DBSuffix you wrote>.db`.
* If no suffix provided it will be random GUID
* Note to run it on sigle monitoring station BES log folder, transaction IDs may intersect if two monitoring stations logs parsed.


  @{ LogFolder = "X:\20250319-Logs\Departure\MonitoringStationDeparture _Logs\MonitoringStation#1_Logs\BES"; DBSuffix = "MS1-departure-20250319" },
   @{ LogFolder = "X:\20250319-Logs\Departure\MonitoringStationDeparture _Logs\MonitoringStation#2_Logs\BES"; DBSuffix = "MS2-departure-20250319" },
   @{ LogFolder = "X:\20250319-Logs\Arrevil Logs\Monitoring_Station\Monitoring_Station#1_2025-03-19\BES"; DBSuffix = "MS1-arrival-20250319" },
   @{ LogFolder = "X:\20250319-Logs\Arrevil Logs\Monitoring_Station\Monitoring_Station#2_2025-03-19\BES"; DBSuffix = "MS2-arrival-20250319" }