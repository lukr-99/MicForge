using System;
using System.Windows;
using System.Windows.Controls;

namespace MicForge.Controls;

/// <summary>
/// Column-balancing ("masonry") panel: lays children out in fixed-width columns, each
/// child placed in the currently-shortest column. Keeps every card at its natural height
/// so short cards don't get stretched to match tall ones in the same row.
/// </summary>
public sealed class MasonryPanel : Panel
{
    public static readonly DependencyProperty ColumnWidthProperty = DependencyProperty.Register(
        nameof(ColumnWidth), typeof(double), typeof(MasonryPanel),
        new FrameworkPropertyMetadata(314.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public double ColumnWidth
    {
        get => (double)GetValue(ColumnWidthProperty);
        set => SetValue(ColumnWidthProperty, value);
    }

    private int ColumnCount(double width)
    {
        double colW = ColumnWidth;
        if (double.IsInfinity(width) || width < colW) return 1;
        return Math.Max(1, (int)(width / colW));
    }

    private static int Shortest(double[] heights)
    {
        int idx = 0;
        for (int i = 1; i < heights.Length; i++)
            if (heights[i] < heights[idx]) idx = i;
        return idx;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        int cols = ColumnCount(availableSize.Width);
        var colH = new double[cols];

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(ColumnWidth, double.PositiveInfinity));
            colH[Shortest(colH)] += child.DesiredSize.Height;
        }

        double h = 0;
        foreach (var v in colH) if (v > h) h = v;
        return new Size(cols * ColumnWidth, h);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int cols = ColumnCount(finalSize.Width);
        var colH = new double[cols];

        foreach (UIElement child in InternalChildren)
        {
            int c = Shortest(colH);
            double y = colH[c];
            child.Arrange(new Rect(c * ColumnWidth, y, ColumnWidth, child.DesiredSize.Height));
            colH[c] += child.DesiredSize.Height;
        }

        return finalSize;
    }
}
