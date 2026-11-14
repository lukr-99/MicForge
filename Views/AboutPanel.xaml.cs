using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MicForge;

/// <summary>The About page body: hero, author card, app facts and credits. Text lives in <see cref="AboutInfo"/>.</summary>
public partial class AboutPanel : UserControl
{
    // Heights (0 to 1) of the hero waveform bars.
    private static readonly double[] Heights = { .25, .45, .7, .4, .85, 1, .6, .9, .5, .75, .35, .6, .3, .5, .2 };

    public AboutPanel()
    {
        InitializeComponent();
        AppNameText.Text = AboutInfo.AppName;
        TaglineText.Text = AboutInfo.Tagline;
        DescriptionText.Text = AboutInfo.Description;
        AuthorText.Text = AboutInfo.AuthorLine;
        FeedbackText.Text = AboutInfo.FeedbackLine;
        LinkButtons.ItemsSource = AboutInfo.Links;
        FactList.ItemsSource = AboutInfo.Facts(Composition.CurrentVersion());
        CreditList.ItemsSource = AboutInfo.Credits;
        BuildWave();
        Loaded += (_, _) => Reveal();
    }

    private void BuildWave()
    {
        foreach (var h in Heights)
        {
            Wave.Children.Add(new Border
            {
                Width = 5,
                Height = Math.Max(4, 40 * h),
                Margin = new Thickness(0, 0, 4, 0),
                CornerRadius = new CornerRadius(2.5),
                VerticalAlignment = VerticalAlignment.Center,
                Background = (Brush)FindResource("AccentBrush"),
                Opacity = .45 + .55 * h,
            });
        }
    }

    // A short fade-in of the author card. Skipped when Windows asks for reduced animation.
    private void Reveal()
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        AuthorCard.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450)) { EasingFunction = new CubicEase() });
    }

    private void Link_Click(object sender, RoutedEventArgs e)
    {
        var url = (sender as FrameworkElement)?.Tag as string;
        if (!AboutInfo.IsKnownUrl(url)) return;
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            LinkError.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Log.Error("Could not open an About link", ex);
            LinkError.Text = "Couldn't open the browser. The link is: " + url;
            LinkError.Visibility = Visibility.Visible;
        }
    }
}
