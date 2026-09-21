using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;

internal static class DashboardRenderer
{
    private static readonly Color Back=Color.FromArgb(10,14,21),Panel=Color.FromArgb(115,10,14,21),Muted=Color.FromArgb(235,242,255),Cyan=Color.FromArgb(135,240,255),Red=Color.FromArgb(255,170,172);
    private static readonly Bitmap? Background=LoadBackground();
    private static Bitmap? LoadBackground()
    {
        try
        {
            using var source=Image.FromFile(Path.Combine(AppContext.BaseDirectory,"background.png"));
            var resized=new Bitmap(320,240,PixelFormat.Format32bppArgb);
            using var graphics=Graphics.FromImage(resized);
            graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(source,new Rectangle(0,0,320,240));
            return resized;
        }
        catch { return null; }
    }
    private static string N(double? n,string format="0")=>n.HasValue?n.Value.ToString(format,CultureInfo.InvariantCulture):"--";
    public static Bitmap Draw(Metrics m)
    {
        var bitmap=new Bitmap(320,240,PixelFormat.Format32bppArgb);
        using var g=Graphics.FromImage(bitmap);
        g.Clear(Back);if(Background is not null)g.DrawImageUnscaled(Background,0,0);g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
        using var small=new Font("Segoe UI",10,FontStyle.Bold,GraphicsUnit.Pixel);
        using var label=new Font("Segoe UI",12,FontStyle.Bold,GraphicsUnit.Pixel);
        using var medium=new Font("Segoe UI",15,FontStyle.Bold,GraphicsUnit.Pixel);
        using var large=new Font("Segoe UI",30,FontStyle.Bold,GraphicsUnit.Pixel);
        void Text(string s,Font f,Color c,float x,float y,float width=300){using var b=new SolidBrush(c);using var fmt=new StringFormat{Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap};using var shadow=new SolidBrush(Color.FromArgb(230,0,0,0));g.DrawString(s,f,shadow,new RectangleF(x+1,y+1,width,40),fmt);g.DrawString(s,f,b,new RectangleF(x,y,width,40),fmt);}
        void Rect(Color c,int x,int y,int w,int h){using var b=new SolidBrush(c);g.FillRectangle(b,x,y,w,h);}
        void Bar(int x,int y,double? value,Color c){Rect(Color.FromArgb(40,52,68),x,y,136,4);if(value.HasValue)Rect(c,x,y,(int)Math.Round(136*Math.Clamp(value.Value/100,0,1)),4);}
        // Keep the artwork visible behind the header.
        Text("DARKNESS / SYSTEM",label,Color.White,9,8,215);
        Text(m.Time.ToString("HH:mm:ss"),label,Muted,255,8,63);
        // Statistics are drawn directly over the background artwork.
        Text("CPU",label,Cyan,13,35);Text("i9-11900K",small,Muted,61,37,90);
        Text("GPU",label,Red,170,35);Text("RTX 4080",small,Muted,217,37,90);
        Text(N(m.CpuTemp)+"°C",large,Color.White,11,49,143);
        Text(N(m.GpuTemp)+"°C",large,Color.White,168,49,143);
        Text(m.CpuStatus,small,Muted,14,85,138);Text(m.GpuStatus,small,Muted,171,85,138);
        Text("Clock",small,Muted,14,102,48);Text(N(m.CpuMHz/1000,"0.00")+" GHz",medium,Color.White,63,98,90);
        Text("Core",small,Muted,171,102,46);Text(N(m.GpuMHz)+" MHz",medium,Color.White,217,98,94);
        Text("Load  "+N(m.CpuLoad)+"%",label,Color.White,14,123,139);
        Text("Mem  "+N(m.GpuMemMHz)+" MHz",small,Muted,171,122,139);
        Bar(14,149,m.CpuLoad,Cyan);Bar(171,149,m.GpuLoad,Red);
        Text("Load "+N(m.GpuLoad)+"%",small,Muted,171,135,136);
        Text("RAM",small,Cyan,14,159,32);
        Text(N(m.RamUsedGiB,"0.0")+" / "+N(m.RamTotalGiB,"0.0")+" GiB",small,Color.White,49,159,140);
        Text(N(m.RamLoad)+"%",small,Color.White,185,159,42);
        Rect(Color.FromArgb(40,52,68),232,164,74,4);
        if(m.RamLoad.HasValue)Rect(Cyan,232,164,(int)Math.Round(74*Math.Clamp(m.RamLoad.Value/100,0,1)),4);
        // Network statistics share the same full-screen background.
        Text("NETWORK",small,Muted,13,178,85);Text(m.Network.ToUpperInvariant(),small,Muted,101,178,205);
        Text("DOWN",small,Cyan,13,197,45);Text(N(m.DownMbps,"0.00"),medium,Color.White,55,193,95);Text("Mbps",small,Muted,112,216,45);
        Text("UP",small,Red,172,197,28);Text(N(m.UpMbps,"0.00"),medium,Color.White,199,193,111);Text("Mbps",small,Muted,270,216,40);
        return bitmap;
    }
}
