using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Temporal;
using Xunit;

namespace NosAi.Core.Tests.WorldModel.Temporal;

/// <summary>
/// AP-01/A5 independent audit of <see cref="WorldModelTemporalEnricher"/>:
/// multi-cycle replay determinism, previous-list order-insensitivity, and
/// what happens with a duplicate <see cref="EntityId"/> inside one cycle's
/// Mob list -- exactly the scenarios named by the AP-01/A5 command ("prova
/// a costruire scenari con più chiamate concatenate... verifica che il
/// risultato sia sempre riproducibile... comportamento quando due Mob nella
/// stessa lista hanno lo stesso EntityId"). None of these are fixed here
/// (A5 owns no production file in this phase); each is either a passing
/// test confirming correct behaviour, or -- where marked -- a documented,
/// accepted gap, never silently patched over.
/// </summary>
public sealed class WorldModelTemporalEnricherDeterminismAndDuplicateIdTests
{
    private static readonly DateTime T0 = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(10);

    private static Mob MakeMob(string id, WorldPosition position, DateTime observedAt) =>
        new(new EntityId(id), WorldFact<WorldPosition>.Live(position, 1.0, observedAt),
            WorldFact<string>.Unknown("r"), WorldFact<bool>.Unknown("r"), WorldFact<bool>.Unknown("r"), CombatantStatus.Empty);

    private static WorldModelSnapshot SnapshotWithMobs(IEnumerable<Mob> mobs) =>
        WorldModelSnapshot.Unknown("bootstrap") with { Mobs = EquatableArray<Mob>.From(mobs) };

    /// <summary>
    /// Unlike <see cref="SnapshotWithMobs"/>, also gives the Player a known,
    /// Live position. Needed for a genuine full-snapshot equality check:
    /// leaving <c>Player.Position</c> Unknown throughout (as
    /// <see cref="SnapshotWithMobs"/> does) would make every cycle's
    /// <c>TemporalBelief.EstimateVelocity</c> call for the Player take its
    /// "insufficient_position_history" early return, which -- see
    /// <c>TemporalBeliefBoundaryTests.EstimateVelocity_InsufficientHistoryResult_LeaksRealWallClockTime_...</c>
    /// -- stamps real wall-clock time rather than a deterministic instant,
    /// making two otherwise-identical replays compare unequal for a reason
    /// unrelated to whatever this test is actually checking. Giving the
    /// Player a real position keeps every velocity computation on the
    /// normal, fully input-derived (deterministic) path instead.
    /// </summary>
    private static WorldModelSnapshot SnapshotWithPlayerAndMobs(WorldPosition playerPosition, DateTime observedAt, IEnumerable<Mob> mobs)
    {
        WorldModelSnapshot baseline = WorldModelSnapshot.Unknown("bootstrap");
        return baseline with
        {
            Player = baseline.Player with { Position = WorldFact<WorldPosition>.Live(playerPosition, 1.0, observedAt) },
            Mobs = EquatableArray<Mob>.From(mobs)
        };
    }

    // ---- Multi-cycle determinism -------------------------------------------

