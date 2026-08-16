using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MicForge.Audio;
using MicForge.ViewModels;

namespace MicForge.Controls;

/// <summary>
/// Interactive equalizer curve with a live spectrum analyzer behind it. Each handle sits
/// ON the combined response curve; dragging moves the curve to the cursor (X = frequency,
/// Y = gain), mouse-wheel changes Q on bell bands. A readout shows exact frequency/gain.
/// </summary>
public sealed class EqGraph : FrameworkElement
{
    private const double FMin = 20, FMax = 20000, GMax = 18;
    private const double PadL = 34, PadR = 12, PadT = 12, PadB = 20;
    private const int N = 1024;   // FFT size

    private static readonly Brush BgBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x18, 0x1B, 0x20)));
    private static readonly Pen GridPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x30, 0x38)), 1));
    private static readonly Pen ZeroPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x44, 0x4C, 0x56)), 1));
    private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xAA, 0xB1, 0xB9)));
    private static readonly Brush SpecBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x4A, 0x6E, 0x84, 0x99)));
    private static readonly Brush CurveBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0xC4, 0xB6)));
    private static readonly Pen CurvePen = Freeze(new Pen(CurveBrush, 2));
    private static readonly Brush FillBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x24, 0x2E, 0xC4, 0xB6)));
    private static readonly Brush HandleBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0xC4, 0xB6)));
    private static readonly Pen HandleStroke = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF6)), 1.5));
    private static readonly Brush TagBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x36)));
    private static readonly Brush TagText = Freeze(new SolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF6)));

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    private readonly float[] _samples = new float[N];
    private readonly float[] _re = new float[N];
    private readonly float[] _im = new float[N];
    private readonly float[] _win = new float[N];
    private readonly double[] _spec = new double[N / 2];   // smoothed 0..1 magnitudes
    private readonly DispatcherTimer _anim = new() { Interval = TimeSpan.FromMilliseconds(33) };

    private int _drag = -1;
    private int _hover = -1;

    public EqGraph()
    {
        MinHeight = 200;
        Focusable = false;
        for (int i = 0; i < N; i++)
            _win[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (N - 1)));   // Hann

        _anim.Tick += (_, _) => { if (IsVisible) InvalidateVisual(); };
        Loaded += (_, _) => { if (IsVisible) _anim.Start(); };
        Unloaded += (_, _) => _anim.Stop();
        IsVisibleChanged += (_, _) => { if (IsVisible) _anim.Start(); else _anim.Stop(); };
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

    private double CombinedDb(double f)
    {
        double sum = 0;
        foreach (var b in Model.Eq.Bands)
            if (b.Enabled) sum += b.Filter.MagnitudeDb(f, Model.SampleRate);
        return sum;
    }

    private Point HandlePoint(int i, Rect r)
    {
        var b = Model.Eq.Bands[i];
        double f = Math.Clamp(b.Freq, FMin, FMax);
        return new Point(XOf(f, r), YOf(Math.Clamp(CombinedDb(f), -GMax, GMax), r));
    }

    private void UpdateSpectrum()
    {
        Model.Chain.CopySpectrum(_samples);
        for (int i = 0; i < N; i++) { _re[i] = _samples[i] * _win[i]; _im[i] = 0; }
        Fft.Forward(_re, _im);
        int bins = N / 2;
        for (int k = 0; k < bins; k++)
        {
            double mag = Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]) / (N / 2);
            double dbfs = 20 * Math.Log10(mag + 1e-9);
            double norm = Math.Clamp((dbfs + 80) / 70.0, 0, 1);
            _spec[k] = Math.Max(norm, _spec[k] * 0.88);   // fast rise, slow fall
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRoundedRectangle(BgBrush, null, new Rect(0, 0, w, h), 6, 6);

        var m = Model;
        if (m == null) return;
        var r = Plot;
        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // Live spectrum behind everything.
        if (m.Chain != null)
        {
            UpdateSpectrum();
            int bins = N / 2;
            double sr = m.SampleRate;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(r.Left, r.Bottom), true, true);
                for (double x = r.Left; x <= r.Right; x += 2)
                {
                    double kf = FOf(x, r) * N / sr;
                    int k0 = (int)kf;
                    if (k0 > bins - 2) k0 = bins - 2;
                    if (k0 < 0) k0 = 0;
                    double frac = kf - k0;
                    double val = _spec[k0] * (1 - frac) + _spec[k0 + 1] * frac;
                    g.LineTo(new Point(x, r.Bottom - val * r.Height), true, false);
                }
                g.LineTo(new Point(r.Right, r.Bottom), true, false);
            }
            geo.Freeze();
            dc.DrawGeometry(SpecBrush, null, geo);
        }

        // Grids + labels.
        (double f, string label)[] fLines =
        {
            (30, "30"), (100, "100"), (300, "300"), (1000, "1k"), (3000, "3k"), (10000, "10k")
        };
        foreach (var (f, label) in fLines)
        {
            double x = XOf(f, r);
            dc.DrawLine(GridPen, new Point(x, r.Top), new Point(x, r.Bottom));
            var ft = Text(label, 10.5, ppd);
            dc.DrawText(ft, new Point(x - ft.Width / 2, r.Bottom + 3));
        }
        foreach (int g in new[] { -12, -6, 0, 6, 12 })
        {
            double y = YOf(g, r);
            dc.DrawLine(g == 0 ? ZeroPen : GridPen, new Point(r.Left, y), new Point(r.Right, y));
            var ft = Text(g > 0 ? "+" + g : g.ToString(), 10.5, ppd);
            dc.DrawText(ft, new Point(r.Left - ft.Width - 5, y - ft.Height / 2));
        }

        // Combined response curve with a soft fill.
        var curve = new StreamGeometry();
        var area = new StreamGeometry();
        double y0 = YOf(0, r);
        using (var cc = curve.Open())
        using (var ac = area.Open())
        {
            bool first = true;
            Point last = default;
            for (double x = r.Left; x <= r.Right; x += 2)
            {
                double y = YOf(Math.Clamp(CombinedDb(FOf(x, r)), -GMax, GMax), r);
                var p = new Point(x, y);
                if (first)
                {
                    cc.BeginFigure(p, false, false);
                    ac.BeginFigure(new Point(x, y0), true, true);
                    ac.LineTo(p, true, false);
                    first = false;
                }
                else { cc.LineTo(p, true, false); ac.LineTo(p, true, false); }
                last = p;
            }
            ac.LineTo(new Point(last.X, y0), true, false);
        }
        curve.Freeze(); area.Freeze();
        dc.DrawGeometry(FillBrush, null, area);
        dc.DrawGeometry(null, CurvePen, curve);

        for (int i = 0; i < m.Eq.Bands.Count; i++)
        {
            var p = HandlePoint(i, r);
            double rad = (i == _drag || i == _hover) ? 8 : 6;
            dc.DrawEllipse(HandleBrush, HandleStroke, p, rad, rad);
        }

        int active = _drag >= 0 ? _drag : _hover;
        if (active >= 0) DrawReadout(dc, active, r, ppd);
    }

    private void DrawReadout(DrawingContext dc, int i, Rect r, double ppd)
    {
        var b = Model.Eq.Bands[i];
        string freq = b.Freq >= 1000 ? $"{b.Freq / 1000:0.0} kHz" : $"{b.Freq:0} Hz";
        string gain = $"{b.GainDb:+0.0;-0.0;0.0} dB";
        var ft = Text($"{freq}   {gain}", 11.5, ppd, TagText);

        var hp = HandlePoint(i, r);
        double pad = 6;
        double tw = ft.Width + pad * 2, th = ft.Height + pad;
        double tx = Math.Clamp(hp.X - tw / 2, r.Left, r.Right - tw);
        double ty = hp.Y - th - 12;
        if (ty < r.Top) ty = hp.Y + 14;

        dc.DrawRoundedRectangle(TagBrush, null, new Rect(tx, ty, tw, th), 4, 4);
        dc.DrawText(ft, new Point(tx + pad, ty + pad / 2));
    }

    private FormattedText Text(string s, double size, double ppd, Brush brush = null)
        => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, brush ?? LabelBrush, ppd);

    private int HitHandle(Point p)
    {
        if (Model == null) return -1;
        var r = Plot;
        int best = -1;
        double bestD = 15 * 15;
        for (int i = 0; i < Model.Eq.Bands.Count; i++)
        {
            var hp = HandlePoint(i, r);
            double d = (p.X - hp.X) * (p.X - hp.X) + (p.Y - hp.Y) * (p.Y - hp.Y);
            if (d <= bestD) { bestD = d; best = i; }
        }
        return best;
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
            m.Eq.UpdateAll();

            double target = Math.Clamp(GOf(p.Y, r), -GMax, GMax);
            double current = CombinedDb(b.Freq);
            b.GainDb = Math.Clamp(b.GainDb + (target - current), -GMax, GMax);
            m.Eq.UpdateAll();

            m.NotifyParamsChanged();
            InvalidateVisual();
        }
        else
        {
            int hOld = _hover;
            _hover = HitHandle(p);
            Cursor = _hover >= 0 ? Cursors.SizeAll : Cursors.Arrow;
            if (_hover != hOld) InvalidateVisual();
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

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_drag < 0 && _hover != -1) { _hover = -1; InvalidateVisual(); }
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
