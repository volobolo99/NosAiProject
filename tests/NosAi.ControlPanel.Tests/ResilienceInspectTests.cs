using NosAi.ControlPanel;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate1;
using NosAi.Runtime.Safety;
using Xunit;

namespace NosAi.ControlPanel.Tests;

/// <summary>
/// The panel reads the breaker; unknown is never a quiet zero.
/// </summary>
public sealed class ResilienceInspectTests
{
    [Fact]
    public void AnAbsentControllerIsUnknownNotZero()
    {
        IReadOnlyList<DisplayField> fields = ResilienceInspect.Inspect(null);

        Assert.All(fields, f => Assert.Equal("UNKNOWN", f.Source));
        Assert.All(fields, f => Assert.Contains(Gate1ResilienceView.NotConfiguredReason, f.Value));
        Assert.DoesNotContain(fields, f => f.Value is "0" or "0 [LIVE]" or "0 [DERIVED]");
    }

    [Fact]
    public void ALiveControllerIsShownWithItsBudgets()
    {
        var recovery = new RecoveryController(new TrustBoundary(TrustTier.Tier4_FullAutonomous));
        IReadOnlyList<DisplayField> fields = ResilienceInspect.Inspect(Gate1ResilienceView.From(recovery));

        DisplayField state = Assert.Single(fields, f => f.Label == "Stato breaker");
        Assert.Contains("Closed", state.Value);
        Assert.Equal("LIVE", state.Source);

        DisplayField failures = Assert.Single(fields, f => f.Label == "Fallimenti in finestra");
        Assert.Contains("0", failures.Value);
        Assert.Equal("LIVE", failures.Source);

        DisplayField window = Assert.Single(fields, f => f.Label == "Budget finestra");
        Assert.Contains(RecoveryController.DefaultWindowSize.ToString(), window.Value);
        Assert.Equal("DERIVED", window.Source);
    }
}
