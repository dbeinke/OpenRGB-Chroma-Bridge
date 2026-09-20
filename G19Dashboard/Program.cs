using System.Drawing.Imaging;

using System.Text.Json;

internal sealed class Config
{
 public string NetworkAdapter {get;set;}="Ethernet 2";
 public int RefreshMilliseconds {get;set;}=1000;
}
internal static class Program
{
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
  using var lcd=new DirectUsbLcd();
  using var lighting=new LightingServer(lcd.SetLighting);
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
     {lcd.Open();ready=true;Log("LCD direct USB initialized.");}
     if(ready)lcd.Show(bitmap);
    }
    catch(Exception ex){Log("LCD: "+ex.Message);try{lcd.Close();}catch(Exception closeEx){Log("LCD close: "+closeEx.Message);} ready=false;nextConnect=DateTime.UtcNow.AddSeconds(5);}
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
  finally{lcd.Close();}
 }
}
