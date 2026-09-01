using System.Collections.Immutable;
using NosAi.ControlPanel;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate3;
using Xunit;

namespace NosAi.ControlPanel.Tests;

public sealed class DecisionInspectTests
{
    [Fact]
    public void Attached_session_is_unknown_with_hosted_reason_not_zeros()
    {
        var hostedZeros = new Gate3LoopView(
            ClassifiedValue<bool>.Derived(false),
            ClassifiedValue<long>.Derived(0),
            ClassifiedValue<string>.Derived("NoWorldState"),
            ClassifiedValue<string>.Derived("None"),
            ClassifiedValue<string>.Derived("quiet"),
            ClassifiedValue<int>.Derived(0),
            ClassifiedValue<int>.Derived(0),
            ClassifiedValue<double>.Derived(0),
            ClassifiedValue<bool>.Derived(false),
            ImmutableArray.Create(new KeyValuePair<string, long>("NoWorldState", 0)));

        IReadOnlyList<DisplayField> fields = DecisionInspect.Inspect(SessionKind.Attached, hostedZeros);

        Assert.NotEmpty(fields);
        Assert.All(fields, f =>
        {
            Assert.Equal("UNKNOWN", f.Source);
            Assert.Contains(DecisionInspect.AttachedUnavailable, f.Value);
        });
        Assert.DoesNotContain(fields, f => f.Value == "0");
        Assert.DoesNotContain(fields, f => f.Value.Contains("NoWorldState") && !f.Value.Contains(DecisionInspect.AttachedUnavailable));
    }

    [Fact]
    public void Hosted_acting_disabled_is_decide_not_a_fault()
    {
        var view = new Gate3LoopView(
            ClassifiedValue<bool>.Derived(true),
            ClassifiedValue<long>.Derived(4),
            ClassifiedValue<string>.Derived("NoCandidate"),
            ClassifiedValue<string>.Derived("None"),
            ClassifiedValue<string>.Derived("personaggio sano"),
            ClassifiedValue<int>.Derived(7305),
            ClassifiedValue<int>.Derived(7305),
            ClassifiedValue<double>.Derived(0.2),
            ClassifiedValue<bool>.Derived(false),
            ImmutableArray.Create(
                new KeyValuePair<string, long>("NoCandidate", 3),
                new KeyValuePair<string, long>("NoWorldState", 1)));

        IReadOnlyList<DisplayField> fields = DecisionInspect.Inspect(SessionKind.Hosted, view);

        DisplayField acting = Assert.Single(fields, f => f.Label == "Azione");
        Assert.Equal("DERIVED", acting.Source);
        Assert.Contains("decide, non agisce", acting.Value);
        Assert.DoesNotContain("guasto", acting.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(fields, f => f.Label == "Esito NoCandidate" && f.Value.Contains("3"));
        Assert.Contains(fields, f => f.Label == "Esito NoWorldState" && f.Value.Contains("1"));
        Assert.DoesNotContain(fields, f => f.Label.Contains("successo", StringComparison.OrdinalIgnoreCase));
    }
}
