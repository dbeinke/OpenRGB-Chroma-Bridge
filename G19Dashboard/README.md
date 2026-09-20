# G19 Dashboard — direct USB migration

## Delivered

Source and hardware-free tests. Publish a Windows x64 framework-dependent build using the commands below. Requires the .NET 9 Windows Desktop Runtime x64. The migration was subsequently installed and tested on a physical G19; see validation below.

`Renderer.cs`, `Sensors.cs`, `background.png`, `dashboard-config.json`, and `start-sensors.ps1` are byte-for-byte identical to the uploaded project. CPU/Core Temp, NVIDIA/NVML, network selection, layout, refresh clamp (500–5000 ms), single-instance mutex, logs, and 30-second preview/metrics snapshots remain in place.

The transport now uses the LCD-only API from G19USB and its column-major RGB565 encoder. Open failures and write failures dispose the connection and retry after five seconds; synchronous writes report failures to the existing loop. Upstream also retries failed transfers internally. RGB565 reduces color precision from the rendered 32-bit bitmap. The watchdog still checks every 15 seconds and restarts the dashboard and Core Temp task; its LCore launch was removed to avoid competing USB owners. No keyboard input, macros, backlight colors, M-key LEDs, or brightness settings are sent by this dashboard.

## Build and tests

From the G19Dashboard folder, with .NET SDK 9 installed:

```powershell
dotnet build . -c Release
dotnet test vendor/G19USB.Tests -c Release
dotnet test tests -c Release
dotnet publish . -c Release -r win-x64 --self-contained false -o ready-to-run
Copy-Item watchdog.ps1,start-sensors.ps1 ready-to-run
```

NuGet restores LibUsbDotNet 2.2.75 and the test dependencies. Keep the entire repository tree when building; the dashboard references the vendored source project.

## Before a later hardware trial

1. Keep the original dashboard installation, configuration, artwork, watchdog, LGS installation, and the original uploaded ZIP. Back up the installed directory (normally `%LOCALAPPDATA%\Programs\G19 Dashboard`) to a separate folder. Do not overwrite it during the first trial.
2. Record the existing `G19 Dashboard` startup command and the `G19 Dashboard Sensors` scheduled task settings. Pause the existing dashboard watchdog/startup and exit the dashboard before testing. Stop only its identified watchdog process, not all PowerShell processes. The old and new dashboards share the single-instance mutex; an existing instance makes a new invocation exit immediately.
3. In Device Manager, record hardware IDs, device instance paths, driver provider/version, INF name, and screenshots of the G19 parent and child devices. Keep the original Logitech installer and driver package available. If the relevant original package is a third-party `oemNN.inf`, export that exact identified package with `pnputil /export-driver oemNN.inf <backup-folder>` from an elevated terminal. Do not guess an INF number. Inbox drivers may not be exportable.
4. Have another keyboard/mouse available. Changing a composite parent can affect its child functions. The earlier conversation reported a repaired Logitech LCD driver; restoring only the parent may not restore that child binding automatically.
5. For a driver-free preview, run `ready-to-run\G19-Dashboard.exe --preview` after stopping the existing dashboard. It reads sensors and writes `preview.png` and `metrics.json` under `%LOCALAPPDATA%\G19 Dashboard`, without opening USB. It uses the same data directory as the original app.

## Later libusbK / Zadig step — manual, not performed

