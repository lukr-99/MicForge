using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DotNetLib.Tray;

namespace MicForge.Tests;

public sealed class SettingsPageTests
{
    [Fact]
    public void SettingsView_RealWindow_UsesKitNavigationAndThemeResources()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                TrayResources.Merge(app.Resources);
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/MicForge;component/Themes/Dark.xaml")
                });
                using var theme = new AppTheme(app.Resources);
                theme.Apply("Dark");
                var stoppedIcon = IconFactory.CreateTrayIcon(IconFactory.TrayState.Stopped);
                var liveIcon = IconFactory.CreateTrayIcon(IconFactory.TrayState.Live);
                var mutedIcon = IconFactory.CreateTrayIcon(IconFactory.TrayState.Muted);
                Assert.Equal(IconFile.TraySizes.Count, BitConverter.ToInt16(stoppedIcon, 4));
                Assert.False(stoppedIcon.SequenceEqual(liveIcon));
                Assert.False(liveIcon.SequenceEqual(mutedIcon));
                using (var stream = new MemoryStream(liveIcon))
                using (var icon = new System.Drawing.Icon(stream, 32, 32))
                    Assert.Equal(32, icon.Width);
                for (var i = 0; i < IconFile.TraySizes.Count; i++)
                    Assert.Equal(IconFile.TraySizes[i], stoppedIcon[6 + i * 16]);
                var view = new SettingsView { DataContext = new SettingsPreview() };
                window = new Window { Content = view, Width = 1100, Height = 800, ShowInTaskbar = false };
                window.Show();
                window.UpdateLayout();
                var page = Assert.IsType<SettingsPage>(view.Content);
                Assert.Equal(new[] { "problems", "general", "monitoring", "reliability", "startup", "data", "updates", "about" },
                    page.Items.Cast<SettingsSection>().Select(section => section.SectionId));
                Assert.Equal(Visibility.Collapsed, ((SettingsSection)page.Items[0]).Visibility);
                Assert.True(page.IsNavigationVisible);
                Assert.False(page.IsSearchVisible);
                Assert.NotNull(page.Template);
                Assert.All(page.Items.Cast<SettingsSection>(), section => Assert.NotNull(section.Template));
                var monitoring = (SettingsSection)page.Items[2];
                Assert.False(((DropdownRow)monitoring.Items[1]).IsEnabled);
                var updates = (SettingsSection)page.Items[6];
                Assert.True(((ButtonRow)updates.Items[1]).IsBusy);

                var startup = (SettingsSection)page.Items[4];
                var toggle = Assert.IsType<ToggleRow>(startup.Items[1]);
                var preview = (SettingsPreview)view.DataContext;
                toggle.Switch.IsChecked = true;
                Assert.True(preview.AutoStartProcessing);

                var outputDirectory = Environment.GetEnvironmentVariable("MICFORGE_TEST_SNAPSHOTS");
                foreach (var mode in new[] { "Dark", "Light" })
                {
                    theme.Apply(mode);
                    window.UpdateLayout();
                    app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    Assert.Equal(app.Resources[TrayThemeTokens.TextPrimary], app.Resources["TextBrush"]);
                    Assert.Equal(mode == "Dark", theme.IsDark);
                    if (outputDirectory != null)
                    {
                        Directory.CreateDirectory(outputDirectory);
                        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(view);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var file = File.Create(Path.Combine(outputDirectory, $"settings-{mode.ToLowerInvariant()}.png"));
                        encoder.Save(file);
                    }
                }
                window.Width = 680;
                window.UpdateLayout();
                Assert.True(page.ShowsNavChips);
            }
            catch (Exception ex) { failure = ex; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF settings verification timed out.");
        if (failure != null) throw new Exception("WPF settings verification failed.", failure);
    }
}
