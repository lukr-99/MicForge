namespace MicForge.Tests;

public sealed class TrayStatusTests
{
    [Theory]
    [InlineData(false, false, false, "MicForge: stopped")]
    [InlineData(false, true, false, "MicForge: live")]
    [InlineData(false, true, true, "MicForge: reconnecting")]
    [InlineData(true, true, true, "MicForge: muted")]
    public void ToolTip_ProcessingState_ReportsMostRelevantStatus(bool muted, bool processing, bool reconnecting, string expected)
        => Assert.Equal(expected, new TrayStatus(muted, processing, reconnecting).ToolTip);
}
