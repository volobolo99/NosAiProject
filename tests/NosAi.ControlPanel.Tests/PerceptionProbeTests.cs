using NosAi.ControlPanel;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.ControlPanel.Tests;

public sealed class PerceptionProbeTests
{
    [Fact]
    public void Probe_never_invents_pixels()
    {
        var result = PerceptionProbe.Run();
        Assert.False(string.IsNullOrWhiteSpace(result.Summary));
        Assert.DoesNotContain("inventat", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.Fields, f => f.Label == "Fotogramma");
        Assert.Contains(result.Fields, f => f.Source is "LIVE" or "UNKNOWN");
    }

    [Fact]
    public void ReadFrame_passes_client_area_so_windowed_roi_is_not_the_full_desktop()
    {
        var pixels = new byte[200 * 200 * 4];
        var frame = new CaptureFrame(200, 200, pixels, DataSourceKind.Simulated, DateTime.UtcNow);
        var clientArea = new PixelRect(50, 50, 100, 100);

        ScreenVitalObservation fullscreen = PerceptionProbe.ReadFrame(frame, clientArea: null);
        ScreenVitalObservation windowed = PerceptionProbe.ReadFrame(frame, clientArea);

        Assert.NotEqual(fullscreen.HpRoi, windowed.HpRoi);
        Assert.True(windowed.HpRoi.X >= clientArea.X);
        Assert.True(windowed.HpRoi.Y >= clientArea.Y);
        Assert.True(windowed.HpRoi.X + windowed.HpRoi.Width <= clientArea.X + clientArea.Width);
        Assert.True(windowed.HpRoi.Y + windowed.HpRoi.Height <= clientArea.Y + clientArea.Height);
    }

    [Fact]
    public void Missing_client_window_is_unknown_and_declares_fullscreen_fallback()
    {
        var result = PerceptionProbe.Run(repoRoot: null, clientProcessName: "");
        DisplayField window = Assert.Single(result.Fields, f => f.Label == "Finestra client");
        Assert.Equal("UNKNOWN", window.Source);
        Assert.Contains("client_process_name_empty", window.Value);
        Assert.Contains(PerceptionProbe.FullscreenFallbackNote, result.Summary);
    }
}
