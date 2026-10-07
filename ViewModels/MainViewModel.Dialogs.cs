using DotNetLib.Tray;

namespace MicForge.ViewModels;

/// <summary>Routes user messages through the shared themed dialog.</summary>
public sealed partial class MainViewModel
{
    private void ShowMessage(string message, string title) =>
        TrayMessageWindow.Inform(title, message, prepare: _theme.Attach);
}
