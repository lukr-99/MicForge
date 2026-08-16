using System;
using System.Windows;
using System.Windows.Media;

namespace MicForge.Controls;

/// <summary>
/// A vertical audio level meter. <see cref="Level"/> is 0..1 (already mapped from dB).
/// Fills bottom-up with a green -> yellow -> red gradient, plus a peak-hold tick.
/// </summary>
public sealed class LevelMeter : FrameworkElement
{
    private static readonly Brush Background;
    private static readonly Brush Fill;
    private static readonly Pen PeakPen;

    private double _peak;
    private DateTime _peakTime;

    static LevelMeter()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x12, 0x14, 0x17));
        Background.Freeze();

        var g = new LinearGradientBrush { StartPoint = new Point(0.5, 1), EndPoint = new Point(0.5, 0) };
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x2F, 0xB8, 0x6B), 0.0));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x36, 0xC7, 0x74), 0.55));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0xE3, 0xC5, 0x3A), 0.80));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0xE5, 0x54, 0x3B), 1.0));
        g.Freeze();
        Fill = g;

        PeakPen = new Pen(new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)), 1.5);
        PeakPen.Freeze();
    }

    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(double), typeof(LevelMeter),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnLevelChanged));

    public double Level
    {
        get => (double)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    private static void OnLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var m = (LevelMeter)d;
        double v = Math.Clamp((double)e.NewValue, 0, 1);
        if (v >= m._peak)
        {
            m._peak = v;
            m._peakTime = DateTime.UtcNow;
        }
        else if ((DateTime.UtcNow - m._peakTime).TotalMilliseconds > 900)
        {
            m._peak = Math.Max(v, m._peak - 0.02); // slow fall
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        const double r = 3;

        dc.DrawRoundedRectangle(Background, null, new Rect(0, 0, w, h), r, r);

        double lv = Math.Clamp(Level, 0, 1);
        double fh = h * lv;
        if (fh > 0)
        {
            var clip = new RectangleGeometry(new Rect(0, 0, w, h), r, r);
            dc.PushClip(clip);
            dc.DrawRectangle(Fill, null, new Rect(0, h - fh, w, fh));
            dc.Pop();
        }

        if (_peak > 0.001)
        {
            double y = h - h * Math.Clamp(_peak, 0, 1);
            dc.DrawLine(PeakPen, new Point(1, y), new Point(w - 1, y));
        }
    }
}
