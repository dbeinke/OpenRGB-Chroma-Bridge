# G19 System Dashboard

A 320x240 LCD dashboard, refreshing once per second. The included layout is configured for an i9-11900K and RTX 4080; hardware labels are in Renderer.cs. Network selection is configured in dashboard-config.json.

- CPU: hottest core temperature in Celsius, current Core Temp CPU clock in GHz, and average core load.
- GPU: NVIDIA core temperature, graphics clock, memory clock, and utilization.
- Network: actual Ethernet 2 upload/download throughput in megabits per second (Mbps), including local-network traffic. These are traffic rates, not an internet speed test.

CPU data comes from the installed Core Temp shared-memory interface. GPU data comes directly from NVIDIA's installed NVML driver library. Network data uses Windows adapter byte counters. Missing sensor data displays `--` rather than invented readings. Network rates need two samples after startup. If Ethernet 2 disconnects, an active Ethernet/Wi-Fi adapter is selected, preferring one with a gateway.

The app uses the installed Logitech LCD SDK and requires Logitech Gaming Software (LCore) to be running. Select `G19 System Dashboard` with the G19 LCD app-switch controls if another applet is visible.

## Installed setup

App: `%LOCALAPPDATA%\Programs\G19 Dashboard\G19-Dashboard.exe`
Config: `dashboard-config.json` beside the app. Supports `NetworkAdapter` and `RefreshMilliseconds` (500-5000).
Logs and a periodically saved screen preview: `%LOCALAPPDATA%\G19 Dashboard`.
Windows startup entry: `G19 Dashboard`, running `watchdog.ps1`.
Scheduled task: `G19 Dashboard Sensors`, starts the installed Core Temp with the elevation needed for hardware sensors at sign-in.
The watchdog keeps the dashboard, LCore, and Core Temp running. The existing RGB bridge is separate and remains unchanged.

To stop automatic startup, remove the `G19 Dashboard` value from the current user's Windows Run key and disable the `G19 Dashboard Sensors` scheduled task, then stop the watchdog and dashboard processes. Core Temp and Logitech software remain installed.

## Display repair performed

The LCD interface originally had no assigned driver and the LCD SDK registration was missing. The existing Logitech-signed LGPBTDD driver package was installed using pnputil, and the installed x64 LgLcdApi.dll and LogitechLcd.dll were registered. Windows now identifies the device as `Logitech G19 LCD`; the SDK connects and accepts frames. No driver-security setting was disabled.

## Build

Requires .NET 9 Desktop Runtime and the existing Logitech Gaming Software/Core Temp/NVIDIA driver installations.

    dotnet publish -c Release -r win-x64 --self-contained false

Run with `--preview` to write a live PNG and metrics JSON without opening the LCD. Exit an existing dashboard instance first because the application is single-instance.

## Validation

Release build passed with no warnings. Live Core Temp temperatures/clocks and network samples were read successfully. NVIDIA readings were checked against nvidia-smi. The installed dashboard connects to the repaired LCD, submits frames, and refreshes metrics. The sensor startup task completed with result 0. The RGB synchronization process continues operating.

References: https://www.alcpu.com/CoreTemp/developers.html and the installed Logitech LCD SDK.

## Custom artwork

The included background.png provides the Darkness metal-and-smoke artwork. Replace it with another 4:3 image and restart the dashboard to change the artwork. The renderer scales it to 320x240 and draws bright statistics directly above it with subtle black text shadows. If the image is unavailable, a solid dark background is used.
