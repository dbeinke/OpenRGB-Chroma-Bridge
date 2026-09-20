using System.IO.Pipes;
using OpenRGB.NET;
namespace OpenRgbChromaBridge;

// Dashboard owns USB. A persistent local pipe carries the latest animation color.
internal sealed class LogitechLighting : IDisposable
{
 private readonly object gate=new();
 private readonly AutoResetEvent changed=new(false);
 private readonly CancellationTokenSource stop=new();
 private readonly Thread worker;
 private readonly Action<string> log;
 private Color? latest;
 public LogitechLighting(Action<string> log){this.log=log;worker=new Thread(Run){IsBackground=true,Name="G19 lighting pipe"};worker.Start();}
 public void Update(Color color){lock(gate)latest=color;changed.Set();}
 private void Run()
 {
  NamedPipeClientStream? pipe=null;long retryAt=0,nextStatus=0,frames=0;
  try
  {
   while(!stop.IsCancellationRequested)
   {
    changed.WaitOne(100);if(stop.IsCancellationRequested)break;
    long now=Environment.TickCount64;if(now<retryAt)continue;
    try
    {
     Color? color;lock(gate)color=latest;if(!color.HasValue)continue;
     if(pipe is null){pipe=new NamedPipeClientStream(".","G19Dashboard.Lighting.v1",PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);pipe.ConnectAsync(1000,stop.Token).GetAwaiter().GetResult();log("G19 lighting connected through dashboard USB owner.");}
     using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);timeout.CancelAfter(2000);
     byte[] rgb={color.Value.R,color.Value.G,color.Value.B};
     pipe.WriteAsync(rgb,timeout.Token).AsTask().GetAwaiter().GetResult();
     var actual=new byte[3];pipe.ReadExactlyAsync(actual,timeout.Token).AsTask().GetAwaiter().GetResult();frames++;
     if(!rgb.AsSpan().SequenceEqual(actual))throw new IOException("G19 RGB hardware readback mismatch.");
     if(now>=nextStatus){log($"G19 shared USB: {frames} frames; sent #{rgb[0]:X2}{rgb[1]:X2}{rgb[2]:X2}; hardware readback match=True.");nextStatus=now+30000;}
    }
    catch(Exception ex){pipe?.Dispose();pipe=null;retryAt=Environment.TickCount64+5000;if(!stop.IsCancellationRequested)log("G19 lighting retry in 5 seconds: "+ex.Message);}
   }
  }
  finally{pipe?.Dispose();}
 }
 public void Dispose(){stop.Cancel();changed.Set();if(worker.Join(3000)){changed.Dispose();stop.Dispose();}}
}
