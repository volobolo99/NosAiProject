using System;
using System.Collections.Generic;
using NosAi.Core.WorldModel;
using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;
using NosAi.Runtime.WorldModel.Fusion;
using Xunit;
using RuntimeDataSourceKind = NosAi.Runtime.Contracts.DataSourceKind;

namespace NosAi.Runtime.Tests;

/// <summary>
/// A drop disappearing from the ground and the controlled character having taken it are two
/// different facts, and the wire only ever states the first directly: <c>get</c> names a taker
/// type, not an identity, until <c>cond</c> resolves it. These tests pin
/// <see cref="GameplayObservationProjector.ProjectDropClaim"/>, the route that turns the most
/// recent <c>get</c> into <see cref="WorldModelSnapshot.LastDropClaim"/> without ever reading an
/// unresolved taker as "somebody else took it".
/// </summary>
public sealed class DropClaimProjectionTests
{
    private static readonly DateTime At = DateTime.UnixEpoch;

    [Fact]
    public void PickupNeverPublished_LeavesTheClaimUnknown()
    {
        WorldModelSnapshot snapshot = Project(WithPickup(
            ClassifiedValue<ItemPickup>.Unknown("no_pickup_observed_yet")));

        Assert.False(snapshot.LastDropClaim.HasValue);
    }

    [Fact]
    public void PickupByTheControlledCharacter_ProjectsAClaimOnThatDrop()
    {
        var pickup = new ItemPickup(TakerType: 1, TakerId: 1, DropId: 77, ByPlayer: true, At, RuntimeDataSourceKind.Live);

        WorldModelSnapshot snapshot = Project(WithPickup(
            ClassifiedValue<ItemPickup>.Live(pickup, At)));

        Assert.True(snapshot.LastDropClaim.HasValue);
        Assert.Equal("drop-77", snapshot.LastDropClaim.Value.Drop.Value);
        Assert.True(snapshot.LastDropClaim.Value.ByPlayer.HasValue);
        Assert.True(snapshot.LastDropClaim.Value.ByPlayer.Value);
    }

    [Fact]
    public void PickupByAnotherPlayer_ProjectsByPlayerFalse()
    {
        var pickup = new ItemPickup(TakerType: 1, TakerId: 555, DropId: 77, ByPlayer: false, At, RuntimeDataSourceKind.Live);

        WorldModelSnapshot snapshot = Project(WithPickup(
            ClassifiedValue<ItemPickup>.Live(pickup, At)));

        Assert.True(snapshot.LastDropClaim.Value.ByPlayer.HasValue);
        Assert.False(snapshot.LastDropClaim.Value.ByPlayer.Value);
    }

    /// <summary>
    /// Taker type 1 before <c>cond</c> has named an identity: the drop is genuinely gone -- a
    /// character matching "a player" took it -- so the claim must exist, but nobody has said
    /// which player, so <see cref="DropClaim.ByPlayer"/> must stay Unknown rather than fold to
    /// false. Reading it as false would tell a planner "somebody else took it", the very fact an
    /// actually-resolved non-player pickup produces, and a planner acting on that confusion would
    /// consider the pickup lost and repeat a collection that had, in truth, already succeeded.
    /// </summary>
    [Fact]
    public void PickupWithUnresolvedTaker_ClaimExistsButByPlayerStaysUnknown()
    {
        var pickup = new ItemPickup(TakerType: 1, TakerId: 0, DropId: 77, ByPlayer: null, At, RuntimeDataSourceKind.Live);

        WorldModelSnapshot snapshot = Project(WithPickup(
            ClassifiedValue<ItemPickup>.Live(pickup, At)));

        Assert.True(snapshot.LastDropClaim.HasValue);

        WorldFact<bool> byPlayer = snapshot.LastDropClaim.Value.ByPlayer;
        Assert.False(byPlayer.HasValue);

        // Explicit, because HasValue alone would not stop a careless caller from also reading
        // Value: WorldFact<bool>.Unknown still carries default(bool) there. Comparing the whole
        // fact against the shape an actually-resolved false pickup produces proves this is not
        // that fact -- an unresolved taker is not "somebody else took it".
        Assert.NotEqual(WorldFact<bool>.Derived(false, 1.0, At), byPlayer);
    }

    /// <summary>
    /// Proves the claim's id is formed the same way the ground-item projection forms its own, so
    /// a consumer can actually join <see cref="WorldModelSnapshot.LastDropClaim"/> against
    /// <see cref="WorldModelSnapshot.Drops"/> instead of holding two incompatible identifiers for
    /// the same drop.
    /// </summary>
    [Fact]
    public void ClaimedDropId_MatchesTheMatchingGroundItem()
    {
        var groundItem = new GroundItem(Vnum: 200, DropId: 77, X: 10, Y: 20, Amount: 1, OwnerId: 0, At, RuntimeDataSourceKind.Live);
        var pickup = new ItemPickup(TakerType: 1, TakerId: 1, DropId: 77, ByPlayer: true, At, RuntimeDataSourceKind.Live);

        WorldModelSnapshot snapshot = Project(WithPickup(
            ClassifiedValue<ItemPickup>.Live(pickup, At),
            ClassifiedValue<IReadOnlyList<GroundItem>>.Live(new List<GroundItem> { groundItem }, At)));

        Assert.True(snapshot.LastDropClaim.HasValue);
        Assert.Contains(snapshot.Drops, drop => drop.Id == snapshot.LastDropClaim.Value.Drop);
    }

    private static WorldModelSnapshot Project(GameplayObservation observation) =>
        GameplayObservationProjector.Project(observation, new EntityId("player-1"), version: 1, At);

    private static GameplayObservation WithPickup(
        ClassifiedValue<ItemPickup> pickup,
        ClassifiedValue<IReadOnlyList<GroundItem>>? groundItems = null) =>
        new(
            ClassifiedValue<int>.Derived(100, At),
            ClassifiedValue<int>.Derived(100, At),
            ClassifiedValue<int>.Derived(50, At),
            ClassifiedValue<int>.Derived(50, At),
            ClassifiedValue<bool>.Derived(false, At),
            ClassifiedValue<bool>.Derived(false, At),
            ClassifiedValue<int>.Derived(0, At),
            At)
        {
            LastPickup = pickup,
            GroundItems = groundItems ?? ClassifiedValue<IReadOnlyList<GroundItem>>.Unknown(GameplayObservation.NotPublishedReason),
        };
}
