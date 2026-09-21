$ErrorActionPreference='Stop'
# Run from an elevated PowerShell window for the Windows user who runs the lighting apps.
 $lightingUser=[Security.Principal.WindowsIdentity]::GetCurrent().Name
 foreach($lightingName in @('G19 Dashboard','OpenRGB Chroma Bridge')) {
  $lightingScript=Join-Path $env:LOCALAPPDATA ('Programs\'+$lightingName+'\watchdog.ps1')
  if(-not (Test-Path -LiteralPath $lightingScript)){throw "Missing watchdog: $lightingScript"}
  $lightingAction=New-ScheduledTaskAction -Execute "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -Argument ('-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "'+$lightingScript+'"') -WorkingDirectory (Split-Path $lightingScript)
  $lightingTrigger=New-ScheduledTaskTrigger -AtLogOn -User $lightingUser
  $lightingTrigger.Delay='PT15S'
  $lightingPrincipal=New-ScheduledTaskPrincipal -UserId $lightingUser -LogonType Interactive -RunLevel Highest
  $lightingSettings=New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1)
  Register-ScheduledTask -TaskName $lightingName -Action $lightingAction -Trigger $lightingTrigger -Principal $lightingPrincipal -Settings $lightingSettings -Description ('Starts the '+$lightingName+' watchdog in the signed-in user session and restarts it on failure.') -Force | Select-Object TaskName,State
 }


# Remove duplicate legacy startup entries after both tasks are registered.
foreach ($lightingName in @('G19 Dashboard','OpenRGB Chroma Bridge')) {
 Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name $lightingName -ErrorAction SilentlyContinue
}



