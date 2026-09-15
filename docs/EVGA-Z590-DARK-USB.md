# EVGA Z590 DARK USB lighting

Tested hardware: EVGA Z590 DARK (E599), USB VID `0x3842` / PID `0x300A`,
product string `EVGA Z590 MCU`. Only interface 1, usage page `0x08`, usage
`0x4B` is the lighting collection; other collections must not be opened by
this detector.

Protocol facts were established by examining the locally installed vendor
application's report definitions and verified with feature-report readback on
the hardware. The implementation does not link to or redistribute the vendor
application or library.

## Lighting report

The board uses a 195-byte HID feature report, including the report ID.

| Offset | Meaning |
|---|---|
| 0 | Report ID `0x07` |
| 1-2 | Header `0xEA`, `0x02` |
| 3 | Command: `0x01` modes, `0x03` static colors, `0x04` breathing |
| 4 | `0` write, `1` read |
| 5 | Zero |
| 6 | Request zero; response `0xC0` success, `0xC1` failure, `0xC2` busy |
| 7 | Checksum: total byte sum modulo 256 equals zero |
| 8 | Zone mask for writes; zero on a read request |
| 9 onward | Six mode bytes, or six brightness/red/green/blue groups |
| remaining | Zero-filled request padding |

Zone bit/index order: ARGB header 1, ARGB header 2, PCH, motherboard cover,
RGB header 1, RGB header 2. The DARK excludes PCH; the write mask for all
supported zones is `0x3B`.

Mode `0` is Off, mode `1` is Static, and mode `2` is Breathing. Other existing
modes are preserved on detection. Static brightness uses 255; RGB channels use
bytes 0-255.

Send the feature report, wait 5 ms, then get the feature report. Validate the
exact response size, report ID, header, command, operation, success status, and
checksum before accepting it. Failed or ambiguous replies are logged and are
not blindly retried.

## Blackout switch

The blackout switch uses a 17-byte general feature report: report ID `0x04`,
header `0xEA 0x02`, command `0x50`, read/write in byte 4, status in byte 6,
and value in byte 7. Value `0` enables blackout and `1` enables lighting. This
report has no lighting-report checksum. Only explicit selection of Static or
Breathing clears blackout. Detection never writes this setting.

## OpenRGB behavior

The detector validates the board name and reads modes, static colors,
breathing parameters, and timing limits. It exposes five one-color zones, Off,
Static, Breathing, and an inert No change entry for existing effects. It does
not send firmware, flash-save, reset, fan, pump, or overclocking commands.
Direct streaming and per-addressable-LED control are not advertised.

Vendor software must not simultaneously change the same controller. Local I/O
is serialized; no cross-application locking protocol has been established.

## Breathing

Command `0x04` carries six nine-byte groups starting at offset 9: brightness,
R1, G1, B1, R2, G2, B2, cycle-duration low byte, and cycle-duration high byte.
Brightness and colors range from 0-255. OpenRGB exposes two shared colors,
brightness, and speed across all five supported zones.

Timing limits are read with command `0x20`, operation `2`. Unlike zone reports,
the payload starts at offset 8: little-endian 16-bit minimum, default, and
maximum breathing durations. The values must satisfy
`0 < minimum < default < maximum`. Speed 0 maps to maximum duration, 50 maps to
the default, and 100 maps to minimum duration, linearly within each half.
Detection performs no writes. An existing breathing effect is active only when
all supported zones have identical parameters.

