using System;
using System.Windows;
using System.Windows.Media;
using DotNetLib.Tray;

namespace MicForge;

/// <summary>Applies the shared theme and maps MicForge's existing semantic brushes to it.</summary>
public sealed class AppTheme : IDisposable
{
    private readonly ResourceDictionary _resources;
    private readonly TrayThemeApplier _applier;

    public AppTheme(ResourceDictionary resources)
    {
        _resources = resources;
        var light = TrayPalette.NeutralLight with
        {
            Primary = Color.FromRgb(0x08, 0x75, 0x6D),
            Focus = Color.FromRgb(0x08, 0x75, 0x6D)
        };
        var dark = TrayPalette.NeutralDark with
        {
            Primary = Color.FromRgb(0x2E, 0xC4, 0xB6),
            Focus = Color.FromRgb(0x2E, 0xC4, 0xB6)
        };
        _applier = new TrayThemeApplier(resources, TrayThemeApplier.WindowsAppsUseDark, light, dark);
        _applier.Applied += OnApplied;
    }

    public bool IsDark => _applier.IsDark;
    public event EventHandler Applied;

    public void Apply(string mode) => _applier.Apply(
        Enum.TryParse<TrayThemeMode>(mode, out var selected) ? selected : TrayThemeMode.Dark);

    public void Attach(Window window) => _applier.Attach(window);

    private void OnApplied(object sender, EventArgs e)
    {
        if (SystemParameters.HighContrast)
        {
            _resources[TrayThemeTokens.Background] = SystemColors.WindowBrush;
            _resources[TrayThemeTokens.Surface] = SystemColors.WindowBrush;
            _resources[TrayThemeTokens.SurfaceRaised] = SystemColors.ControlBrush;
            _resources[TrayThemeTokens.TextPrimary] = SystemColors.WindowTextBrush;
            _resources[TrayThemeTokens.TextSecondary] = SystemColors.WindowTextBrush;
            _resources[TrayThemeTokens.Border] = SystemColors.WindowTextBrush;
            _resources[TrayThemeTokens.Primary] = SystemColors.HighlightBrush;
            _resources[TrayThemeTokens.OnPrimary] = SystemColors.HighlightTextBrush;
        }
        Map("WindowBrush", TrayThemeTokens.Background);
        Map("SurfaceBrush", TrayThemeTokens.Surface);
        Map("SurfaceAltBrush", TrayThemeTokens.SurfaceRaised);
        Map("BorderBrush", TrayThemeTokens.Border);
        Map("TextBrush", TrayThemeTokens.TextPrimary);
        Map("TextMutedBrush", TrayThemeTokens.TextSecondary);
        Map("AccentBrush", TrayThemeTokens.Primary);
        Map("TrackBrush", TrayThemeTokens.Border);
        Map("StartBrush", TrayThemeTokens.Success);
        Map("StopBrush", TrayThemeTokens.Danger);
        Map("OnStatusBrush", TrayThemeTokens.OnPrimary);
        Applied?.Invoke(this, EventArgs.Empty);
    }

    private void Map(string appKey, string kitKey) => _resources[appKey] = _resources[kitKey];

    public void Dispose()
    {
        _applier.Applied -= OnApplied;
        _applier.Dispose();
    }
}
