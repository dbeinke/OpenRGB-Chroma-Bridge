using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using OpenRGB.NET;

namespace OpenRgbChromaBridge;

// G19 046D:C229 vendor HID collection, feature report 07 followed by R,G,B.
// Protocol: https://lkml.rescloud.iu.edu/hypermail/linux/kernel/1502.2/03681.html
internal sealed class LogitechLighting : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct InterfaceData { public int Size; public Guid Guid; public int Flags; public IntPtr Reserved; }
    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
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
    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, IntPtr caps);
    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] report, int size);
    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetFeature(SafeFileHandle handle, [In, Out] byte[] report, int size);

    private readonly object gate = new();
    private readonly AutoResetEvent changed = new(false);
    private readonly Thread worker;
    private readonly Action<string> log;
    private Color? latest;
    private volatile bool stopping;

    public LogitechLighting(Action<string> log)
    {
        this.log = log;
        worker = new Thread(Run) { IsBackground = true, Name = "Logitech G19 lighting" };
        worker.Start();
    }
    public void Update(Color color)
    {
        lock (gate) latest = color;
        changed.Set();
    }
    private static SafeFileHandle OpenG19()
    {
        HidD_GetHidGuid(out Guid guid);
        IntPtr set = SetupDiGetClassDevsW(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12);
        if (set == new IntPtr(-1)) throw new Win32Exception();
        try
        {
            for (uint index = 0; ; index++)
            {
                var data = new InterfaceData { Size = Marshal.SizeOf<InterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref data))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == 259) break;
                    throw new Win32Exception(error);
                }
                SetupDiGetDeviceInterfaceDetailW(set, ref data, IntPtr.Zero, 0, out uint size, IntPtr.Zero);
                if (size < 8) continue;
                IntPtr detail = Marshal.AllocHGlobal(checked((int)size));
                string? path;
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetailW(set, ref data, detail, size, out _, IntPtr.Zero)) throw new Win32Exception();
                    path = Marshal.PtrToStringUni(detail + 4);
                }
                finally { Marshal.FreeHGlobal(detail); }
                if (path is null || !path.Contains("vid_046d&pid_c229&mi_01", StringComparison.OrdinalIgnoreCase)) continue;
                var handle = CreateFileW(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                bool selected = false;
                try
                {
                    if (handle.IsInvalid || !HidD_GetPreparsedData(handle, out IntPtr parsed)) continue;
                    IntPtr caps = Marshal.AllocHGlobal(64); // sizeof(HIDP_CAPS)
                    try
                    {
                        if (HidP_GetCaps(parsed, caps) != 0x110000) continue;
                        ushort page = (ushort)Marshal.ReadInt16(caps, 2);
                        ushort featureLength = (ushort)Marshal.ReadInt16(caps, 8);
                        if (page != 0xFF00 || featureLength < 4) continue;
                    }
                    finally { Marshal.FreeHGlobal(caps); HidD_FreePreparsedData(parsed); }
                    var report = new byte[] { 7, 0, 0, 0 };
                    if (!HidD_GetFeature(handle, report, report.Length) || report[0] != 7) continue;
                    selected = true;
                    return handle;
                }
                finally { if (!selected) handle.Dispose(); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        throw new IOException("G19 RGB HID interface unavailable");
    }
    private void Run()
    {
        SafeFileHandle? handle = null;
        long retryAt = 0, nextStatus = 0, writtenFrames = 0;
        try
        {
            while (!stopping)
            {
                changed.WaitOne(100);
                if (stopping) break;
                long now = Environment.TickCount64;
                if (now < retryAt) continue;
                try
                {
                    if (handle is null)
                    {
                        handle = OpenG19();
                        log("G19 direct USB lighting connected (046D:C229, feature report 07).");
                        nextStatus = 0;
                    }
                    Color? color;
                    lock (gate) color = latest;
                    if (!color.HasValue) continue;
                    var report = new byte[] { 7, color.Value.R, color.Value.G, color.Value.B };
                    // Repeat static frames too, to recover from LGS profile changes.
                    if (!HidD_SetFeature(handle, report, report.Length)) throw new Win32Exception();
                    writtenFrames++;
                    if (now >= nextStatus)
                    {
                        var readback = new byte[] { 7, 0, 0, 0 };
                        if (!HidD_GetFeature(handle, readback, readback.Length)) throw new Win32Exception();
                        bool matches = report.AsSpan().SequenceEqual(readback);
                        log($"G19 USB: {writtenFrames} frames written; sent #{report[1]:X2}{report[2]:X2}{report[3]:X2}, device readback #{readback[1]:X2}{readback[2]:X2}{readback[3]:X2}, match={matches}.");
                        nextStatus = now + 30000;
                    }
                }
                catch (Exception ex)
                {
                    handle?.Dispose();
                    handle = null;
                    retryAt = now + 5000;
                    log("G19 USB lighting retry in 5 seconds: " + ex.Message);
                }
            }
        }
        finally { handle?.Dispose(); }
    }
    public void Dispose()
    {
        stopping = true;
        changed.Set();
        if (worker.Join(1500)) changed.Dispose();
    }
}
