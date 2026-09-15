$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdministrator) {
    $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    $elevated = Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    exit $elevated.ExitCode
}

$openRgbConfig = Join-Path $env:ProgramFiles 'OpenRGB\service_config\OpenRGB.json'
if (-not (Test-Path -LiteralPath $openRgbConfig)) {
    throw "OpenRGB service configuration was not found at $openRgbConfig"
}

$content = [IO.File]::ReadAllText($openRgbConfig)
if ($content -notmatch '"name"\s*:\s*"Chroma Bridge"') {
    $virtualController = @'
"DebugDevices": {
        "devices": [
            {
                "name": "Chroma Bridge",
                "type": "argb",
                "layout": 0,
                "single": true,
                "linear": false,
                "resizable": false,
                "keyboard": false,
                "underglow": false
            }
        ]
    }
'@
    $updated = [Text.RegularExpressions.Regex]::Replace(
        $content,
        '"DebugDevices"\s*:\s*null',
        $virtualController,
        [Text.RegularExpressions.RegexOptions]::None,
        [TimeSpan]::FromSeconds(2))

    if ($updated -eq $content) {
        throw 'DebugDevices is already configured and could not be updated automatically.'
    }

    $backup = "$openRgbConfig.chroma-bridge-backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    Copy-Item -LiteralPath $openRgbConfig -Destination $backup
    [IO.File]::WriteAllText($openRgbConfig, $updated, [Text.UTF8Encoding]::new($false))
}

$service = Get-Service -Name 'OpenRGB' -ErrorAction SilentlyContinue
if ($service) {
    Restart-Service -Name 'OpenRGB' -Force
}
