using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Temporal;
using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;
using NosAi.Runtime.WorldModel.Fusion;
using Xunit;

namespace NosAi.Runtime.Tests.WorldModel.Fusion;

/// <summary>
/// AP-02/A5 independent audit item 4: chains
/// <see cref="VisualObservationFusion.FuseVitals"/> with
/// <see cref="GameplayObservationProjector.Project"/> and
/// <see cref="WorldModelTemporalEnricher.Enrich"/> across several cycles --
/// the same style of end-to-end determinism check AP-01/A5 ran for
/// <see cref="WorldModelTemporalEnricher"/> alone -- to verify two identical
/// replays produce bit-for-bit identical final snapshots once the new AP-02
/// vitals fusion stage is inserted into the chain.
/// </summary>
public sealed class MultimodalPipelineDeterminismTests
{
    private static readonly EntityId PlayerId = new("player-1");
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxObservationGap = TimeSpan.FromSeconds(5);

    private static WorldModelSnapshot RunOneCycle(
        WorldModelSnapshot previous,
        int hp, int maxHp, int x, int y,
        int screenHp, int screenMaxHp,
        DateTime nowUtc)
    {
        // Every field GameplayObservation.Unobserved would otherwise leave at
        // its own ClassifiedValue<T>.Unknown(reason) default is overridden
        // here explicitly (Mp/MaxMp/MapId included, not just Hp/MaxHp/Position).
        // This is a deliberate workaround for the real defect documented in
        // this audit's report (GameplayObservation.Unobserved,
        // src/NosAi.Runtime/LiveIntegration/GameplayProvider.cs:196-216):
        // every one of its sixteen per-field Unknown(...) calls ignores the
        // method's own `atUtc` parameter and stamps real wall-clock time
        // instead. Left un-overridden, that leak (not anything in AP-02's own
        // new code) would make this test non-deterministic for reasons
        // unrelated to what this test exists to check -- see the dedicated,
        // deliberately red test below that isolates it precisely.
        GameplayObservation observation = GameplayObservation.Unobserved("reason", nowUtc) with
        {
            Hp = ClassifiedValue<int>.Live(hp, nowUtc),
            MaxHp = ClassifiedValue<int>.Live(maxHp, nowUtc),
            Mp = ClassifiedValue<int>.Live(50, nowUtc),
            MaxMp = ClassifiedValue<int>.Live(100, nowUtc),
            PlayerPosition = ClassifiedValue<MapPoint>.Live(new MapPoint(x, y), nowUtc),
            MapId = ClassifiedValue<int>.Live(1, nowUtc)
        };
        WorldModelSnapshot projected = GameplayObservationProjector.Project(observation, PlayerId, version: previous.Version + 1, nowUtc);

        VisualObservation baseline = VisualObservation.Unobserved("no_reading", nowUtc);
        var screenHpPair = new ScreenVitalPair(ClassifiedValue<int>.Derived(screenHp, nowUtc), ClassifiedValue<int>.Derived(screenMaxHp, nowUtc), Confidence: 0.9, FailureReason: null);
        VisualObservation visual = baseline with { Vitals = baseline.Vitals with { Hp = screenHpPair } };

        WorldModelSnapshot fused = VisualObservationFusion.FuseVitals(projected, visual, nowUtc, MaxAge);

        return WorldModelTemporalEnricher.Enrich(previous, fused, nowUtc, MaxAge, MaxObservationGap);
    }

