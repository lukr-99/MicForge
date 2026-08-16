using System;
using System.Windows;
using System.Windows.Media;

namespace MicForge.Controls;

/// <summary>
/// Horizontal level bar with a threshold marker. Used for the gate (level vs open
/// threshold) and the de-esser (sibilance vs threshold). Glows accent when active.
/// </summary>
public sealed class ThresholdMeter : FrameworkElement
{
    private static readonly Brush BgBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x19)));
    private static readonly Brush IdleBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x50, 0x58, 0x62)));
    private static readonly Brush ActiveBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0xC4, 0xB6)));
    private static readonly Pen MarkerPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xF2, 0xC5, 0x3A)), 2));

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    public ThresholdMeter()
    {
        Height = 18;
        IsHitTestVisible = false;
    }

    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(double), typeof(ThresholdMeter),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThresholdProperty = DependencyProperty.Register(
        nameof(Threshold), typeof(double), typeof(ThresholdMeter),
        new FrameworkPropertyMetadata(0.5, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(
        nameof(Active), typeof(bool), typeof(ThresholdMeter),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Level { get => (double)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public double Threshold { get => (double)GetValue(ThresholdProperty); set => SetValue(ThresholdProperty, value); }
    public bool Active { get => (bool)GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRoundedRectangle(BgBrush, null, new Rect(0, 0, w, h), 4, 4);

        double lv = Math.Clamp(Level, 0, 1);
        double fw = w * lv;
        if (fw > 0)
        {
            var clip = new RectangleGeometry(new Rect(0, 0, w, h), 4, 4);
            dc.PushClip(clip);
            dc.DrawRectangle(Active ? ActiveBrush : IdleBrush, null, new Rect(0, 0, fw, h));
            dc.Pop();
        }

        double tx = w * Math.Clamp(Threshold, 0, 1);
        dc.DrawLine(MarkerPen, new Point(tx, 1), new Point(tx, h - 1));
    }
}
