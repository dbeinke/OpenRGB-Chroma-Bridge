using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;

internal sealed class Config
{
 public string NetworkAdapter {get;set;}="Ethernet 2";
 public int RefreshMilliseconds {get;set;}=1000;
}
internal static class Program
{
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern bool SetDllDirectory(string path);
 private static readonly string DataPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"G19 Dashboard");
 private static void Log(string text){Directory.CreateDirectory(DataPath);File.AppendAllText(Path.Combine(DataPath,"dashboard.log"),$"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}\n");}
 [STAThread] private static int Main(string[] args)
 {
  using var mutex=new Mutex(true,"Local\\G19-System-Dashboard",out bool owns);
  if(!owns)return 0;
  Directory.CreateDirectory(DataPath);
  Config config=new();
  var configPath=Path.Combine(AppContext.BaseDirectory,"dashboard-config.json");
  try {if(File.Exists(configPath))config=JsonSerializer.Deserialize<Config>(File.ReadAllText(configPath))??new();}catch(Exception ex){Log("Config: "+ex.Message);}
  using var sensors=new Sensors(config.NetworkAdapter);
  if(args.Contains("--preview"))
  {
   sensors.Read();Thread.Sleep(1100);var metrics=sensors.Read();
   using var bitmap=DashboardRenderer.Draw(metrics);
   bitmap.Save(Path.Combine(DataPath,"preview.png"),ImageFormat.Png);
   File.WriteAllText(Path.Combine(DataPath,"metrics.json"),JsonSerializer.Serialize(metrics,new JsonSerializerOptions{WriteIndented=true}));
   return 0;
  }
  bool ready=false;DateTime nextConnect=DateTime.MinValue,nextLog=DateTime.MinValue;
  SetDllDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Logitech Gaming Software","SDK","LCD","x64"));
  try
  {
   Log("Dashboard started.");
   while(true)
   {
    var metrics=sensors.Read();
    using var bitmap=DashboardRenderer.Draw(metrics);
    try
    {
     if(!ready&&DateTime.UtcNow>=nextConnect)
     {ready=LcdProbe.LogiLcdInit("G19 System Dashboard",2);nextConnect=DateTime.UtcNow.AddSeconds(5);Log("LCD initialize: "+ready);}
     if(ready&&LcdProbe.LogiLcdIsConnected(2))
     {
      var data=bitmap.LockBits(new Rectangle(0,0,320,240),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
      byte[] pixels=new byte[320*240*4];
      try {Marshal.Copy(data.Scan0,pixels,0,pixels.Length);}finally{bitmap.UnlockBits(data);}
      if(!LcdNative.LogiLcdColorSetBackground(pixels))throw new IOException("LCD rejected frame");
      LcdProbe.LogiLcdUpdate();
     }
     else if(ready){LcdProbe.LogiLcdShutdown();ready=false;Log("LCD disconnected; retrying.");}
    }
    catch(Exception ex){Log("LCD: "+ex.Message);try{LcdProbe.LogiLcdShutdown();}catch{} ready=false;nextConnect=DateTime.UtcNow.AddSeconds(5);}
    if(DateTime.UtcNow>=nextLog)
    {
     File.WriteAllText(Path.Combine(DataPath,"metrics.json"),JsonSerializer.Serialize(metrics,new JsonSerializerOptions{WriteIndented=true}));
     bitmap.Save(Path.Combine(DataPath,"preview.png"),ImageFormat.Png);
     Log($"CPU {metrics.CpuTemp:0}C {metrics.CpuMHz:0}MHz; GPU {metrics.GpuTemp:0}C {metrics.GpuMHz:0}MHz; LCD={ready}");
     nextLog=DateTime.UtcNow.AddSeconds(30);
    }
    Application.DoEvents();Thread.Sleep(Math.Clamp(config.RefreshMilliseconds,500,5000));
   }
  }
  catch(Exception ex){Log("Stopped: "+ex);return 1;}
  finally{if(ready)LcdProbe.LogiLcdShutdown();}
 }
}
internal static class LcdNative
{
 [DllImport(@"C:\Program Files\Logitech Gaming Software\SDK\LCD\x64\LogitechLcd.dll",CallingConvention=CallingConvention.Cdecl)]
 [return:MarshalAs(UnmanagedType.I1)]internal static extern bool LogiLcdColorSetBackground(byte[] bytes);
}
