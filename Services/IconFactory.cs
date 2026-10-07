using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace MicForge;

/// <summary>Draws the MicForge icon at runtime (no .ico asset needed).</summary>
internal static class IconFactory
{
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);

    private static Bitmap Draw(int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        using (var bg = new SolidBrush(Color.FromArgb(0x2E, 0xC4, 0xB6)))
            g.FillEllipse(bg, 0.5f, 0.5f, size - 1f, size - 1f);

        var dark = Color.FromArgb(0x14, 0x16, 0x19);
        float w = size * 0.30f, h = size * 0.42f;
        float x = (size - w) / 2f, y = size * 0.17f;

        using (var body = new SolidBrush(dark))
        using (var path = Capsule(x, y, w, h))
            g.FillPath(body, path);

        using (var pen = new Pen(dark, Math.Max(1f, size * 0.045f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            float cx = size / 2f;
            g.DrawArc(pen, x - size * 0.07f, y + h * 0.30f, w + size * 0.14f, h * 0.85f, 25, 130);
            g.DrawLine(pen, cx, y + h + size * 0.11f, cx, size * 0.84f);
        }
        return bmp;
    }

    private static GraphicsPath Capsule(float x, float y, float w, float h)
    {
        float r = w / 2f;
        var p = new GraphicsPath();
        p.AddArc(x, y, w, w, 180, 180);
        p.AddArc(x, y + h - w, w, w, 0, 180);
        p.CloseFigure();
        return p;
    }

    public enum TrayState { Stopped, Live, Muted }

    public static byte[] CreateTrayIcon(TrayState state) =>
        DotNetLib.Tray.IconFile.Create(DotNetLib.Tray.IconFile.TraySizes, size =>
            DotNetLib.Tray.IconFile.Render(size, (context, _) =>
            {
                using var bitmap = DrawStatus(size, state);
                using var stream = new System.IO.MemoryStream();
                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                stream.Position = 0;
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                context.DrawImage(frame, new Rect(0, 0, size, size));
            }));

    private static Bitmap DrawStatus(int size, TrayState state)
    {
        var bmp = Draw(size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (state == TrayState.Live)
            {
                using var dot = new SolidBrush(Color.FromArgb(0x7F, 0xB0, 0x69));
                using var ring = new Pen(Color.FromArgb(0x14, 0x16, 0x19), 1.5f);
                g.FillEllipse(dot, size * 0.56f, size * 0.56f, size * 0.40f, size * 0.40f);
                g.DrawEllipse(ring, size * 0.56f, size * 0.56f, size * 0.40f, size * 0.40f);
            }
            else if (state == TrayState.Muted)
            {
                using var pen = new Pen(Color.FromArgb(0xE5, 0x54, 0x3B), Math.Max(2f, size * 0.10f))
                { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(pen, size * 0.20f, size * 0.22f, size * 0.80f, size * 0.78f);
            }
        }

        return bmp;
    }

    public static System.Windows.Media.ImageSource CreateImageSource()
    {
        using var bmp = Draw(64);
        IntPtr hb = bmp.GetHbitmap();
        try
        {
            var src = Imaging.CreateBitmapSourceFromHBitmap(
                hb, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally { DeleteObject(hb); }
    }
}
