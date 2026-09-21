# Saved setup — September 21, 2026

The user confirmed that the Thermaltake fans synchronized with the red/white breathing effect after resetting the three fan USB devices, reconnecting OpenRGB, and reapplying the lighting modes/colors. A requested solid-green override did not remain active; that behavior is unresolved.

## Startup

Run `install-startup-tasks.ps1` in an elevated PowerShell window as the user who runs the lighting apps, after installing both watchdog scripts. It registers the G19 Dashboard and OpenRGB Chroma Bridge watchdogs at sign-in with a 15-second delay, matching elevated interactive contexts, unlimited runtime, and restart-on-failure. It replaces the legacy Run entries. OpenRGB remains an automatic Windows service. The existing Core Temp sensor task is separate.

## Fan diagnosis

All three Thermaltake USB devices (VID 264A, PIDs 232B/232C/232D) opened successfully but HID writes returned -1, with an overlapped-I/O timeout error. OpenRGB's cached LED colors still matched the requested effect; those values were not hardware acknowledgement.

Restarting only OpenRGB and reapplying Direct mode did not recover the fans. With the bridge and OpenRGB stopped, restarting each of the three exact USB device instances with Windows `pnputil /restart-device` restored successful 193-byte USB initialization writes. OpenRGB and the bridge were then restarted. A subsequent reapplication of Direct mode and LED colors to the controllers was followed by user confirmation that red/white breathing synchronized.

Do not reset unrelated USB devices. Do not infer physical synchronization from OpenRGB's cached colors alone. This recovery was verified for this incident; a full reboot test and a permanent automatic recovery fix remain outstanding.

## LCD

The direct-USB G19 dashboard includes CPU/GPU temperatures, clocks and utilization, network throughput, and RAM used/total GiB plus percent and a usage bar over the custom background. RAM uses Windows GlobalMemoryStatusEx. The dashboard owns the G19 USB connection and shares keyboard lighting with the bridge through its named pipe.
