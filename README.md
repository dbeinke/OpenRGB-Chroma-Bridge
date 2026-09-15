# OpenRGB Chroma Bridge

A Windows background bridge that keeps OpenRGB devices and a legacy Razer
ManO'War synchronized through the Razer Chroma SDK.

This background utility mirrors the active color/effect on the `EVGA Z590 DARK USB`
OpenRGB device to the Razer ManO'War through the local Razer Chroma SDK.

Supported mappings:

- OpenRGB Off -> headset Off
- OpenRGB Static/Direct -> headset Static
- OpenRGB Breathing -> headset Breathing
- OpenRGB Spectrum Cycle -> headset Spectrum Cycling

The Razer Ouroboros has fixed-color lighting, so it is not a Chroma color target.

The bridge applies one synchronized breathing clock to the EVGA motherboard, all
detected `TT LEDFanBox` controllers, the Razer Firefly, the NVIDIA GPU lighting, and
the Razer ManO'War. OpenRGB devices use Direct or Static mode so every device follows
the same phase.

The included active profile is `White to Red - Speed 30`. It slides from full white
to full red and back at constant brightness. On OpenRGB's 0–100 speed scale, speed
30 produces an 11-second cycle.
The white and pure-red endpoints each hold for 10% of the cycle so slower devices,
including the ManO'War, visibly reach the exact endpoint color.
The legacy ManO'War path is paced at 300 ms, led by 700 ms to compensate for
Synapse 2 latency, and reaches pure red early enough to hold it through the shared
red endpoint.
Set `FollowSourceColor` to `true` to replace the profile's first color with the EVGA
source color.

The Razer Ouroboros cannot participate because its lighting color and animation are
fixed by the mouse hardware.

## Requirements

- Windows 10 or 11
- OpenRGB 1.0 with the SDK server available on `127.0.0.1:6742`
- .NET 9 Desktop Runtime, or a self-contained publish
- Razer Synapse 2 with Chroma Apps enabled
- Razer Chroma SDK Core and `CChromaEditorLibrary64.dll`

## Build

```powershell
dotnet restore
dotnet publish -c Release -r win-x64 --self-contained false
```

Copy `CChromaEditorLibrary64.dll` beside the published executable if it is not
installed in the Windows system path. Edit `bridge-config.json` to match the
OpenRGB source device and desired synchronized profile.

The custom motherboard integration and verified HID report format are covered
in [EVGA Z590 DARK USB lighting](docs/EVGA-Z590-DARK-USB.md).

## Operation

The bridge starts automatically when the current Windows user signs in. It runs in
the background and writes status information to:

`%LOCALAPPDATA%\OpenRGB Chroma Bridge\bridge.log`

The source device, two colors, speed, frame interval, and minimum brightness can be
changed in `bridge-config.json`. Saved profile configurations are kept in the
`Profiles` folder. Close the bridge before editing that file, then start it again to
load the change.

If OpenRGB or Synapse is temporarily unavailable, the bridge waits and reconnects.
It also watches for OpenRGB client profile changes once per second, restores the
Direct/Static modes required for synchronization, and reconnects automatically.
Manual colors selected on the EVGA source device are detected before the next
animation frame and immediately become the synchronized color on every target. The
same applies to edits on any synchronized OpenRGB device. Selecting the saved
`White to Red - Speed 30` profile clears the manual override and restarts its cycle
from full white; restarting the bridge does the same.
