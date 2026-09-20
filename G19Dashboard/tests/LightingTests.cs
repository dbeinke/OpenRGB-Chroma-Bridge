using System.IO.Pipes;
using Xunit;
public class LightingTests
{
 [Fact] public async Task PipePreservesRgbAndAcceptsReconnection()
 {
  using var server=new LightingServer(rgb=>rgb.ToArray());
  using var timeout=new CancellationTokenSource(5000);
  for(int i=0;i<2;i++)
  {
   using var pipe=new NamedPipeClientStream(".","G19Dashboard.Lighting.v1",PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
   await pipe.ConnectAsync(timeout.Token);
   foreach(var rgb in new[]{new byte[]{255,255,255},new byte[]{255,0,0},new byte[]{0,0,0},new byte[]{3,79,201}})
   {
    await pipe.WriteAsync(rgb.AsMemory(0,1),timeout.Token);
    await pipe.WriteAsync(rgb.AsMemory(1),timeout.Token);
    byte[] reply=new byte[3];await pipe.ReadExactlyAsync(reply,timeout.Token);Assert.Equal(rgb,reply);
   }
  }
 }
}
