using NosAi.Core.WorldModel;
using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;
using NosAi.Runtime.WorldModel.Fusion;
using Xunit;

namespace NosAi.Runtime.Tests.WorldModel.Fusion;

/// <summary>
/// AP-02/A5 independent audit of <see cref="VisualObservationFusion.FuseVitals"/>
/// (docs/agents/phases/AP-02/AP-02_A5_AUDIT.md item 1). Ownership: tests only,
/// no production file touched. Every hypothesis below was checked against the
/// real fusion code, not assumed from reading it.
/// </summary>
public sealed class VisualObservationFusionBoundaryTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly EntityId PlayerId = new("player-1");

    private static WorldModelSnapshot NetworkOnlySnapshot(ClassifiedValue<int> hp, ClassifiedValue<int> maxHp)
    {
        GameplayObservation observation = GameplayObservation.Unobserved("reason", Now) with { Hp = hp, MaxHp = maxHp };
        return GameplayObservationProjector.Project(observation, PlayerId, version: 1, Now);
    }

    private static VisualObservation VisualWithHp(ClassifiedValue<int> current, ClassifiedValue<int> maximum, double confidence = 0.9)
    {
        VisualObservation baseline = VisualObservation.Unobserved("no_reading", Now);
        var hp = new ScreenVitalPair(current, maximum, Confidence: confidence, FailureReason: null);
        return baseline with { Vitals = baseline.Vitals with { Hp = hp } };
    }

    // ------------------------------------------------------------------
    // 1. Duplicate ResourceKind.Health entries in the input snapshot.
    // ------------------------------------------------------------------

    /// <summary>
    /// <see cref="VisualObservationFusion.FuseVitals"/> reconstructs
    /// <c>Player.Status.Resources</c> by iterating the ORIGINAL list and
    /// replacing every entry whose <c>Kind</c> matches, while the value it
    /// substitutes is computed once, from <c>FindResource</c>'s "first
    /// match wins" lookup. Documented gap (not a blocking defect, per the
    /// same reasoning AP-01/A5 applied to duplicate <c>EntityId</c>
    /// entries): nothing in this repository's only real snapshot producer
    /// (<see cref="GameplayObservationProjector"/>) can create two
    /// <see cref="ResourceKind.Health"/> resources on the same player, so
    /// this input shape is not reachable today -- but nothing rejects it
    /// either. Pinned down here so the behaviour cannot silently change:
    /// BOTH duplicate slots survive in the output (the list is not
    /// deduplicated), and BOTH are silently overwritten with the SAME
    /// fused value, computed only from the first-listed duplicate. The
    /// second duplicate's own Current/Maximum readings are discarded
    /// without any warning, disagreement flag, or trace.
    /// </summary>
    [Fact]
    public void DuplicateHealthResourceInSnapshot_BothSlotsSurvive_ButBothAreOverwrittenFromOnlyTheFirstDuplicate()
    {
        WorldModelSnapshot snapshot = NetworkOnlySnapshot(ClassifiedValue<int>.Live(80, Now), ClassifiedValue<int>.Live(100, Now));

        // Inject a second, deliberately DIFFERENT Health resource that the
        // real projector would never produce, to see how fusion copes with
        // an already-inconsistent input list.
        var secondHealth = new Resource(ResourceKind.Health, WorldFact<double>.Live(30, 1.0, Now), WorldFact<double>.Live(50, 1.0, Now));
        List<Resource> withDuplicate = new(snapshot.Player.Status.Resources) { secondHealth };
        WorldModelSnapshot snapshotWithDuplicate = snapshot with
        {
            Player = snapshot.Player with { Status = snapshot.Player.Status with { Resources = EquatableArray<Resource>.From(withDuplicate) } }
        };
        Assert.Equal(2, snapshotWithDuplicate.Player.Status.Resources.Count(r => r.Kind == ResourceKind.Health));

        VisualObservation visual = VisualObservation.Unobserved("no_reading", Now);
        WorldModelSnapshot fused = VisualObservationFusion.FuseVitals(snapshotWithDuplicate, visual, Now);

        List<Resource> healthEntries = fused.Player.Status.Resources.Where(r => r.Kind == ResourceKind.Health).ToList();

        // Neither merged into one entry nor rejected: both positions survive.
        Assert.Equal(2, healthEntries.Count);
        // Both were overwritten with the identical fused value...
        Assert.Equal(healthEntries[0], healthEntries[1]);
        // ...and that value came from the FIRST duplicate (80), never the second (30):
        // proof the second duplicate's data was silently discarded rather than
        // merged, disagreement-flagged, or used at all.
        Assert.Equal(80d, healthEntries[0].Current.Value);
        Assert.NotEqual(30d, healthEntries[0].Current.Value);
    }

    // ------------------------------------------------------------------
    // 2. ResourceKind.Custom resources (e.g. a "Combo Gauge") must pass
    //    through fusion completely untouched.
    // ------------------------------------------------------------------

    [Fact]
    public void CustomResourceKind_IsPreservedUnchanged_ByPassingThroughTheElseBranch()
    {
        WorldModelSnapshot snapshot = NetworkOnlySnapshot(ClassifiedValue<int>.Live(80, Now), ClassifiedValue<int>.Live(100, Now));
        var comboGauge = new Resource(
            ResourceKind.Custom,
            WorldFact<double>.Live(3, 1.0, Now),
            WorldFact<double>.Live(5, 1.0, Now),
            customName: "Combo Gauge");
        List<Resource> withCustom = new(snapshot.Player.Status.Resources) { comboGauge };
        WorldModelSnapshot snapshotWithCustom = snapshot with
        {
            Player = snapshot.Player with { Status = snapshot.Player.Status with { Resources = EquatableArray<Resource>.From(withCustom) } }
        };

        VisualObservation visual = VisualWithHp(ClassifiedValue<int>.Derived(50, Now), ClassifiedValue<int>.Derived(100, Now));
        WorldModelSnapshot fused = VisualObservationFusion.FuseVitals(snapshotWithCustom, visual, Now);

        Resource preserved = Assert.Single(fused.Player.Status.Resources, r => r.Kind == ResourceKind.Custom);
        Assert.Equal(comboGauge, preserved);
        Assert.Equal("Combo Gauge", preserved.CustomName);
        Assert.Equal(3d, preserved.Current.Value);
        Assert.Equal(5d, preserved.Maximum.Value);
        // And Health/Mana were still fused correctly alongside it (3 total).
        Assert.Equal(3, fused.Player.Status.Resources.Count);
    }

    // ------------------------------------------------------------------
    // 3. Both channels legitimately observed but older than maxAge.
    // ------------------------------------------------------------------

    /// <summary>
    /// Unlike the existing <c>BothChannelsUnknown_ResultIsUnknown_NeverAFabricatedZero</c>
    /// test (which starts from <c>ClassifiedValue&lt;int&gt;.Unknown</c> on
    /// both sides), this drives the ACTUAL staleness path: both channels
    /// have a real, previously-observed value, but every candidate is older
    /// than <paramref name="maxAge"/> (5s default) as of <c>nowUtc</c>.
    /// Verified honestly Unknown -- no hole found: <see cref="FactFusion.Resolve"/>
    /// discards every candidate whose <see cref="WorldFact{T}.IsFresh"/> is
    /// false before ever comparing values, so a stale Live/Derived reading
    /// never wins by default.
    /// </summary>
    [Fact]
    public void BothChannelsOlderThanMaxAge_ResultIsHonestlyUnknown_NotTheStaleValue()
    {
        DateTime tenSecondsAgo = Now - TimeSpan.FromSeconds(10);
        WorldModelSnapshot snapshot = NetworkOnlySnapshot(
            ClassifiedValue<int>.Live(80, tenSecondsAgo),
            ClassifiedValue<int>.Live(100, tenSecondsAgo));
        VisualObservation visual = VisualWithHp(
            ClassifiedValue<int>.Derived(50, tenSecondsAgo),
            ClassifiedValue<int>.Derived(100, tenSecondsAgo));

        WorldModelSnapshot fused = VisualObservationFusion.FuseVitals(snapshot, visual, Now, maxAge: VisualObservationFusion.DefaultMaxAge);

        Resource health = Assert.Single(fused.Player.Status.Resources, r => r.Kind == ResourceKind.Health);
        Assert.False(health.Current.HasValue);
        Assert.False(health.Maximum.HasValue);
        Assert.Equal("no_fresh_channel_reported_this_fact", health.Current.Reason);
        // The Unknown result is stamped with the caller's own nowUtc, not a
        // leaked real wall-clock instant -- FactFusion's own fallback path
        // passes nowUtc explicitly (see FactFusion.cs:84).
        Assert.Equal(Now, health.Current.ObservedAtUtc);
    }

    /// <summary>Sanity companion: a candidate exactly at the maxAge boundary still competes (per <see cref="WorldFact{T}.IsFresh"/>'s own documented "&lt;=" contract), so this is a real edge, not an off-by-one accident.</summary>
    [Fact]
    public void ExactlyAtMaxAgeBoundary_StillCompetes_ConsistentWithIsFreshsOwnLessThanOrEqualContract()
    {
        DateTime exactlyAtBoundary = Now - VisualObservationFusion.DefaultMaxAge;
        WorldModelSnapshot snapshot = NetworkOnlySnapshot(
            ClassifiedValue<int>.Live(80, exactlyAtBoundary),
            ClassifiedValue<int>.Live(100, exactlyAtBoundary));
        VisualObservation visual = VisualObservation.Unobserved("no_reading", Now);

        WorldModelSnapshot fused = VisualObservationFusion.FuseVitals(snapshot, visual, Now);

        Resource health = Assert.Single(fused.Player.Status.Resources, r => r.Kind == ResourceKind.Health);
        Assert.True(health.Current.HasValue);
        Assert.Equal(80d, health.Current.Value);
    }

    // ------------------------------------------------------------------
    // 4. Confidence flattening: ScreenVitalPair.Confidence (>= 0.85,
    //    e.g. 0.9) never survives into the fused WorldFact.Confidence.
    // ------------------------------------------------------------------

    /// <summary>
    /// <see cref="ClassifiedValueBridge.WithSource{T}"/> always stamps
    /// <c>1.0</c> regardless of the source classified value, and network-side
    /// <see cref="ClassifiedValue{T}"/> has no confidence field to begin
    /// with (only <see cref="ScreenVitalPair"/>/<see cref="ScreenBarFill"/>
    /// carry a real one, gated at >= 0.85). Verified: whatever
    /// <see cref="ScreenVitalPair.Confidence"/> actually is (0.85 through
    /// 1.0), the resulting <see cref="WorldFact{T}.Confidence"/> is always
    /// exactly 1.0. Decision recorded, as the audit command asked for
    /// explicitly: this is an ACCEPTED LIMIT, not a defect, because (a) the
    /// network side never had a distinct confidence to compare against in
    /// the first place (<see cref="ClassifiedValue{T}"/> simply has no such
    /// field), so the flattening is symmetric across both channels rather
    /// than favouring one; and (b) the only consumer of
    /// <see cref="WorldFact{T}.Confidence"/> in this fusion path is
    /// <see cref="FactFusion"/>'s own same-rank tie-break, which cannot ever
    /// discriminate here since both sides are always 1.0 -- confirmed by
    /// the companion test below. The moment a future phase (e.g. AP-05
    /// combat risk scoring) starts reading <c>Resource.Current.Confidence</c>
    /// to weigh trust in the value itself, this flattening would start
    /// silently discarding real information (a barely-passing 0.85 OCR read
    /// vs. a clean 0.99 one, currently indistinguishable) -- flagged here so
    /// that future consumer knows not to rely on it without revisiting this
    /// bridge.
    /// </summary>
    [Theory]
    [InlineData(0.85)]
    [InlineData(0.9)]
    [InlineData(1.0)]
    public void ScreenVitalPairConfidence_NeverSurvivesIntoTheFusedWorldFactConfidence_AcceptedLimit(double screenConfidence)
    {
        WorldModelSnapshot snapshot = NetworkOnlySnapshot(ClassifiedValue<int>.Unknown("not_seen"), ClassifiedValue<int>.Unknown("not_seen"));
        VisualObservation visual = VisualWithHp(ClassifiedValue<int>.Derived(50, Now), ClassifiedValue<int>.Derived(100, Now), confidence: screenConfidence);

        WorldModelSnapshot fused = VisualObservationFusion.FuseVitals(snapshot, visual, Now);

        Resource health = Assert.Single(fused.Player.Status.Resources, r => r.Kind == ResourceKind.Health);
        Assert.Equal(1.0, health.Current.Confidence);
    }

    /// <summary>
    /// Companion proving the flattening is harmless TODAY: two same-rank
    /// (both Derived) candidates with genuinely different screen confidence
    /// values still resolve purely by timestamp/channel-name tie-break, not
    /// by which one had the higher real screen confidence -- because both
    /// arrive at <see cref="FactFusion"/> already flattened to 1.0.
    /// </summary>
    [Fact]
    public void WhenBothChannelsShareTheSameRank_TieBreakIgnoresTheOriginalScreenConfidence_BecauseBothAreFlattenedToOne()
    {
        // Network side reported as Derived (not Live) so both candidates
        // share the same DataSourceKind rank and the tie-break chain is
        // actually exercised, per FactFusion.IsBetter's documented order.
        GameplayObservation observation = GameplayObservation.Unobserved("reason", Now) with
        {
            Hp = ClassifiedValue<int>.Derived(80, Now - TimeSpan.FromMilliseconds(1)),
            MaxHp = ClassifiedValue<int>.Derived(100, Now - TimeSpan.FromMilliseconds(1))
        };
        WorldModelSnapshot snapshot = GameplayObservationProjector.Project(observation, PlayerId, version: 1, Now);

        // Screen is timestamped LATER than network and has a much lower
        // (but still publishable) confidence (0.85 vs. an implied "network
        // confidence" that does not exist). If confidence still mattered,
        // one might expect the higher-confidence network reading to win;
        // instead the fresher timestamp wins, exactly as FactFusion's
        // documented tie-break order (confidence -> timestamp -> channel)
        // predicts once confidence is tied at 1.0 on both sides.
        VisualObservation visual = VisualWithHp(ClassifiedValue<int>.Derived(50, Now), ClassifiedValue<int>.Derived(100, Now), confidence: 0.85);

        WorldModelSnapshot fused = VisualObservationFusion.FuseVitals(snapshot, visual, Now);

        Resource health = Assert.Single(fused.Player.Status.Resources, r => r.Kind == ResourceKind.Health);
        Assert.Equal(50d, health.Current.Value); // screen (fresher timestamp) won, not network (higher "real" confidence)
    }

    // ------------------------------------------------------------------
    // 5. int -> double conversion cannot introduce NaN/overflow here.
    // ------------------------------------------------------------------

    /// <summary>
    /// AP-01/A5 found that <c>TemporalBelief</c>'s velocity arithmetic
    /// (subtraction/division on <see cref="float"/> positions) can overflow
    /// to +/-Infinity or NaN. Checked whether the same class of problem
    /// reaches here: it does not, because <see cref="ScreenVitalPair.Current"/>/
    /// <c>Maximum</c> are <see cref="int"/>, and <c>(double)int</c> is a
    /// widening conversion that cannot produce NaN or overflow for ANY
    /// 32-bit integer value (int.MinValue/int.MaxValue both convert to
    /// finite doubles). Verified against the real runtime, not assumed.
    /// </summary>
    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    [InlineData(0)]
    [InlineData(-1)]
    public void IntToDoubleWideningConversion_NeverProducesNaNOrInfinity_ForAny32BitInt(int value)
    {
        double converted = value;
        Assert.False(double.IsNaN(converted));
        Assert.False(double.IsInfinity(converted));
    }

    [Fact]
    public void ExtremeScreenVitalIntValues_FuseWithoutProducingNaNOrInfinity()
    {
        WorldModelSnapshot snapshot = NetworkOnlySnapshot(ClassifiedValue<int>.Unknown("not_seen"), ClassifiedValue<int>.Unknown("not_seen"));
        VisualObservation visual = VisualWithHp(ClassifiedValue<int>.Derived(int.MaxValue, Now), ClassifiedValue<int>.Derived(int.MaxValue, Now));

        WorldModelSnapshot fused = VisualObservationFusion.FuseVitals(snapshot, visual, Now);

        Resource health = Assert.Single(fused.Player.Status.Resources, r => r.Kind == ResourceKind.Health);
        Assert.Equal((double)int.MaxValue, health.Current.Value);
        Assert.False(double.IsNaN(health.Current.Value));
    }

    // ------------------------------------------------------------------
    // 6. The "no_network_reading" Unknown placeholder omits nowUtc (an
    //    AP-01/A5-style wall-clock leak into a WorldFact.Unknown call) --
    //    verified empirically whether it actually breaks the documented
    //    "same inputs -> same output" purity guarantee.
    // ------------------------------------------------------------------

    /// <summary>
    /// <c>VisualObservationFusion.FuseResource</c> builds its "network side
    /// absent" placeholder via <c>WorldFact&lt;double&gt;.Unknown("no_network_reading")</c>
    /// with NO <c>observedAtUtc</c> argument, unlike every other <c>Unknown(...)</c>
    /// call in this file and in <see cref="GameplayObservationProjector"/>
    /// (which always thread through an explicit instant). Per
    /// <see cref="WorldFact{T}.Unknown"/>'s own default, this stamps real
    /// <see cref="DateTime.UtcNow"/> rather than the method's own
    /// <c>nowUtc</c> parameter -- the same class of bug AP-01/A5 found (and
    /// saw fixed) in <c>TemporalBelief.EstimateVelocity</c>/<c>PredictPosition</c>.
    /// <para>
    /// Verified here, empirically, across a real wall-clock delay (not
    /// merely assumed from reading the code) whether this leak is
    /// consequential: it is NOT. <see cref="WorldFact{T}.IsFresh"/> is
    /// defined as <c>HasValue &amp;&amp; ...</c>, and this placeholder
    /// always has <c>HasValue == false</c> (it is a <c>DataSourceKind.Unknown</c>
    /// fact), so <see cref="FactFusion.Resolve{T}"/> discards it via the
    /// short-circuited <c>HasValue</c> check before its <c>ObservedAtUtc</c>
    /// is ever compared against anything -- the leaked timestamp is written
    /// but never read. This test proves it by calling <c>FuseVitals</c>
    /// twice, with an intervening real-time delay, on a snapshot whose
    /// network side has NO Health/Mana resource at all (forcing this exact
    /// code path) and asserting the two results are bit-for-bit equal.
    /// </para>
    /// <para>
    /// Documented as a code-smell / convention deviation worth aligning for
    /// defensive hygiene (the fix is one line: pass <c>nowUtc</c> at both
    /// call sites in <c>FuseResource</c>, mirroring the AP-01/A5 fix
    /// pattern), but NOT filed as a red regression test: no observable
    /// output difference could be produced to fail against.
    /// </para>
    /// </summary>
    [Fact]
    public async Task NetworkResourceEntirelyAbsent_WallClockLeakInTheUnknownPlaceholder_DoesNotBreakDeterminism_VerifiedAcrossARealDelay()
    {
        WorldModelSnapshot emptyResources = WorldModelSnapshot.Unknown("bootstrap", Now) with
        {
            Player = WorldModelSnapshot.Unknown("bootstrap", Now).Player with
            {
                Status = CombatantStatus.Empty // no Health/Mana Resource entries at all: FindResource returns null.
            }
        };
        VisualObservation visual = VisualWithHp(ClassifiedValue<int>.Derived(10, Now), ClassifiedValue<int>.Derived(20, Now));

        WorldModelSnapshot first = VisualObservationFusion.FuseVitals(emptyResources, visual, Now);
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        WorldModelSnapshot second = VisualObservationFusion.FuseVitals(emptyResources, visual, Now);

        Assert.Equal(first, second);
    }
}
