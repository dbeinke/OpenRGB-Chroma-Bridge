using G19USB;
internal sealed class DirectUsbLcd : IDisposable
{
 private readonly object gate=new();
 private LCD? device;
 public void Open(){lock(gate){if(device is not null)return;var candidate=new LCD();try{candidate.OpenDevice();device=candidate;}catch{candidate.Dispose();throw;}}}
 public void Show(Bitmap bitmap){var pixels=Encode(bitmap);lock(gate){if(device is null)throw new InvalidOperationException("LCD is not open.");device.UpdateScreen(pixels);}}
 public byte[] SetLighting(byte[] rgb){lock(gate){if(device is null)throw new IOException("LCD USB connection unavailable.");return device.SetBacklightExact(rgb[0],rgb[1],rgb[2]);}}
 internal static byte[] Encode(Bitmap bitmap){if(bitmap.Width!=320||bitmap.Height!=240)throw new ArgumentException("Expected a 320x240 dashboard frame.",nameof(bitmap));return G19Helpers.ConvertBitmapToRGB565(bitmap);}
 public void Close(){lock(gate){var old=device;device=null;old?.Dispose();}}
 public void Dispose()=>Close();
}
