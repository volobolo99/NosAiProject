using NosAi.Core.WorldModel;
using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;
using NosAi.Runtime.WorldModel.Fusion;
using Xunit;
using RuntimeDataSourceKind = NosAi.Runtime.Contracts.DataSourceKind;

namespace NosAi.Runtime.Tests.WorldModel.Fusion;

/// <summary>
/// AP-01/A5 independent audit of <see cref="GameplayObservationProjector"/>:
/// what happens when the wire reports a negative vnum/slot/amount/id (AP-01/A5
/// command S:4). Decision recorded by this audit, as explicitly asked for
/// rather than left open: today this is NOT treated as a defect requiring a
/// fix. <see cref="ItemId"/>/<see cref="EntityId"/> are documented opaque
/// wire-mirroring identifiers (Identifiers.cs: "mirrors the client's own
/// single entity-id numbering"); nothing in this repository parses them
/// back into a number; and no reference catalogue exists yet that could
/// reject an out-of-range vnum (the projector's own XML doc explains why
/// Mobs/Npcs/Skills/Equipment stay empty for the identical reason: "no
/// reference catalogue exists").
///
/// These tests exist so (a) the current, tolerant behaviour is pinned down
/// and cannot silently change, and (b) a future AP-02+ catalogue
/// integration has a concrete note of what "negative" looks like today, in
/// case it turns out to signal wire corruption once a real catalogue can
/// validate vnums/slots.
/// </summary>
public sealed class GameplayObservationProjectorBoundaryTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly EntityId PlayerId = new("player-1");

    [Fact]
    public void NegativeVnum_ProjectsIntoAnItemId_ThatCarriesTheMinusSignAsPlainText()
    {
        GameplayObservation observation = GameplayObservation.Unobserved("reason", Now) with
        {
            Inventory = ClassifiedValue<IReadOnlyList<InventorySlotReading>>.Live(
                new[] { new InventorySlotReading(InventoryKind: 2, Slot: 0, Vnum: -5, Amount: 1, Rarity: 0, Now, RuntimeDataSourceKind.Live) },
                Now)
        };

        WorldModelSnapshot snapshot = GameplayObservationProjector.Project(observation, PlayerId, version: 1, Now);

        InventoryItem item = Assert.Single(snapshot.Player.Inventory.Value);
        // Not rejected, not clamped to zero, not marked Unknown -- "-5"
        // round-trips as a syntactically valid but semantically suspicious
        // ItemId. ItemId's own constructor only rejects null/empty/whitespace.
        Assert.Equal("-5", item.Id.Value);
    }

    [Fact]
    public void NegativeSlotIndex_IsReportedAsAConfidentlyKnownValue_NotAsUnknown()
    {
        GameplayObservation observation = GameplayObservation.Unobserved("reason", Now) with
        {
            Inventory = ClassifiedValue<IReadOnlyList<InventorySlotReading>>.Live(
                new[] { new InventorySlotReading(InventoryKind: 2, Slot: -1, Vnum: 100, Amount: 1, Rarity: 0, Now, RuntimeDataSourceKind.Live) },
                Now)
        };

        WorldModelSnapshot snapshot = GameplayObservationProjector.Project(observation, PlayerId, version: 1, Now);

        InventoryItem item = Assert.Single(snapshot.Player.Inventory.Value);
        // A slot of -1 is a common sentinel elsewhere ("no slot"/"not
        // applicable"); the projector has no notion of that and reports it
        // as a confidently Live, HasValue == true fact rather than Unknown.
        // Not fixed here: GameTrafficObserver.cs's own doc on
        // InventorySlotReading.Slot does not document -1 as meaning
        // anything special, so there is no sentinel contract to honor
        // today -- but if the wire protocol ever adopts one, this would
        // misrepresent "not applicable" as "observed at slot -1".
        Assert.True(item.SlotIndex.HasValue);
        Assert.Equal(-1, item.SlotIndex.Value);
    }

    [Fact]
    public void NegativeAmount_PassesThroughUnvalidated()
    {
        GameplayObservation observation = GameplayObservation.Unobserved("reason", Now) with
        {
            Inventory = ClassifiedValue<IReadOnlyList<InventorySlotReading>>.Live(
                new[] { new InventorySlotReading(InventoryKind: 2, Slot: 0, Vnum: 100, Amount: -3, Rarity: 0, Now, RuntimeDataSourceKind.Live) },
                Now)
        };

        WorldModelSnapshot snapshot = GameplayObservationProjector.Project(observation, PlayerId, version: 1, Now);

        InventoryItem item = Assert.Single(snapshot.Player.Inventory.Value);
        Assert.Equal(-3, item.Quantity.Value);
    }

    [Fact]
    public void NegativeDropId_ProducesADoubleDashEntityId_CosmeticButNotRejected()
    {
        GameplayObservation observation = GameplayObservation.Unobserved("reason", Now) with
        {
            GroundItems = ClassifiedValue<IReadOnlyList<GroundItem>>.Live(
                new[] { new GroundItem(Vnum: 42, DropId: -7, X: 0, Y: 0, Amount: 1, OwnerId: 0, Now, RuntimeDataSourceKind.Live) },
                Now)
        };

        WorldModelSnapshot snapshot = GameplayObservationProjector.Project(observation, PlayerId, version: 1, Now);

        Drop drop = Assert.Single(snapshot.Drops);
        // "drop-" + "-7" == "drop--7": a harmless but slightly odd double
        // dash, still a unique, non-empty, valid EntityId.
        Assert.Equal("drop--7", drop.Id.Value);
    }
}
