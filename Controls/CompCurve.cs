using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MicForge.ViewModels;

namespace MicForge.Controls;

/// <summary>
/// Compressor transfer curve: input level (x) vs output level (y) in dB, showing the
/// threshold, ratio, knee and makeup gain. A live dot marks the current input level.
/// </summary>
public sealed class CompCurve : FrameworkElement
{
    private const double DbMin = -60, DbMax = 0, OutMax = 6;
    private const double PadL = 28, PadR = 8, PadT = 8, PadB = 16;

    private static readonly Brush BgBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x18, 0x1B, 0x20)));
    private static readonly Pen GridPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x30, 0x38)), 1));
    private static readonly Pen UnityPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x39, 0x40, 0x49)), 1) { DashStyle = DashStyles.Dash });
    private static readonly Pen ThreshPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x5A, 0x63, 0x6E)), 1) { DashStyle = DashStyles.Dash });
    private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x8A, 0x92, 0x9A)));
    private static readonly Pen CurvePen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xE3, 0xB2, 0x3C)), 2));
    private static readonly Brush DotBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF6)));

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    public CompCurve()
    {
        MinHeight = 150;
        IsHitTestVisible = false;
    }

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(CompressorStageViewModel), typeof(CompCurve),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public CompressorStageViewModel Model
    {
        get => (CompressorStageViewModel)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    public static readonly DependencyProperty LevelDbProperty = DependencyProperty.Register(
        nameof(LevelDb), typeof(double), typeof(CompCurve),
        new FrameworkPropertyMetadata(-100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Current input level in dB, for the moving dot.</summary>
    public double LevelDb
    {
        get => (double)GetValue(LevelDbProperty);
        set => SetValue(LevelDbProperty, value);
    }

    private Rect Plot => new(PadL, PadT, Math.Max(1, ActualWidth - PadL - PadR), Math.Max(1, ActualHeight - PadT - PadB));

    private static double XOf(double db, Rect r) => r.Left + (db - DbMin) / (DbMax - DbMin) * r.Width;
    private static double YOf(double db, Rect r) => r.Bottom - (db - DbMin) / (OutMax - DbMin) * r.Height;

    private static double OutDb(double inDb, double thr, double ratio, double knee, double makeup)
    {
        double over = inDb - thr;
        double gainDb;
        if (2 * over < -knee) gainDb = 0;
        else if (knee > 0 && 2 * Math.Abs(over) <= knee)
        {
            double t = over + knee / 2;
            gainDb = (1.0 / ratio - 1.0) * (t * t) / (2 * knee);
        }
        else gainDb = (thr + over / ratio) - inDb;
        return inDb + gainDb + makeup;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRoundedRectangle(BgBrush, null, new Rect(0, 0, w, h), 6, 6);

        var m = Model;
        if (m == null) return;
        var r = Plot;
        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var c = m.Compressor;

        foreach (int db in new[] { -48, -36, -24, -12, 0 })
        {
            double x = XOf(db, r);
            dc.DrawLine(GridPen, new Point(x, r.Top), new Point(x, r.Bottom));
            var ft = Text(db.ToString(), 9, ppd);
            dc.DrawText(ft, new Point(x - ft.Width / 2, r.Bottom + 2));
        }
        foreach (int db in new[] { -48, -24, 0 })
        {
            double y = YOf(db, r);
            var ft = Text(db.ToString(), 9, ppd);
            dc.DrawText(ft, new Point(1, y - ft.Height / 2));
        }

        // 1:1 reference.
        dc.DrawLine(UnityPen, new Point(XOf(DbMin, r), YOf(DbMin, r)), new Point(XOf(DbMax, r), YOf(DbMax, r)));

        // Threshold marker.
        double tx = XOf(Math.Clamp(c.ThresholdDb, DbMin, DbMax), r);
        dc.DrawLine(ThreshPen, new Point(tx, r.Top), new Point(tx, r.Bottom));

        // Transfer curve.
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            bool first = true;
            for (double db = DbMin; db <= DbMax; db += 1)
            {
                double o = Math.Clamp(OutDb(db, c.ThresholdDb, c.Ratio, c.KneeDb, c.MakeupDb), DbMin, OutMax);
                var p = new Point(XOf(db, r), YOf(o, r));
                if (first) { ctx.BeginFigure(p, false, false); first = false; }
                else ctx.LineTo(p, true, false);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, CurvePen, geo);

        // Live level dot.
        if (LevelDb > DbMin + 0.5)
        {
            double inDb = Math.Clamp(LevelDb, DbMin, DbMax);
            double o = Math.Clamp(OutDb(inDb, c.ThresholdDb, c.Ratio, c.KneeDb, c.MakeupDb), DbMin, OutMax);
            dc.DrawEllipse(DotBrush, null, new Point(XOf(inDb, r), YOf(o, r)), 4, 4);
        }
    }

    private FormattedText Text(string s, double size, double ppd)
        => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, LabelBrush, ppd);
}
