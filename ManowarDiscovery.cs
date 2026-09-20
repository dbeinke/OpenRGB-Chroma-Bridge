using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OpenRgbChromaBridge;

/// <summary>
/// Finds the ManO'War's vendor HID collection without opening the audio interface
/// or writing a report. The lighting protocol will be added only after this probe
/// confirms the report shape on the attached receiver.
/// </summary>
internal static class ManowarDiscovery
{
    private const string ReceiverPathFragment = "vid_1532&pid_0a02&mi_03";
    private const ushort VendorUsagePage = 0xFF00;

    [StructLayout(LayoutKind.Sequential)]
    private struct InterfaceData
    {
        public int Size;
        public Guid Guid;
        public int Flags;
        public IntPtr Reserved;
    }

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid guid);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_FreePreparsedData(IntPtr data);

    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr data, IntPtr caps);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(ref Guid guid, IntPtr enumerator, IntPtr parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid guid, uint index, ref InterfaceData data);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref InterfaceData data, IntPtr detail, uint size, out uint required, IntPtr device);

    [DllImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    internal readonly record struct ReportShape(ushort UsagePage, ushort InputBytes, ushort OutputBytes, ushort FeatureBytes);

    public static ReportShape? FindVendorCollection()
    {
        HidD_GetHidGuid(out Guid hidGuid);
        IntPtr deviceSet = SetupDiGetClassDevsW(ref hidGuid, IntPtr.Zero, IntPtr.Zero, 0x12);
        if (deviceSet == new IntPtr(-1))
            throw new Win32Exception();

        try
        {
            for (uint index = 0; ; index++)
            {
                var data = new InterfaceData { Size = Marshal.SizeOf<InterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(deviceSet, IntPtr.Zero, ref hidGuid, index, ref data))
                {
                    if (Marshal.GetLastWin32Error() == 259)
                        return null;
                    throw new Win32Exception();
                }

                SetupDiGetDeviceInterfaceDetailW(deviceSet, ref data, IntPtr.Zero, 0, out uint required, IntPtr.Zero);
                if (required < 8)
                    continue;

                IntPtr detail = Marshal.AllocHGlobal(checked((int)required));
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetailW(deviceSet, ref data, detail, required, out _, IntPtr.Zero))
                        throw new Win32Exception();
                    string? path = Marshal.PtrToStringUni(detail + 4);
                    if (path is null || !path.Contains(ReceiverPathFragment, StringComparison.OrdinalIgnoreCase))
                        continue;

                    using var handle = CreateFileW(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                    if (handle.IsInvalid || !HidD_GetPreparsedData(handle, out IntPtr preparsed))
                        continue;
                    try
                    {
                        IntPtr caps = Marshal.AllocHGlobal(64);
                        try
                        {
                            if (HidP_GetCaps(preparsed, caps) != 0x110000)
                                continue;
                            var shape = new ReportShape(
                                (ushort)Marshal.ReadInt16(caps, 2),
                                (ushort)Marshal.ReadInt16(caps, 4),
                                (ushort)Marshal.ReadInt16(caps, 6),
                                (ushort)Marshal.ReadInt16(caps, 8));
                            if (shape.UsagePage == VendorUsagePage)
                                return shape;
                        }
                        finally { Marshal.FreeHGlobal(caps); }
                    }
                    finally { HidD_FreePreparsedData(preparsed); }
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(deviceSet); }
    }
}