    [Fact]
    public void ThreeConsecutiveCycles_ReplayedTwice_ProduceBitForBitEqualFinalSnapshots()
    {
        WorldModelSnapshot cycle0 = SnapshotWithPlayerAndMobs(new WorldPosition(0, 0), T0, new[] { MakeMob("mob-1", new WorldPosition(0, 0), T0) });
        WorldModelSnapshot cycle1Input = SnapshotWithPlayerAndMobs(new WorldPosition(1, 0), T0 + TimeSpan.FromSeconds(1), new[] { MakeMob("mob-1", new WorldPosition(2, 0), T0 + TimeSpan.FromSeconds(1)) });
        WorldModelSnapshot cycle2Input = SnapshotWithPlayerAndMobs(new WorldPosition(3, 0), T0 + TimeSpan.FromSeconds(2), new[] { MakeMob("mob-1", new WorldPosition(5, 0), T0 + TimeSpan.FromSeconds(2)) });

        WorldModelSnapshot RunChain()
        {
            WorldModelSnapshot enriched0 = WorldModelTemporalEnricher.Enrich(
                WorldModelSnapshot.Unknown("no_prior_cycle"), cycle0, T0, MaxAge, MaxGap);
            WorldModelSnapshot enriched1 = WorldModelTemporalEnricher.Enrich(
                enriched0, cycle1Input, T0 + TimeSpan.FromSeconds(1), MaxAge, MaxGap);
            WorldModelSnapshot enriched2 = WorldModelTemporalEnricher.Enrich(
                enriched1, cycle2Input, T0 + TimeSpan.FromSeconds(2), MaxAge, MaxGap);
            return enriched2;
        }

        WorldModelSnapshot resultA = RunChain();
        WorldModelSnapshot resultB = RunChain();

        Assert.Equal(resultA, resultB);
        Assert.Equal(resultA.GetHashCode(), resultB.GetHashCode());
        // Sanity: the chain actually derived non-trivial velocities for both
        // the Player and the mob, so this is not a vacuous "two empty/Unknown
        // snapshots are equal" test.
        Assert.True(resultA.Mobs[0].Velocity.HasValue);
        Assert.True(resultA.Player.Velocity.HasValue);
    }

    // ---- Order-insensitivity of the PREVIOUS list (unique ids) -------------

    [Fact]
    public void PreviousMobListOrder_DoesNotAffectTheDerivedVelocity_WhenIdsAreUnique()
    {
        DateTime previousInstant = T0 - TimeSpan.FromSeconds(1);
        Mob previousA = MakeMob("mob-A", new WorldPosition(0, 0), previousInstant);
        Mob previousB = MakeMob("mob-B", new WorldPosition(10, 10), previousInstant);
        Mob previousC = MakeMob("mob-C", new WorldPosition(-5, -5), previousInstant);

        WorldModelSnapshot previousInOrder = SnapshotWithMobs(new[] { previousA, previousB, previousC });
        WorldModelSnapshot previousReversed = SnapshotWithMobs(new[] { previousC, previousB, previousA });

        WorldModelSnapshot current = SnapshotWithMobs(new[]
        {
            MakeMob("mob-A", new WorldPosition(4, 0), T0),
            MakeMob("mob-B", new WorldPosition(10, 14), T0),
            MakeMob("mob-C", new WorldPosition(-5, -1), T0)
        });

        WorldModelSnapshot enrichedInOrder = WorldModelTemporalEnricher.Enrich(previousInOrder, current, T0, MaxAge, MaxGap);
        WorldModelSnapshot enrichedReversed = WorldModelTemporalEnricher.Enrich(previousReversed, current, T0, MaxAge, MaxGap);

        // Deliberately scoped to .Mobs, not the whole snapshot: both
        // previousInOrder/previousReversed/current here leave Player.Position
        // Unknown (via SnapshotWithMobs), which independently hits the
        // wall-clock-timestamp gap documented in
        // TemporalBeliefBoundaryTests.EstimateVelocity_InsufficientHistoryResult_LeaksRealWallClockTime_...
        // for the (here, irrelevant) Player.Velocity field. That is a
        // separate, already-reported defect; comparing only .Mobs keeps this
        // test focused on what it actually names: mob-matching order
        // insensitivity.
        Assert.Equal(enrichedInOrder.Mobs, enrichedReversed.Mobs);
        for (int i = 0; i < enrichedInOrder.Mobs.Count; i++)
            Assert.Equal(enrichedInOrder.Mobs[i].Velocity, enrichedReversed.Mobs[i].Velocity);
    }

    // ---- Duplicate EntityId within ONE cycle's current.Mobs list -----------

