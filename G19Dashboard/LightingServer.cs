using System.IO.Pipes;

// One local, same-user client; requests and replies are exactly three RGB bytes.
internal sealed class LightingServer : IDisposable
{
 private readonly CancellationTokenSource stop=new();
 private readonly Task worker;
 public LightingServer(Func<byte[],byte[]> apply)
 {
  worker=Task.Run(async ()=> {
   while(!stop.IsCancellationRequested)
   {
    try
    {
     using var pipe=new NamedPipeServerStream("G19Dashboard.Lighting.v1",PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
     await pipe.WaitForConnectionAsync(stop.Token);
     var rgb=new byte[3];
     while(!stop.IsCancellationRequested)
     {
      await pipe.ReadExactlyAsync(rgb,stop.Token);
      var actual=apply(rgb);
      await pipe.WriteAsync(actual,stop.Token);
     }
    }
    catch(OperationCanceledException) when(stop.IsCancellationRequested){break;}
    catch { if(!stop.IsCancellationRequested)await Task.Delay(250,stop.Token); }
   }
  });
 }
 public void Dispose(){stop.Cancel();try{worker.GetAwaiter().GetResult();}catch(OperationCanceledException){}stop.Dispose();}
}
