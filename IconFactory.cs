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
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
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

    public static Icon CreateIcon()
    {
        using var bmp = Draw(32);
        IntPtr h = bmp.GetHicon();
        try { using var tmp = Icon.FromHandle(h); return (Icon)tmp.Clone(); }
        finally { DestroyIcon(h); }
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
