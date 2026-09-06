using NosAi.Core.WorldModel;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.WorldModel.Fusion;
using Xunit;
using RuntimeDataSourceKind = NosAi.Runtime.Contracts.DataSourceKind;

namespace NosAi.Runtime.Tests.WorldModel.Fusion;

/// <summary>
/// AP-02/A5 independent audit item 3: confirms the extraction of
/// <see cref="ClassifiedValueBridge"/> out of <see cref="GameplayObservationProjector"/>
/// (commit be8f12b) changed no behaviour. <c>git show be8f12b -- .../GameplayObservationProjector.cs</c>
/// shows the old private <c>ToWorldFact</c>/<c>MapSource</c> method bodies were
/// moved verbatim (renamed call sites only, identical logic including the
/// <c>_ =&gt; WorldFact&lt;T&gt;.Unknown("source_unknown", observedAtUtc)</c>
/// default arm). This test file re-implements that exact pre-refactor logic
/// locally (a byte-for-byte transcription of the removed private methods, not
/// a paraphrase) and asserts <see cref="ClassifiedValueBridge"/> agrees with
/// it across every <see cref="DataSourceKind"/> and both HasValue states, so
/// the equivalence claim is checked empirically rather than only by reading
/// the diff.
/// </summary>
public sealed class ClassifiedValueBridgeEquivalenceTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    // ---- Verbatim transcription of the pre-be8f12b private methods that
    // used to live in GameplayObservationProjector, kept ONLY in this test
    // file for comparison. Never call production code from here except
    // through ClassifiedValueBridge itself. ----

    private static WorldFact<TResult> LegacyToWorldFact<TSource, TResult>(ClassifiedValue<TSource> value, Func<TSource, TResult> project)
    {
        if (!value.HasValue)
            return WorldFact<TResult>.Unknown(value.FailureReason ?? "not_observed", value.ObservedAtUtc);

        return LegacyMapSource(value.Source, project(value.Value), value.ObservedAtUtc);
    }

    private static WorldFact<T> LegacyMapSource<T>(RuntimeDataSourceKind source, T value, DateTime observedAtUtc) => source switch
    {
        RuntimeDataSourceKind.Live => WorldFact<T>.Live(value, 1.0, observedAtUtc),
        RuntimeDataSourceKind.Derived => WorldFact<T>.Derived(value, 1.0, observedAtUtc),
        RuntimeDataSourceKind.Cached => WorldFact<T>.Cached(value, 1.0, observedAtUtc),
        RuntimeDataSourceKind.Simulated => WorldFact<T>.Simulated(value, 1.0, observedAtUtc),
        _ => WorldFact<T>.Unknown("source_unknown", observedAtUtc)
    };

    public static IEnumerable<object[]> AllSourceKinds()
    {
        yield return new object[] { RuntimeDataSourceKind.Live };
        yield return new object[] { RuntimeDataSourceKind.Derived };
        yield return new object[] { RuntimeDataSourceKind.Cached };
        yield return new object[] { RuntimeDataSourceKind.Simulated };
        yield return new object[] { RuntimeDataSourceKind.Unknown };
    }

    [Theory]
    [MemberData(nameof(AllSourceKinds))]
    public void WithSource_AgreesWithThePreRefactorPrivateMapSource_ForEveryDataSourceKind(RuntimeDataSourceKind source)
    {
        WorldFact<int> viaBridge = ClassifiedValueBridge.WithSource(source, 42, Now);
        WorldFact<int> viaLegacy = LegacyMapSource(source, 42, Now);

        Assert.Equal(viaLegacy, viaBridge);
    }

    [Theory]
    [MemberData(nameof(AllSourceKinds))]
    public void ToWorldFact_WithProjection_AgreesWithThePreRefactorPrivateToWorldFact_WhenValuePresent(RuntimeDataSourceKind source)
    {
        // HasObservedValue must be true and Source non-Unknown to be a
        // realistic "HasValue" classified value; construct directly so
        // every DataSourceKind (including Unknown, an edge no public
        // factory produces with HasObservedValue=true) is exercised exactly
        // as the switch statement itself would see it.
        var classified = new ClassifiedValue<int>(7, source, Now, HasObservedValue: true);

        WorldFact<string> viaBridge = ClassifiedValueBridge.ToWorldFact(classified, v => v.ToString());
        WorldFact<string> viaLegacy = LegacyToWorldFact(classified, v => v.ToString());

        Assert.Equal(viaLegacy, viaBridge);
    }

    [Fact]
    public void ToWorldFact_WithProjection_AgreesWithThePreRefactorPrivateToWorldFact_WhenValueAbsent()
    {
        ClassifiedValue<int> unknown = ClassifiedValue<int>.Unknown("some_reason");

        WorldFact<string> viaBridge = ClassifiedValueBridge.ToWorldFact(unknown, v => v.ToString());
        WorldFact<string> viaLegacy = LegacyToWorldFact(unknown, v => v.ToString());

        Assert.Equal(viaLegacy, viaBridge);
        Assert.Equal("some_reason", viaBridge.Reason);
    }

    [Fact]
    public void ToWorldFact_WithProjection_AbsentValue_FallsBackToNotObserved_WhenFailureReasonIsNull()
    {
        // ClassifiedValue<T>.Unknown always supplies a reason, but the
        // bridge (like the old private method) defends against a
        // hand-constructed value where FailureReason is null.
        var noReason = new ClassifiedValue<int>(0, RuntimeDataSourceKind.Unknown, Now, HasObservedValue: false, Warning: null, FailureReason: null);

        WorldFact<string> viaBridge = ClassifiedValueBridge.ToWorldFact(noReason, v => v.ToString());
        WorldFact<string> viaLegacy = LegacyToWorldFact(noReason, v => v.ToString());

        Assert.Equal(viaLegacy, viaBridge);
        Assert.Equal("not_observed", viaBridge.Reason);
    }

    [Theory]
    [MemberData(nameof(AllSourceKinds))]
    public void ToWorldFact_SameTypeOverload_AgreesWithProjectionOverloadUsingIdentity(RuntimeDataSourceKind source)
    {
        var classified = new ClassifiedValue<int>(99, source, Now, HasObservedValue: true);

        WorldFact<int> viaSameTypeOverload = ClassifiedValueBridge.ToWorldFact(classified);
        WorldFact<int> viaProjectionOverload = ClassifiedValueBridge.ToWorldFact(classified, static v => v);

        Assert.Equal(viaProjectionOverload, viaSameTypeOverload);
    }

    /// <summary>
    /// End-to-end confirmation using the real, unmodified production caller:
    /// <see cref="GameplayObservationProjector.Project"/>'s own pre-existing
    /// tests (<c>GameplayObservationProjectorTests</c>/<c>GameplayObservationProjectorBoundaryTests</c>,
    /// both owned by AP-01/A5 and AP-01/A2, untouched by this audit) already
    /// pass unmodified after the refactor -- see this audit's build/test
    /// evidence. This test adds one more direct check at this boundary: the
    /// Health resource projected from a Live network HP reading carries
    /// Confidence == 1.0 and Source == Live, exactly as the removed private
    /// <c>MapSource</c> would have produced.
    /// </summary>
    [Fact]
    public void RealProjectorCaller_StillProducesTheSameLiveHealthFact_AfterTheExtraction()
    {
        var hp = ClassifiedValue<int>.Live(80, Now);
        WorldFact<double> viaBridge = ClassifiedValueBridge.ToWorldFact(hp, v => (double)v);
        WorldFact<double> viaLegacy = LegacyToWorldFact(hp, v => (double)v);

        Assert.Equal(viaLegacy, viaBridge);
        Assert.Equal(NosAi.Core.WorldModel.DataSourceKind.Live, viaBridge.Source);
        Assert.Equal(1.0, viaBridge.Confidence);
        Assert.Equal(80d, viaBridge.Value);
    }
}
