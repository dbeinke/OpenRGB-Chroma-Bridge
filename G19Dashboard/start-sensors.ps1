$ErrorActionPreference='Stop'
if (-not (Get-Process -Name 'Core Temp' -ErrorAction SilentlyContinue)) {
 Start-Process -FilePath 'C:\Program Files\Core Temp\Core Temp.exe' -WindowStyle Hidden
}
