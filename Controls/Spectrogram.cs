using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MicForge.Audio;
using MicForge.ViewModels;

namespace MicForge.Controls;

/// <summary>
/// Scrolling spectrogram / waterfall of the processed output. Time runs left→right (newest
/// on the right edge); the vertical axis is log-frequency; brightness/colour is level. Fed
/// from the chain's output-spectrum tap each UI tick.
/// </summary>
public sealed class Spectrogram : FrameworkElement
{
    private const int N = 1024;                 // FFT size
    private const double FMin = 30, FMax = 18000;

    private readonly float[] _samples = new float[N];
    private readonly float[] _re = new float[N];
    private readonly float[] _im = new float[N];
    private readonly float[] _win = new float[N];
    private readonly double[] _mag = new double[N / 2];
    private readonly DispatcherTimer _anim = new() { Interval = TimeSpan.FromMilliseconds(40) };

    private WriteableBitmap _bmp;
    private int[] _px;
    private int _w, _h;
    private int[] _rowBin;

    private static readonly Brush BgBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x12, 0x15, 0x1A)));
    private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x8A, 0x92, 0x9A)));
    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    public Spectrogram()
    {
        MinHeight = 140;
        Focusable = false;
        for (int i = 0; i < N; i++)
            _win[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (N - 1)));

        _anim.Tick += (_, _) => Advance();
        Loaded += (_, _) => { if (IsVisible) _anim.Start(); };
        Unloaded += (_, _) => _anim.Stop();
        IsVisibleChanged += (_, _) => { if (IsVisible) _anim.Start(); else _anim.Stop(); };
    }

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(EqStageViewModel), typeof(Spectrogram), new PropertyMetadata(null));
    public EqStageViewModel Model
    {
        get => (EqStageViewModel)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    private void EnsureBuffers()
    {
        int w = Math.Max(1, (int)ActualWidth), h = Math.Max(1, (int)ActualHeight);
        if (w == _w && h == _h && _bmp != null) return;
        _w = w; _h = h;
        _px = new int[w * h];
        int bg = unchecked((int)0xFF12151A);
        for (int i = 0; i < _px.Length; i++) _px[i] = bg;
        _bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);

        // Precompute the FFT bin for each pixel row (top = high freq).
        _rowBin = new int[h];
        double sr = Model?.SampleRate ?? 48000;
        double lmin = Math.Log10(FMin), lmax = Math.Log10(FMax);
        for (int y = 0; y < h; y++)
        {
            double f = Math.Pow(10, lmax - (y / (double)(h - 1)) * (lmax - lmin));
            int bin = (int)Math.Round(f * N / sr);
            _rowBin[y] = Math.Clamp(bin, 1, N / 2 - 1);
        }
    }

    private void Advance()
    {
        if (!IsVisible || ActualWidth < 2 || ActualHeight < 2) return;
        var m = Model;
        if (m?.Chain == null) return;

        EnsureBuffers();
        ComputeMagnitudes(m.Chain.CopyOutputSpectrum);

        int w = _w, h = _h;
        // Scroll every row left by one pixel, write the new column on the right.
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            Array.Copy(_px, row + 1, _px, row, w - 1);
            double v = _mag[_rowBin[y]];
            _px[row + w - 1] = Ramp(v);
        }
        _bmp.WritePixels(new Int32Rect(0, 0, w, h), _px, w * 4, 0);
        InvalidateVisual();
    }

    private void ComputeMagnitudes(Action<float[]> copy)
    {
        copy(_samples);
        for (int i = 0; i < N; i++) { _re[i] = _samples[i] * _win[i]; _im[i] = 0; }
        Fft.Forward(_re, _im);
        int bins = N / 2;
        for (int k = 0; k < bins; k++)
        {
            double mag = Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]) / (N / 2);
            double dbfs = 20 * Math.Log10(mag + 1e-9);
            _mag[k] = Math.Clamp((dbfs + 80) / 70.0, 0, 1);
        }
    }

    // Level → colour ramp (dark → blue → teal → amber → red).
    private static int Ramp(double v)
    {
        (double p, int r, int g, int b)[] stops =
        {
            (0.00, 0x12, 0x15, 0x1A),
            (0.35, 0x1E, 0x50, 0x6E),
            (0.60, 0x2E, 0xC4, 0xB6),
            (0.80, 0xE3, 0xB2, 0x3C),
            (1.00, 0xE5, 0x54, 0x3B),
        };
        v = Math.Clamp(v, 0, 1);
        for (int i = 1; i < stops.Length; i++)
        {
            if (v <= stops[i].p)
            {
                var a = stops[i - 1]; var c = stops[i];
                double t = (v - a.p) / (c.p - a.p + 1e-9);
                int r = (int)(a.r + (c.r - a.r) * t);
                int g = (int)(a.g + (c.g - a.g) * t);
                int b = (int)(a.b + (c.b - a.b) * t);
                return unchecked((int)0xFF000000 | (r << 16) | (g << 8) | b);
            }
        }
        return unchecked((int)0xFFE5543B);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        var clip = new RectangleGeometry(new Rect(0, 0, w, h), 6, 6);
        dc.PushClip(clip);
        dc.DrawRectangle(BgBrush, null, new Rect(0, 0, w, h));
        if (_bmp != null) dc.DrawImage(_bmp, new Rect(0, 0, w, h));

        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double lmin = Math.Log10(FMin), lmax = Math.Log10(FMax);
        foreach (var (f, label) in new[] { (100.0, "100"), (1000.0, "1k"), (5000.0, "5k"), (10000.0, "10k") })
        {
            double y = (lmax - Math.Log10(f)) / (lmax - lmin) * h;
            var ft = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 9.5, LabelBrush, ppd);
            dc.DrawText(ft, new Point(4, y - ft.Height / 2));
        }
        dc.Pop();
    }
}
