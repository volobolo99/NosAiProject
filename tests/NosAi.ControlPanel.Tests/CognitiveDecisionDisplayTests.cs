using System.Collections.Immutable;
using NosAi.ControlPanel;
using NosAi.Core.Cognitive;
using Xunit;

namespace NosAi.ControlPanel.Tests;

public sealed class CognitiveDecisionDisplayTests
{
    [Fact]
    public void Cycle_decision_without_measure_never_prints_a_percentage()
    {
        var decision = new CognitiveDecisionView(
            "d1",
            "c1",
            "Gate3 cycle",
            "None",
            double.NaN,
            double.NaN,
            "Confirmed",
            DateTimeOffset.UtcNow,
            ImmutableArray<DecisionCandidateView>.Empty);

        string meta = CognitiveMemoryWindow.DecisionMetaLine(decision);

        Assert.Contains("Esito: Confirmed", meta, StringComparison.Ordinal);
        Assert.Contains("UNKNOWN", meta, StringComparison.Ordinal);
        Assert.DoesNotContain("%", meta, StringComparison.Ordinal);
    }

    [Fact]
    public void Cycle_trace_without_measure_never_prints_a_percentage()
    {
        var trace = new CognitiveTraceEvent(
            "e1",
            "c1",
            CognitiveNodeKind.Reobserve,
            CognitiveNodeStatus.Unknown,
            "runtime.cycle",
            "cycle summary",
            "Outcome=NoCandidate; observationAge=100ms",
            double.NaN,
            DateTimeOffset.UtcNow,
            1);

        string detail = CognitiveMemoryWindow.FormatTraceDetail(trace);

        Assert.Contains("UNKNOWN", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("%", detail, StringComparison.Ordinal);
    }
}
