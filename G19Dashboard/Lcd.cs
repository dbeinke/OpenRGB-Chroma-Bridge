using G19USB;

// LCD only: no keyboard input, macro handling, brightness or RGB writes.
internal sealed class DirectUsbLcd : IDisposable
{
 private LCD? device;
 public void Open()
 {
  if(device is not null)return;
  var candidate=new LCD();
  try {candidate.OpenDevice();device=candidate;}
  catch {candidate.Dispose();throw;}
 }
 public void Show(Bitmap bitmap)
 {
  if(device is null)throw new InvalidOperationException("LCD is not open.");
  device.UpdateScreen(Encode(bitmap)); // Wait for completion so errors reach the retry loop.
 }
 internal static byte[] Encode(Bitmap bitmap)
 {
  if(bitmap.Width!=320||bitmap.Height!=240)throw new ArgumentException("Expected a 320x240 dashboard frame.",nameof(bitmap));
  return G19Helpers.ConvertBitmapToRGB565(bitmap);
 }
 public void Close(){var old=device;device=null;old?.Dispose();}
 public void Dispose()=>Close();
}
