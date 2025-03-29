# Get the full path to BesTransactions.exe (assumed to be in the same folder as this script)
$exePath = Join-Path $PSScriptRoot "BesTransactions.exe"

# Define processes as an array of hashtables
$processes = @(
    #@{ LogFolder = "F:\20250319-Logs\Departure\BES\BES-MS1"; DBSuffix = "MS1-departure-20250319" },
    #@{ LogFolder = "F:\20250319-Logs\Departure\BES\BES-MS2"; DBSuffix = "MS2-departure-20250319" },
    #@{ LogFolder = "F:\20250319-Logs\Arrevil Logs\BES\BES-MS1"; DBSuffix = "MS1-arrival-20250319" },
    #@{ LogFolder = "F:\20250319-Logs\Arrevil Logs\BES\BES-MS2"; DBSuffix = "MS2-arrival-20250319" }

   @{ LogFolder = "X:\20250319-Logs\Departure\MonitoringStationDeparture _Logs\MonitoringStation#1_Logs\BES"; DBSuffix = "MS1-departure-20250319" },
   @{ LogFolder = "X:\20250319-Logs\Departure\MonitoringStationDeparture _Logs\MonitoringStation#2_Logs\BES"; DBSuffix = "MS2-departure-20250319" },
   @{ LogFolder = "X:\20250319-Logs\Arrevil Logs\Monitoring_Station\Monitoring_Station#1_2025-03-19\BES"; DBSuffix = "MS1-arrival-20250319" },
   @{ LogFolder = "X:\20250319-Logs\Arrevil Logs\Monitoring_Station\Monitoring_Station#2_2025-03-19\BES"; DBSuffix = "MS2-arrival-20250319" }

   #@{ LogFolder = "X:\KFIA-Logs-20250313\MonitoringStations\BES\107"; DBSuffix = "MS107-OldLogs" },
   #@{ LogFolder = "X:\KFIA-Logs-20250313\MonitoringStations\BES\113"; DBSuffix = "MS113-OldLogs" }
)

# Start each process and store the process objects in an array.
# The argument values are explicitly quoted to handle paths with spaces.
$processList = foreach ($p in $processes) {
    $args = "-LogFolder `"$($p.LogFolder)`" -DBSuffix `"$($p.DBSuffix)`""
    Start-Process -FilePath $exePath -ArgumentList $args -PassThru
}

Write-Host "Processes started. Waiting for them to exit..."

# Wait for all processes to complete
Wait-Process -Id $processList.Id

# Wait for user input before closing the script
Read-Host "Press Enter to exit"