    /// <summary>
    /// Three cycles, player-only (the only entity kind the current AP-02
    /// scope actually fuses -- see AP-02_STATUS.md S:1, entity fusion is
    /// deliberately out of scope). Replayed twice from the same fixed
    /// instants and inputs; both replays must agree exactly.
    /// </summary>
    [Fact]
    public void ThreeCyclesOfFuseThenProjectThenEnrich_ReplayedTwice_ProduceBitForBitEqualFinalSnapshots()
    {
        WorldModelSnapshot ReplayFromScratch()
        {
            WorldModelSnapshot current = WorldModelSnapshot.Unknown("no_prior_fusion_cycle", new DateTime(2026, 9, 5, 11, 59, 0, DateTimeKind.Utc));
            DateTime t0 = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

            current = RunOneCycle(current, hp: 100, maxHp: 100, x: 0, y: 0, screenHp: 100, screenMaxHp: 100, nowUtc: t0);
            current = RunOneCycle(current, hp: 90, maxHp: 100, x: 1, y: 0, screenHp: 90, screenMaxHp: 100, nowUtc: t0 + TimeSpan.FromSeconds(1));
            current = RunOneCycle(current, hp: 80, maxHp: 100, x: 2, y: 0, screenHp: 80, screenMaxHp: 100, nowUtc: t0 + TimeSpan.FromSeconds(2));
            return current;
        }

        WorldModelSnapshot replayA = ReplayFromScratch();
        WorldModelSnapshot replayB = ReplayFromScratch();

        Assert.Equal(replayA, replayB);
        // Sanity: the chain actually did something observable (velocity
        // derived, HP fused), not just two empty snapshots trivially equal.
        Assert.True(replayA.Player.Velocity.HasValue);
        Assert.Equal(80d, replayA.Player.Status.Resources.Single(r => r.Kind == ResourceKind.Health).Current.Value);
    }