Follow the [pinned G19USB driver instructions](https://github.com/MagicMau/G19USB/tree/6016ff8394dd2980bb015d9e2db72bfd275a53f1). Close LGS/LCore and other programs accessing the G19 first; keep LGS installed for rollback.

Download [Zadig from its official site](https://zadig.akeo.ie/) and run as administrator. Enable **Options > List All Devices**, and disable **Ignore Hubs or Composite Parents**. Select the G19/G19s **composite parent** with VID `046D`, PID `C229`, verifying against your recorded IDs. Do not select an `MI_00`/`MI_01` interface, USB hub, ordinary HID typing node, or another Logitech device. If the matching parent is not identifiable, stop and inspect the device tree before proceeding.

Select **libusbK**, then **Replace Driver**. This is the upstream requirement even though this app opens only LCD interface 0. The publish output includes the managed USB dependency, not a driver installer or native USB runtime installer. A native-library load error after driver setup requires checking the official x64 libusbK runtime installation; do not download loose DLLs from third-party DLL sites.

Run the new app manually from `ready-to-run`, without either watchdog. Check `%LOCALAPPDATA%\G19 Dashboard\dashboard.log` for `LCD direct USB initialized` and subsequent `LCD=True`. Verify colors/orientation, sensor updates, ordinary typing, special keys, and any RGB software. Then test unplug/replug and sleep/resume. Only after acceptance, deploy all ready-to-run files and point startup at the updated watchdog. It depends on the existing `G19 Dashboard Sensors` task; this package does not create that task.

## Rollback

1. Stop the new dashboard and its watchdog; disable any startup command pointing to it.
2. In Device Manager, identify the exact `046D:C229` parent under libusbK using the recorded instance path. Use Driver > Roll Back Driver if available. Otherwise use Update Driver > Browse my computer > Let me pick to select the recorded original compatible driver.
3. If that does not restore it, follow the [official Zadig recovery procedure](https://github.com/pbatard/libwdi/wiki/FAQ): uninstall that exact device, selecting removal of its Zadig-installed driver package when offered, then unplug/replug and let Windows rediscover it. Do not remove unrelated libusbK devices or packages.
4. Restore the previously recorded Logitech child driver if necessary using the saved signed driver package or original LGS installer. Check the parent and LCD child against the recorded baseline. Reboot if Windows requests it.
5. Restore the original dashboard folder/configuration and original watchdog/startup command, then start LGS and the original dashboard. Confirm typing, LCD, Core Temp, special keys and RGB behavior. Do not run the new and original watchdog together.

## Validation and limits

Release build and publish succeeded using SDK 9.0.205 on Windows x64 with zero build warnings/errors. All 56 upstream tests and 5 dashboard integration tests passed. Tests exercise actual bitmap encoding (colors, byte order, column order and corners), wrong-size rejection, unopened transport behavior, and renderer output with populated/missing metrics. Renderer previews were generated with synthetic test values; these are not live sensor measurements. Tests can be reproduced using the commands above.

Physical G19 validation on 2026-09-20: libusbK 3.1.0.0 installed on the 046D:C229 composite parent; direct USB initialization and frame writes succeeded; the user confirmed the screen was visible and updating. The installed build and updated watchdog were started successfully. Unplug/replug, sleep/resume, and long-running watchdog recovery remain untested. G-keys/macros and LGS applet switching are not implemented; coexistence with OpenRGB/the existing RGB bridge is not guaranteed after the parent driver change. The app does not change LCD brightness, so retained hardware brightness may need separate adjustment. A basic physical display test passed; the recovery and coexistence limitations above still apply.

## Third-party provenance

G19USB source pinned to `6016ff8394dd2980bb015d9e2db72bfd275a53f1`, from https://github.com/MagicMau/G19USB. Apache-2.0 license and NOTICE are included. Local modifications: library/tests retargeted from .NET 10 to .NET 9; MinVer removed for a standalone source build; LCD bulk transfers must report the complete requested byte count before being accepted as successful. Other upstream source is preserved. Upstream documentation in `vendor/README.md` describes the original .NET 10 project, not this compatibility build.

LibUsbDotNet 2.2.75 is distributed unmodified as a separate replaceable assembly. Its license is in `licenses/`, and corresponding source is available at https://github.com/LibUsbDotNet/LibUsbDotNet/tree/2d7289ab5d4059ac9bfbde666cfb0d4825d89814 (source commit from the package metadata: `2d7289ab5d4059ac9bfbde666cfb0d4825d89814`). Test dependencies are restored from NuGet and are not part of the ready-to-run distribution.
