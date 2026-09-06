using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

public sealed class VisualObservationTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Unobserved_EveryFieldIsExplicitlyUnknown_NeverFabricated()
    {
        VisualObservation observation = VisualObservation.Unobserved("no_capture_backend_attached", Now);

        Assert.False(observation.Frame.FrameAcquired);
        Assert.Empty(observation.Frame.Entities);
        Assert.False(observation.Vitals.Hp.Current.HasValue);
        Assert.False(observation.Vitals.Hp.Maximum.HasValue);
        Assert.False(observation.Vitals.Mp.Current.HasValue);
        Assert.False(observation.Vitals.HpBar.Ratio.HasValue);
        Assert.False(observation.HasTarget.HasValue);
        Assert.False(observation.HasDialogWindow.HasValue);
        Assert.Equal(Now, observation.ObservedAtUtc);
    }

    [Fact]
    public void Unobserved_CarriesTheGivenReason_OnEveryField()
    {
        VisualObservation observation = VisualObservation.Unobserved("capture_unavailable");

        Assert.Equal("capture_unavailable", observation.Frame.UnavailableReason);
        Assert.Equal("capture_unavailable", observation.Vitals.Hp.FailureReason);
        Assert.Equal("capture_unavailable", observation.HasTarget.FailureReason);
        Assert.Equal("capture_unavailable", observation.HasDialogWindow.FailureReason);
    }
}
