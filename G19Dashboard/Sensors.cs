using System.Diagnostics;
using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

internal sealed record Metrics(DateTime Time, double? CpuTemp, double? CpuMHz, double? CpuLoad,
    double? GpuTemp, double? GpuMHz, double? GpuMemMHz, double? GpuLoad, double? VramGiB,
    double? DownMbps, double? UpMbps, string Network, string CpuStatus, string GpuStatus);

internal sealed class Sensors : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct Utilization { public uint Gpu, Memory; }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryInfo { public ulong Total, Free, Used; }
    [DllImport("nvml.dll", CallingConvention=CallingConvention.Cdecl)] private static extern int nvmlInit_v2();
    [DllImport("nvml.dll", CallingConvention=CallingConvention.Cdecl)] private static extern int nvmlShutdown();
    [DllImport("nvml.dll", CallingConvention=CallingConvention.Cdecl)] private static extern int nvmlDeviceGetHandleByIndex_v2(uint index,out IntPtr device);
    [DllImport("nvml.dll", CallingConvention=CallingConvention.Cdecl)] private static extern int nvmlDeviceGetTemperature(IntPtr device,uint sensor,out uint value);
    [DllImport("nvml.dll", CallingConvention=CallingConvention.Cdecl)] private static extern int nvmlDeviceGetClockInfo(IntPtr device,uint type,out uint value);
    [DllImport("nvml.dll", CallingConvention=CallingConvention.Cdecl)] private static extern int nvmlDeviceGetUtilizationRates(IntPtr device,out Utilization value);
    [DllImport("nvml.dll", CallingConvention=CallingConvention.Cdecl)] private static extern int nvmlDeviceGetMemoryInfo(IntPtr device,out MemoryInfo value);
    private IntPtr gpu;
    private bool nvmlReady;
    private string? networkId;
    private long previousRx,previousTx;
    private long previousTicks;
    private readonly string preferredNetwork;
    public Sensors(string network) { preferredNetwork=network; }
    public Metrics Read()
    {
        double? cpuTemp=null,cpuMHz=null,cpuLoad=null,gpuTemp=null,gpuMHz=null,gpuMem=null,gpuLoad=null,vram=null,down=null,up=null;
        string cpuStatus="CORE TEMP UNAVAILABLE",gpuStatus="GPU UNAVAILABLE",network="NO NETWORK";
        try
        {
            var processes=Process.GetProcessesByName("Core Temp");
            bool running=processes.Length>0;
            foreach(var process in processes) process.Dispose();
            if(!running) throw new IOException("Core Temp is not running");
            using var map=MemoryMappedFile.OpenExisting("CoreTempMappingObjectEx",MemoryMappedFileRights.Read);
            using var view=map.CreateViewAccessor(0,4740,MemoryMappedFileAccess.Read);
            int cores=checked((int)view.ReadUInt32(1536)),cpus=checked((int)view.ReadUInt32(1540));
            if(cores<1||cpus<1||cores*cpus>256) throw new IOException("Invalid sensor data");
            bool fahrenheit=view.ReadByte(2684)!=0,delta=view.ReadByte(2685)!=0;
            double hottest=double.MinValue,load=0;
            for(int i=0;i<cores*cpus;i++)
            {
                double t=view.ReadSingle(1544+i*4);
                if(fahrenheit) t=delta?t*5/9:(t-32)*5/9;
                if(delta)t=view.ReadUInt32(1024+(i/cores)*4)-t;
                hottest=Math.Max(hottest,t);
                load+=view.ReadUInt32(i*4);
            }
            double clock=view.ReadSingle(2572);
            if(double.IsFinite(hottest)&&hottest>=0&&hottest<130)cpuTemp=hottest;
            if(double.IsFinite(clock)&&clock>0&&clock<10000)cpuMHz=clock;
            cpuLoad=Math.Clamp(load/(cores*cpus),0,100);
            cpuStatus="HOTTEST CORE";
        }
        catch { }
        try
        {
            if(!nvmlReady) nvmlReady=nvmlInit_v2()==0;
            if(nvmlReady&&gpu==IntPtr.Zero)nvmlDeviceGetHandleByIndex_v2(0,out gpu);
            if(gpu!=IntPtr.Zero)
            {
                if(nvmlDeviceGetTemperature(gpu,0,out uint t)==0)gpuTemp=t;
                if(nvmlDeviceGetClockInfo(gpu,0,out uint clk)==0)gpuMHz=clk;
                if(nvmlDeviceGetClockInfo(gpu,2,out uint mem)==0)gpuMem=mem;
                if(nvmlDeviceGetUtilizationRates(gpu,out var utilization)==0)gpuLoad=utilization.Gpu;
                if(nvmlDeviceGetMemoryInfo(gpu,out var memory)==0)vram=memory.Used/1073741824.0;
                gpuStatus=gpuTemp.HasValue?"GPU CORE":"GPU UNAVAILABLE";
                if(!gpuTemp.HasValue) {gpu=IntPtr.Zero;if(nvmlReady)nvmlShutdown();nvmlReady=false;}
            }
        }
        catch {gpu=IntPtr.Zero;}
        try
        {
            var adapters=NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up&&
                n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211).ToArray();
            var nic=adapters.FirstOrDefault(n=>n.Name.Equals(preferredNetwork,StringComparison.OrdinalIgnoreCase))??
                adapters.OrderByDescending(n=>n.GetIPProperties().GatewayAddresses.Count>0).ThenByDescending(n=>n.Speed).FirstOrDefault();
            if(nic!=null)
            {
                network=nic.Name;
                var stats=nic.GetIPStatistics();long now=Stopwatch.GetTimestamp();
                if(networkId==nic.Id&&previousTicks!=0)
                {
                    double seconds=(now-previousTicks)/(double)Stopwatch.Frequency;
                    if(seconds>0&&stats.BytesReceived>=previousRx&&stats.BytesSent>=previousTx)
                    {down=(stats.BytesReceived-previousRx)*8/seconds/1000000;up=(stats.BytesSent-previousTx)*8/seconds/1000000;}
                }
                previousRx=stats.BytesReceived;previousTx=stats.BytesSent;previousTicks=now;networkId=nic.Id;
            }
            else {networkId=null;previousTicks=0;}
        }
        catch {networkId=null;previousTicks=0;}
        return new(DateTime.Now,cpuTemp,cpuMHz,cpuLoad,gpuTemp,gpuMHz,gpuMem,gpuLoad,vram,down,up,network,cpuStatus,gpuStatus);
    }
    public void Dispose(){if(nvmlReady)nvmlShutdown();}
}