    /// <summary>
    /// DOCUMENTED GAP, not fixed here: <see cref="WorldModelTemporalEnricher"/>
    /// never validates that <c>current.Mobs</c> has unique
    /// <see cref="EntityId"/>s. Two entries sharing one id are each enriched
    /// independently and BOTH survive into the output -- nothing merges,
    /// flags, or rejects the duplicate. The doc comment on
    /// <see cref="EntityId"/> itself says a caller should be able to "look
    /// up what is at entity id N", which silently assumes a uniqueness this
    /// type never enforces. Upstream Sensor Fusion/AP-02 is expected to
    /// never produce such a list, but nothing here would catch it if it
    /// did. Left as an explicit open item for A6/A4: a future validation
    /// step, or an explicit dedupe-by-id policy, would belong in the
    /// projector/enricher's shared caller, not in this pure per-entity
    /// enrichment function.
    /// </summary>
    [Fact]
    public void DuplicateEntityIdInCurrentMobs_IsNotMergedOrRejected_BothEntriesSurvive_DocumentedGap()
    {
        WorldModelSnapshot previous = WorldModelSnapshot.Unknown("no_prior_cycle");
        WorldModelSnapshot current = SnapshotWithMobs(new[]
        {
            MakeMob("mob-dup", new WorldPosition(0, 0), T0),
            MakeMob("mob-dup", new WorldPosition(99, 99), T0)
        });

        WorldModelSnapshot enriched = WorldModelTemporalEnricher.Enrich(previous, current, T0, MaxAge, MaxGap);

        Assert.Equal(2, enriched.Mobs.Count);
        Assert.Equal(new EntityId("mob-dup"), enriched.Mobs[0].Id);
        Assert.Equal(new EntityId("mob-dup"), enriched.Mobs[1].Id);
        // The two duplicate-id entries keep their own distinct positions --
        // no fields are merged or overwritten between them.
        Assert.Equal(new WorldPosition(0, 0), enriched.Mobs[0].Position.Value);
        Assert.Equal(new WorldPosition(99, 99), enriched.Mobs[1].Position.Value);
    }

    /// <summary>
    /// DOCUMENTED GAP, not fixed here: when the PREVIOUS cycle's own
    /// <c>Mobs</c> list has a duplicate id -- something nothing in AP-01/A1
    /// or AP-01/A2 prevents either -- <c>EnrichMobs</c>'s internal
    /// <c>Dictionary&lt;EntityId, Mob&gt;</c> build silently keeps only the
    /// LAST entry for that id (<c>previousById[mob.Id] = mob;</c>
    /// overwrites the earlier one). This is deterministic for a fixed list
    /// (the same input order always produces the same winner), but it
    /// directly contradicts the general "matching is insensitive to list
    /// order" property confirmed above
    /// (<see cref="PreviousMobListOrder_DoesNotAffectTheDerivedVelocity_WhenIdsAreUnique"/>):
    /// that property silently stops holding the moment a duplicate id is
    /// present in <c>previous</c>, because which duplicate survives to be
    /// matched against then IS a function of list order. Pinned down here
    /// so a future change cannot alter which duplicate wins without a test
    /// noticing.
    /// </summary>
    [Fact]
    public void DuplicateEntityIdInPreviousMobs_MatchesAgainstWhicheverIsLastInListOrder_DocumentedGap()
    {
        DateTime previousInstant = T0 - TimeSpan.FromSeconds(1);
        // Same id, two different positions -- only the second (last) one
        // should end up surviving in the internal lookup dictionary.
        Mob firstDuplicate = MakeMob("mob-dup", new WorldPosition(0, 0), previousInstant);
        Mob lastDuplicate = MakeMob("mob-dup", new WorldPosition(100, 0), previousInstant);

        WorldModelSnapshot previous = SnapshotWithMobs(new[] { firstDuplicate, lastDuplicate });
        WorldModelSnapshot current = SnapshotWithMobs(new[] { MakeMob("mob-dup", new WorldPosition(103, 0), T0) });

        WorldModelSnapshot enriched = WorldModelTemporalEnricher.Enrich(previous, current, T0, MaxAge, MaxGap);

        // Matched against lastDuplicate (100,0) -> (103,0) over 1s = 3/s.
        // Matched against firstDuplicate (0,0) instead would have given 103/s.
        Assert.True(enriched.Mobs[0].Velocity.HasValue);
        Assert.Equal(3f, enriched.Mobs[0].Velocity.Value.DxPerSecond);
    }
}