    /// <summary>
    /// <b>Real defect, found via this exact chaining methodology, outside
    /// AP-02's own new files.</b> <see cref="WorldModelTemporalEnricher"/>
    /// (<c>src/NosAi.Core/WorldModel/Temporal/WorldModelTemporalEnricher.cs:68</c>,
    /// inside the private <c>EnrichMobs</c>) builds the "no prior sighting of
    /// this entity" case as:
    /// <code>WorldFact&lt;WorldVelocity&gt;.Unknown("no_prior_sighting_of_this_entity")</code>
    /// with NO <c>observedAtUtc</c> argument -- exactly the same bug pattern
    /// AP-01/A5 found and saw fixed in <c>TemporalBelief.EstimateVelocity</c>/
    /// <c>PredictPosition</c> (three sibling call sites in the same file were
    /// corrected to pass <c>current.ObservedAtUtc</c>/<c>asOfUtc</c>; this
    /// fourth call site, one level up in the caller that consumes
    /// <c>TemporalBelief</c>, was missed).
    /// <para>
    /// This call site is unreachable through today's real production wiring
    /// (<see cref="GameplayObservationProjector.Project"/> always leaves
    /// <see cref="WorldModelSnapshot.Mobs"/> empty -- AP-02's own explicit,
    /// documented scope boundary), so it does not regress anything AP-02
    /// shipped. It becomes reachable the moment a future phase bridges
    /// <c>PerceptionResult.Entities</c>/network mob sightings into
    /// <see cref="WorldModelSnapshot.Mobs"/> -- which <see cref="VisualObservationFusion"/>'s
    /// own XML remarks flag as exactly the next task in this area. Found
    /// here, before that phase starts, by chaining the current AP-02 primitives
    /// with <see cref="WorldModelTemporalEnricher"/> exactly as this audit
    /// command's item 4 asked, using a hand-built <see cref="Mob"/> to
    /// simulate what that future bridging would hand to <c>Enrich</c> --
    /// not a hypothetical contract invented here, since <c>EnrichMobs</c>
    /// already exists and already has this exact branch today.
    /// </para>
    /// <para>
    /// Deliberately red: the test picks <c>nowUtc</c> in the year 2099 (the
    /// same technique AP-01/A5 used for the same class of bug) so a truly
    /// deterministic implementation would stamp the new mob's Unknown
    /// velocity with a 2099 instant; the real implementation instead stamps
    /// real wall-clock time (2026 in this sandbox), breaking
    /// <see cref="WorldModelTemporalEnricher"/>'s own documented "the same
    /// two snapshots always enrich to the same result" guarantee for any mob
    /// seen for the first time.
    /// </para>
    /// </summary>
    [Fact]
    public void EnrichMobs_FirstSightingOfANewMob_LeaksRealWallClockTime_InsteadOfADeterministicInstant_KnownWorldModelTemporalEnricherGap()
    {
        DateTime year2099 = new(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        WorldModelSnapshot previousNoMobs = WorldModelSnapshot.Unknown("no_prior_fusion_cycle", year2099);

        var newMob = new Mob(
            new EntityId("mob-1"),
            WorldFact<WorldPosition>.Live(new WorldPosition(5, 5), 1.0, year2099),
            WorldFact<string>.Unknown("no_species_catalog", year2099),
            WorldFact<bool>.Unknown("hostility_not_observed", year2099),
            WorldFact<bool>.Live(true, 1.0, year2099),
            CombatantStatus.Empty);
        WorldModelSnapshot currentWithNewMob = previousNoMobs with
        {
            Mobs = EquatableArray<Mob>.From(new[] { newMob })
        };

        WorldModelSnapshot enriched = WorldModelTemporalEnricher.Enrich(previousNoMobs, currentWithNewMob, year2099, MaxAge, MaxObservationGap);

        Mob enrichedMob = Assert.Single(enriched.Mobs);
        Assert.False(enrichedMob.Velocity.HasValue); // correctly Unknown -- no prior sighting to derive from.
        Assert.Equal(
            year2099,
            enrichedMob.Velocity.ObservedAtUtc); // FAILS: real wall-clock time leaks through instead.
    }

    /// <summary>Direct companion pinning down the exact same-instant replay failure this causes, mirroring AP-01/A5's own <c>ThreeConsecutiveCycles...</c> determinism test but for a newly-sighted mob specifically.</summary>
    [Fact]
    public void EnrichMobs_FirstSightingOfANewMob_ReplayedTwice_DoesNotProduceBitForBitEqualResults_KnownWorldModelTemporalEnricherGap()
    {
        DateTime nowUtc = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
        WorldModelSnapshot previousNoMobs = WorldModelSnapshot.Unknown("no_prior_fusion_cycle", nowUtc);
        var newMob = new Mob(
            new EntityId("mob-1"),
            WorldFact<WorldPosition>.Live(new WorldPosition(5, 5), 1.0, nowUtc),
            WorldFact<string>.Unknown("no_species_catalog", nowUtc),
            WorldFact<bool>.Unknown("hostility_not_observed", nowUtc),
            WorldFact<bool>.Live(true, 1.0, nowUtc),
            CombatantStatus.Empty);
        WorldModelSnapshot currentWithNewMob = previousNoMobs with { Mobs = EquatableArray<Mob>.From(new[] { newMob }) };

        WorldModelSnapshot replayA = WorldModelTemporalEnricher.Enrich(previousNoMobs, currentWithNewMob, nowUtc, MaxAge, MaxObservationGap);
        Thread.Sleep(5);
        WorldModelSnapshot replayB = WorldModelTemporalEnricher.Enrich(previousNoMobs, currentWithNewMob, nowUtc, MaxAge, MaxObservationGap);

        // FAILS today: each replay's new-mob Velocity.ObservedAtUtc captures
        // a different real instant, so the two otherwise-identical replays
        // are not equal -- violating the "same inputs -> same output"
        // guarantee this audit was asked to check for explicitly (item 4).
        Assert.Equal(replayA, replayB);
    }

    /// <summary>
    /// <b>Second real defect, found via this exact chaining methodology,
    /// also outside AP-02's own new files.</b> While building the "green"
    /// determinism test above, leaving any field of
    /// <c>GameplayObservation.Unobserved(reason, atUtc)</c> un-overridden
    /// made the whole chain non-deterministic. Root cause isolated here,
    /// directly on <see cref="GameplayObservation.Unobserved"/> alone (no
    /// fusion/enrichment involved), at
    /// <c>src/NosAi.Runtime/LiveIntegration/GameplayProvider.cs:196-216</c>:
    /// the factory's own top-level <c>ObservedAtUtc</c> positional field
    /// correctly uses <c>atUtc ?? DateTime.UtcNow</c>, but every one of its
    /// sixteen per-field defaults (<c>PlayerPosition</c>, <c>MapId</c>,
    /// <c>StandingCell</c>, <c>Entities</c>, <c>HitBy</c>,
    /// <c>SelectedTarget</c>, <c>SkillsReady</c>, <c>Inventory</c>,
    /// <c>LastPickup</c>, <c>GroundItems</c>, and the four vitals
    /// <c>Hp</c>/<c>MaxHp</c>/<c>Mp</c>/<c>MaxMp</c> plus
    /// <c>HasTarget</c>/<c>InCombat</c>/<c>EntitiesInView</c>) is built via
    /// <c>ClassifiedValue&lt;T&gt;.Unknown(reason)</c> with NO
    /// <c>observedAtUtc</c> argument, which defaults to real
    /// <see cref="DateTime.UtcNow"/> -- completely disconnected from the
    /// <paramref name="atUtc"/> the caller explicitly supplied. This is the
    /// same bug class AP-01/A5 found in <c>TemporalBelief</c> and this audit
    /// found again in <c>WorldModelTemporalEnricher.EnrichMobs</c>, but here
    /// at its widest blast radius: any test or caller that builds an
    /// <c>Unobserved</c> baseline and overrides only SOME fields (a
    /// completely reasonable, common pattern -- see this file's own
    /// <c>RunOneCycle</c>, and <c>VisualObservationFusionTests.NetworkOnlySnapshot</c>'s
    /// analogous pattern for <c>VisualObservation</c>, which happens not to
    /// hit this because it never reads the leftover fields) silently gets a
    /// non-deterministic <c>MapId</c>/<c>PlayerPosition</c>/etc., which then
    /// flows through <see cref="GameplayObservationProjector.Project"/>
    /// (via <see cref="ClassifiedValueBridge.ToWorldFact{TSource,TResult}"/>,
    /// which faithfully preserves whatever <c>ObservedAtUtc</c> it is
    /// handed) into <c>Player.CurrentMap</c>'s own <c>ObservedAtUtc</c>.
    /// <para>
    /// Worth noting as an added inconsistency this test also surfaces: the
    /// SAME logical "map not observed" fact is represented twice in a
    /// projected <see cref="WorldModelSnapshot"/> -- <c>Player.CurrentMap</c>
    /// (leaks wall-clock time, as shown here) and <c>Map</c> itself, built
    /// via <c>MapModel.Unknown(..., nowUtc)</c> in
    /// <see cref="GameplayObservationProjector.Project"/>, which correctly
    /// threads <c>nowUtc</c> through and is NOT affected. One representation
    /// of "unknown map" is deterministic; the other, of the same underlying
    /// fact, is not.
    /// </para>
    /// </summary>
    [Fact]
    public void GameplayObservationUnobserved_LeavesEveryUnoverriddenFieldWallClockStamped_InsteadOfUsingItsOwnAtUtcParameter_KnownGameplayObservationGap()
    {
        DateTime year2099 = new(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        GameplayObservation observation = GameplayObservation.Unobserved("reason", year2099);

        // A deterministic implementation would stamp every per-field Unknown
        // with the same 2099 instant it was explicitly given; the real
        // implementation stamps real wall-clock time (2026 in this sandbox)
        // on every one of these instead.
        Assert.Equal(year2099, observation.MapId.ObservedAtUtc);
    }

    /// <summary>
    /// Direct replay-equality companion: two calls to
    /// <c>GameplayObservation.Unobserved</c> with the IDENTICAL <c>reason</c>
    /// and <c>atUtc</c> should be equal (records compare structurally), but
    /// are not, because each call's leftover fields independently capture a
    /// different real instant.
    /// </summary>
    [Fact]
    public void GameplayObservationUnobserved_CalledTwiceWithIdenticalArguments_DoesNotProduceEqualResults_KnownGameplayObservationGap()
    {
        DateTime fixedInstant = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

        GameplayObservation first = GameplayObservation.Unobserved("reason", fixedInstant);
        GameplayObservation second = GameplayObservation.Unobserved("reason", fixedInstant);

        Assert.Equal(first, second);
    }
}
