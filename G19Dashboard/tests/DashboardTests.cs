using Xunit;
using System.Drawing.Imaging;
public class DashboardTests
{
 [Fact] public void EncodesActualBitmapWithCorrectColorsAndColumnOrder()
 {
  using var image=new Bitmap(320,240,PixelFormat.Format32bppArgb);
  image.SetPixel(0,0,Color.Red); image.SetPixel(0,1,Color.Lime);
  image.SetPixel(1,0,Color.Blue); image.SetPixel(319,239,Color.White);
  var data=DirectUsbLcd.Encode(image);
  Assert.Equal(153600,data.Length);
  Assert.Equal(new byte[]{0,248,224,7},data[..4]);
  Assert.Equal(new byte[]{31,0},data[480..482]);
  Assert.Equal(new byte[]{255,255},data[^2..]);
  Assert.Equal(0,data[4]);
 }
 [Fact] public void RejectsWrongSize()
 {
  using var image=new Bitmap(1,1);
  Assert.Throws<ArgumentException>(()=>DirectUsbLcd.Encode(image));
 }
 [Fact] public void UnopenedTransportRejectsWriteAndClosesIdempotently()
 {
  using var lcd=new DirectUsbLcd();using var image=new Bitmap(320,240);
  Assert.Throws<InvalidOperationException>(()=>lcd.Show(image));
  lcd.Close();lcd.Close();
 }
 [Theory] [InlineData(true)] [InlineData(false)]
 public void RendererProducesEncodableFrame(bool missing)
 {
  double? value=missing?null:65;
  var metrics=new Metrics(new DateTime(2026,9,20,12,34,56),value,value,value,value,value,value,value,value,value,value,"Ethernet 2",missing?"CORE TEMP UNAVAILABLE":"CORE TEMP",missing?"GPU UNAVAILABLE":"NVML");
  using var image=DashboardRenderer.Draw(metrics);
  Assert.Equal(320,image.Width);Assert.Equal(240,image.Height);
  Assert.Equal(153600,DirectUsbLcd.Encode(image).Length);
  image.Save(Path.Combine(AppContext.BaseDirectory,missing?"preview-missing.png":"preview-fixture.png"),ImageFormat.Png);
 }
}
