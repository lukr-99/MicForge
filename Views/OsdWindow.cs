using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MicForge;

/// <summary>
/// A small, click-through, always-on-top overlay that briefly flashes the mute state
/// (e.g. after a global hotkey while the main window is hidden or unfocused).
/// </summary>
public sealed class OsdWindow : Window
{
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20, WsExNoActivate = 0x08000000, WsExToolWindow = 0x80;

    private readonly Ellipse _dot;
    private readonly TextBlock _text;
    private readonly DispatcherTimer _hide = new() { Interval = TimeSpan.FromMilliseconds(1100) };

    public OsdWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        IsHitTestVisible = false;
        Focusable = false;
        Opacity = 0;

        _dot = new Ellipse { Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center };
        _text = new TextBlock
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_dot);
        row.Children.Add(_text);

        Content = new Border
        {
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(22, 12, 24, 12),
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x1E, 0x20, 0x24)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x34, 0x3A, 0x42)),
            BorderThickness = new Thickness(1),
            Child = row
        };

        SizeChanged += (_, _) => Reposition();
        _hide.Tick += (_, _) => { _hide.Stop(); Fade(0); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, ex | WsExTransparent | WsExNoActivate | WsExToolWindow);
    }

    public void Flash(bool muted)
    {
        _text.Text = muted ? "MIC MUTED" : "MIC LIVE";
        _dot.Fill = new SolidColorBrush(muted ? Color.FromRgb(0xE5, 0x54, 0x3B) : Color.FromRgb(0x2F, 0xB8, 0x6B));
        if (!IsVisible) Show();
        Reposition();
        _hide.Stop();
        Fade(1);
        _hide.Start();
    }

    private void Reposition()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + (wa.Width - ActualWidth) / 2;
        Top = wa.Top + 42;
    }

    private void Fade(double to)
        => BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(160)));
}
