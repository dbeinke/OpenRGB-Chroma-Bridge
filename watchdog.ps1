$createdNew = $false
$watchdogMutex = [System.Threading.Mutex]::new(
    $true,
    'Local\OpenRGB-Chroma-Bridge-Watchdog',
    [ref]$createdNew)

if (-not $createdNew) {
    exit 0
}

$bridgeExe = Join-Path $PSScriptRoot 'OpenRGB-Chroma-Bridge.exe'
$logDirectory = Join-Path $env:LOCALAPPDATA 'OpenRGB Chroma Bridge'
$bridgeLog = Join-Path $logDirectory 'bridge.log'
$watchdogLog = Join-Path $logDirectory 'watchdog.log'

New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null

function Write-WatchdogLog([string]$Message) {
    Add-Content -LiteralPath $watchdogLog -Value "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $Message"
}

function Get-BridgeProcess {
    Get-CimInstance Win32_Process -Filter "Name = 'OpenRGB-Chroma-Bridge.exe'" -ErrorAction SilentlyContinue |
        Select-Object -First 1
}

try {
    Write-WatchdogLog 'Watchdog started.'

    while ($true) {
        $bridge = Get-BridgeProcess

        if ($bridge) {
            $processAge = (Get-Date) - $bridge.CreationDate
            $logAge = if (Test-Path -LiteralPath $bridgeLog) {
                (Get-Date) - (Get-Item -LiteralPath $bridgeLog).LastWriteTime
            } else {
                $processAge
            }

            if ($processAge.TotalSeconds -gt 45 -and $logAge.TotalSeconds -gt 30) {
                Write-WatchdogLog "Bridge PID $($bridge.ProcessId) stopped responding; restarting it."
                Stop-Process -Id $bridge.ProcessId -Force -ErrorAction SilentlyContinue
                Start-Sleep -Seconds 2
                $bridge = $null
            }
        }

        if (-not $bridge -and (Test-Path -LiteralPath $bridgeExe)) {
            $started = Start-Process -FilePath $bridgeExe -WindowStyle Hidden -PassThru
            Write-WatchdogLog "Started bridge PID $($started.Id)."
        }

        Start-Sleep -Seconds 10
    }
}
finally {
    Write-WatchdogLog 'Watchdog stopped.'
    $watchdogMutex.ReleaseMutex()
    $watchdogMutex.Dispose()
}
