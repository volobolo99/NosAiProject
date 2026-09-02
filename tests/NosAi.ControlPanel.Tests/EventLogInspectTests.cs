using NosAi.ControlPanel;
using NosAi.Runtime.Gate2;
using Xunit;

namespace NosAi.ControlPanel.Tests;

/// <summary>
/// A log with a recorded gap is shown as incomplete, not as a quiet list of rows.
/// </summary>
public sealed class EventLogInspectTests
{
    [Fact]
    public void ALogWithAGapIsShownAsIncomplete()
    {
        var health = new EventLogHealth(
            "data/nosai_telemetry.db",
            true,
            3,
            1,
            9,
            1,
            3,
            DateTime.UtcNow,
            DateTime.UtcNow,
            [new EventLogGapReport(1, 9, "event_bus_full", DateTime.UtcNow)],
            Array.Empty<EventLogTailEntry>(),
            null);

        IReadOnlyList<DisplayField> fields = EventLogInspect.Inspect(health);

        DisplayField salute = Assert.Single(fields, f => f.Label == "Salute registro");
        Assert.Contains(EventLogInspect.IncompleteLabel, salute.Value);
        Assert.Contains("buchi", salute.Value);

        DisplayField complete = Assert.Single(fields, f => f.Label == "Completo");
        Assert.Contains(EventLogInspect.IncompleteLabel, complete.Value);
        Assert.Contains("9 eventi persi", complete.Value);

        Assert.Contains(fields, f => f.Label.Contains("Gap dopo seq 1") && f.Value.Contains("event_bus_full"));
        Assert.DoesNotContain(fields, f => f.Label == "Completo" && f.Value.StartsWith("sì", StringComparison.Ordinal));
    }

    [Fact]
    public void ACompleteLogIsNotLabelledIncomplete()
    {
        var health = new EventLogHealth(
            "data/nosai_telemetry.db",
            true,
            4,
            0,
            0,
            1,
            4,
            DateTime.UtcNow,
            DateTime.UtcNow,
            Array.Empty<EventLogGapReport>(),
            Array.Empty<EventLogTailEntry>(),
            null);

        IReadOnlyList<DisplayField> fields = EventLogInspect.Inspect(health);

        DisplayField complete = Assert.Single(fields, f => f.Label == "Completo");
        Assert.StartsWith("sì", complete.Value);
        Assert.DoesNotContain(EventLogInspect.IncompleteLabel, complete.Value);
    }
}
