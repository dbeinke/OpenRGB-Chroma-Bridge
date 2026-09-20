$created=$false
$mutex=[Threading.Mutex]::new($true,'Local\G19-Dashboard-Watchdog',[ref]$created)
if(-not $created){exit}
try {
 while($true){
  if(-not (Get-Process -Name LCore -ErrorAction SilentlyContinue)){
   Start-Process -FilePath 'C:\Program Files\Logitech Gaming Software\LCore.exe' -ArgumentList '/minimized' -WindowStyle Hidden
  }
  if(-not (Get-Process -Name 'Core Temp' -ErrorAction SilentlyContinue)){
   & "$env:SystemRoot\System32\schtasks.exe" /Run /TN 'G19 Dashboard Sensors' 2>&1 | Out-Null
  }
  if(-not (Get-Process -Name G19-Dashboard -ErrorAction SilentlyContinue)){
   Start-Process -FilePath (Join-Path $PSScriptRoot 'G19-Dashboard.exe') -WindowStyle Hidden
  }
  Start-Sleep -Seconds 15
 }
} finally {$mutex.ReleaseMutex();$mutex.Dispose()}
