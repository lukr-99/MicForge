using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace MicForge.Controls;

/// <summary>
/// Scrolling gain-reduction history. Push a dB value via <see cref="Gr"/> each UI tick;
/// it draws a right-to-left filled history (more reduction = taller from the top).
/// </summary>
public sealed class GrHistory : FrameworkElement
{
    private const double MaxGr = 24;
    private const int Points = 240;

    private static readonly Brush BgBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x18, 0x1B, 0x20)));
    private static readonly Pen GridPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x30, 0x38)), 1));
    private static readonly Brush FillBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x55, 0xE3, 0xB2, 0x3C)));
    private static readonly Pen LinePen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xE3, 0xB2, 0x3C)), 1.5));
    private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x8A, 0x92, 0x9A)));

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    private readonly double[] _buf = new double[Points];
    private int _pos;

    public GrHistory() { IsHitTestVisible = false; }

    public static readonly DependencyProperty GrProperty = DependencyProperty.Register(
        nameof(Gr), typeof(double), typeof(GrHistory),
        new FrameworkPropertyMetadata(0.0, OnGrChanged));

    public double Gr { get => (double)GetValue(GrProperty); set => SetValue(GrProperty, value); }

    private static void OnGrChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var g = (GrHistory)d;
        g._buf[g._pos] = Math.Max(0, (double)e.NewValue);
        g._pos = (g._pos + 1) % Points;
        g.InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRoundedRectangle(BgBrush, null, new Rect(0, 0, w, h), 6, 6);

        for (int g = 6; g < MaxGr; g += 6)
        {
            double y = g / MaxGr * h;
            dc.DrawLine(GridPen, new Point(0, y), new Point(w, y));
        }

        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var lc = line.Open())
        using (var ac = area.Open())
        {
            bool first = true;
            for (int i = 0; i < Points; i++)
            {
                double v = _buf[(_pos + i) % Points];
                double x = i / (double)(Points - 1) * w;
                double y = Math.Clamp(v / MaxGr, 0, 1) * h;
                var p = new Point(x, y);
                if (first)
                {
                    lc.BeginFigure(p, false, false);
                    ac.BeginFigure(new Point(x, 0), true, true);
                    ac.LineTo(p, true, false);
                    first = false;
                }
                else { lc.LineTo(p, true, false); ac.LineTo(p, true, false); }
            }
            ac.LineTo(new Point(w, 0), true, false);
        }
        line.Freeze(); area.Freeze();
        dc.DrawGeometry(FillBrush, null, area);
        dc.DrawGeometry(null, LinePen, line);

        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var ft = new FormattedText("-" + MaxGr + " dB", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 9, LabelBrush, ppd);
        dc.DrawText(ft, new Point(3, h - ft.Height - 2));
    }
}
