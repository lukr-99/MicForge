using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MicForge.Audio;
using MicForge.ViewModels;

namespace MicForge.Controls;

/// <summary>
/// Interactive equalizer curve. Draws the combined frequency response of all bands and
/// lets you drag each band's handle (X = frequency, Y = gain; mouse-wheel = Q on peaks).
/// </summary>
public sealed class EqGraph : FrameworkElement
{
    private const double FMin = 20, FMax = 20000, GMax = 18;
    private const double PadL = 30, PadR = 10, PadT = 10, PadB = 18;

    private static readonly Brush BgBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x18, 0x1B, 0x20)));
    private static readonly Pen GridPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x30, 0x38)), 1));
    private static readonly Pen ZeroPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x3C, 0x44, 0x4E)), 1));
    private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x8A, 0x92, 0x9A)));
    private static readonly Brush CurveBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0xC4, 0xB6)));
    private static readonly Pen CurvePen = Freeze(new Pen(CurveBrush, 2));
    private static readonly Brush HandleBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0xC4, 0xB6)));
    private static readonly Pen HandleStroke = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF6)), 1.5));

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    private int _drag = -1;
    private int _hover = -1;

    public EqGraph()
    {
        MinHeight = 200;
        Focusable = false;
    }

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(EqStageViewModel), typeof(EqGraph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public EqStageViewModel Model
    {
        get => (EqStageViewModel)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    private Rect Plot => new(PadL, PadT, Math.Max(1, ActualWidth - PadL - PadR), Math.Max(1, ActualHeight - PadT - PadB));

    private static double XOf(double f, Rect r)
        => r.Left + (Math.Log10(f) - Math.Log10(FMin)) / (Math.Log10(FMax) - Math.Log10(FMin)) * r.Width;
    private static double FOf(double x, Rect r)
    {
        double t = (x - r.Left) / r.Width;
        return Math.Pow(10, Math.Log10(FMin) + t * (Math.Log10(FMax) - Math.Log10(FMin)));
    }
    private static double YOf(double g, Rect r) => r.Top + (GMax - g) / (2 * GMax) * r.Height;
    private static double GOf(double y, Rect r) => GMax - (y - r.Top) / r.Height * (2 * GMax);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRoundedRectangle(BgBrush, null, new Rect(0, 0, w, h), 6, 6);

        var m = Model;
        if (m == null) return;
        var r = Plot;
        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // Frequency grid + labels.
        (double f, string label)[] fLines =
        {
            (30, "30"), (100, "100"), (300, "300"), (1000, "1k"), (3000, "3k"), (10000, "10k")
        };
        foreach (var (f, label) in fLines)
        {
            double x = XOf(f, r);
            dc.DrawLine(GridPen, new Point(x, r.Top), new Point(x, r.Bottom));
            var ft = Text(label, 10, ppd);
            dc.DrawText(ft, new Point(x - ft.Width / 2, r.Bottom + 3));
        }

        // dB grid + labels.
        foreach (int g in new[] { -12, -6, 0, 6, 12 })
        {
            double y = YOf(g, r);
            dc.DrawLine(g == 0 ? ZeroPen : GridPen, new Point(r.Left, y), new Point(r.Right, y));
            var ft = Text(g > 0 ? "+" + g : g.ToString(), 10, ppd);
            dc.DrawText(ft, new Point(2, y - ft.Height / 2));
        }

        // Combined response curve.
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            bool first = true;
            for (double x = r.Left; x <= r.Right; x += 2)
            {
                double f = FOf(x, r);
                double sum = 0;
                foreach (var b in m.Eq.Bands)
                    if (b.Enabled) sum += b.Filter.MagnitudeDb(f, m.SampleRate);
                double y = YOf(Math.Clamp(sum, -GMax, GMax), r);
                var p = new Point(x, y);
                if (first) { ctx.BeginFigure(p, false, false); first = false; }
                else ctx.LineTo(p, true, false);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, CurvePen, geo);

        // Handles.
        for (int i = 0; i < m.Eq.Bands.Count; i++)
        {
            var b = m.Eq.Bands[i];
            double x = XOf(Math.Clamp(b.Freq, FMin, FMax), r);
            double y = YOf(Math.Clamp(b.GainDb, -GMax, GMax), r);
            double rad = (i == _drag || i == _hover) ? 8 : 6;
            dc.DrawEllipse(HandleBrush, HandleStroke, new Point(x, y), rad, rad);
        }
    }

    private FormattedText Text(string s, double size, double ppd)
        => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, LabelBrush, ppd);

    private int HitHandle(Point p)
    {
        var m = Model;
        if (m == null) return -1;
        var r = Plot;
        for (int i = 0; i < m.Eq.Bands.Count; i++)
        {
            var b = m.Eq.Bands[i];
            double x = XOf(Math.Clamp(b.Freq, FMin, FMax), r);
            double y = YOf(Math.Clamp(b.GainDb, -GMax, GMax), r);
            if ((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y) <= 13 * 13) return i;
        }
        return -1;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        int i = HitHandle(e.GetPosition(this));
        if (i >= 0)
        {
            _drag = i;
            CaptureMouse();
            e.Handled = true;
            InvalidateVisual();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var m = Model;
        if (m == null) return;
        var p = e.GetPosition(this);
        var r = Plot;

        if (_drag >= 0)
        {
            var b = m.Eq.Bands[_drag];
            b.Freq = Math.Clamp(FOf(p.X, r), m.FreqMin[_drag], m.FreqMax[_drag]);
            b.GainDb = Math.Clamp(GOf(p.Y, r), -GMax, GMax);
            m.Eq.UpdateAll();
            m.NotifyParamsChanged();
            InvalidateVisual();
        }
        else
        {
            int h = HitHandle(p);
            Cursor = h >= 0 ? Cursors.SizeAll : Cursors.Arrow;
            if (h != _hover) { _hover = h; InvalidateVisual(); }
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_drag >= 0)
        {
            _drag = -1;
            ReleaseMouseCapture();
            InvalidateVisual();
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var m = Model;
        if (m == null) return;
        int i = _drag >= 0 ? _drag : HitHandle(e.GetPosition(this));
        if (i < 0) return;
        var b = m.Eq.Bands[i];
        if (b.Type == Biquad.FilterType.Peaking)
        {
            b.Q = Math.Clamp(b.Q * (e.Delta > 0 ? 1.12 : 0.89), 0.3, 8);
            m.Eq.UpdateAll();
            m.NotifyParamsChanged();
            InvalidateVisual();
            e.Handled = true;
        }
    }
}
