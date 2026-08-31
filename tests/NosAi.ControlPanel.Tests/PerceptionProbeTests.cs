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
}
