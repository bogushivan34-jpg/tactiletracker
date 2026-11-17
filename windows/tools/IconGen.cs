// Renders the app icon (same geometry as assets/icon.svg) to PNGs + a multi-size .ico using WPF.
// Build & run:  tools\make-icon.cmd
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static class IconGen
{
    static Color C(string h) { return (Color)ColorConverter.ConvertFromString(h); }

    // Draws on a 512x512 design grid. "detail" drops the dots/gloss for tiny sizes.
    static void Draw(DrawingContext dc, bool detail)
    {
        // Raised lip under the keycap (the "tactile" press shadow)
        dc.DrawRoundedRectangle(new SolidColorBrush(C("#B8300F")), null, new Rect(40, 60, 432, 420), 112, 112);

        // Keycap face
        var face = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        face.GradientStops.Add(new GradientStop(C("#FF8059"), 0));
        face.GradientStops.Add(new GradientStop(C("#FF4F28"), 0.55));
        face.GradientStops.Add(new GradientStop(C("#F2441D"), 1));
        dc.DrawRoundedRectangle(face, null, new Rect(40, 32, 432, 420), 112, 112);

        if (detail)
        {
            // Soft top gloss
            var gloss = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            gloss.GradientStops.Add(new GradientStop(Color.FromArgb(56, 255, 255, 255), 0));
            gloss.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
            dc.DrawRoundedRectangle(gloss, null, new Rect(64, 48, 384, 190), 92, 92);
        }

        var check = Geometry.Parse(detail ? "M164 236 L230 302 L352 176" : "M150 250 L226 326 L366 180");
        double w = detail ? 50 : 64;
        var shadowPen = new Pen(new SolidColorBrush(Color.FromArgb(90, 0xA8, 0x2A, 0x0C)), w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        dc.PushTransform(new TranslateTransform(0, 8));
        dc.DrawGeometry(null, shadowPen, check);
        dc.Pop();
        var pen = new Pen(Brushes.White, w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        dc.DrawGeometry(null, pen, check);

        if (detail)
        {
            // The "Running" wave dots
            double[] op = { 0.5, 0.75, 1.0 };
            double[] dy = { 4, -4, 2 };
            for (int i = 0; i < 3; i++)
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(op[i] * 255), 255, 255, 255)), null, new Point(216 + i * 40, 372 + dy[i]), 14, 14);
        }
    }

    static byte[] Render(int size)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 512.0, size / 512.0));
            Draw(dc, size >= 40);
            dc.Pop();
        }
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        RenderOptions.SetEdgeMode(dv, EdgeMode.Unspecified);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using (var ms = new MemoryStream()) { enc.Save(ms); return ms.ToArray(); }
    }

    [STAThread]
    static void Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : ".";
        int[] sizes = { 256, 64, 48, 40, 32, 24, 20, 16 };
        var pngs = new List<byte[]>();
        foreach (int s in sizes) pngs.Add(Render(s));
        File.WriteAllBytes(Path.Combine(outDir, "icon-512.png"), Render(512));
        File.WriteAllBytes(Path.Combine(outDir, "icon-preview-32.png"), pngs[4]);
        using (var fs = File.Create(Path.Combine(outDir, "app.ico")))
        using (var w = new BinaryWriter(fs))
        {
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                byte d = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                w.Write(d); w.Write(d); w.Write((byte)0); w.Write((byte)0);
                w.Write((ushort)1); w.Write((ushort)32);
                w.Write(pngs[i].Length); w.Write(offset);
                offset += pngs[i].Length;
            }
            foreach (var p in pngs) w.Write(p);
        }
        Console.WriteLine("icon written to " + outDir);
    }
}
