using System.Text.Json;
using NosAi.ControlPanel;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.ControlPanel.Tests;

/// <summary>
/// Il lettore dello snapshot legge tutti e quattordici i campi del baseline,
/// non solo i cinque che il pannello disegnava. Un campo non osservato resta
/// UNKNOWN col motivo; una lista vuota è un'assenza guardata, non UNKNOWN.
/// </summary>
public sealed class GameplayWireReaderTests
{
    private static GameplayPanelRead Read(string valueJson) => GameplayWireReader.Read(
        Parse("{\"gameplayBaseline\":{\"source\":\"DERIVED\",\"hasObservedValue\":true,\"value\":" + valueJson + "}}"));

    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void InventoryIsReadWithKindSlotVnumAmountRarity()
    {
        GameplayPanelRead read = Read("""
            { "inventory": { "value": [ { "inventoryKind": 2, "slot": 34, "vnum": 2006, "amount": 1, "rarity": 0 } ], "source": "LIVE", "hasObservedValue": true } }
            """);

        Assert.True(read.Inventory.HasValue);
        InventorySlotReading slot = Assert.Single(read.Inventory.Value);
        Assert.Equal(2, slot.InventoryKind);
        Assert.Equal(34, slot.Slot);
        Assert.Equal(2006, slot.Vnum);
        Assert.Equal(1, slot.Amount);
        Assert.Equal(0, slot.Rarity);
        Assert.Equal(DataSourceKind.Live, read.Inventory.Source);
    }

    [Fact]
    public void SelectedTargetIsReadWithEntityIdAndType()
    {
        GameplayPanelRead read = Read("""
            { "selectedTarget": { "value": { "entityId": 42, "entityType": 2 }, "source": "LIVE", "hasObservedValue": true } }
            """);

        Assert.True(read.SelectedTarget.HasValue);
        Assert.Equal(42, read.SelectedTarget.Value.EntityId);
        Assert.Equal(2, read.SelectedTarget.Value.EntityType);
    }

    [Fact]
    public void GroundItemsAreReadWithDropVnumPositionAndAmount()
    {
        GameplayPanelRead read = Read("""
            { "groundItems": { "value": [ { "dropId": 8, "vnum": 8, "x": 10, "y": 20, "amount": 1, "ownerId": 5 } ], "source": "LIVE", "hasObservedValue": true } }
            """);

        GroundItem ground = Assert.Single(read.GroundItems.Value);
        Assert.Equal(8, ground.DropId);
        Assert.Equal(8, ground.Vnum);
        Assert.Equal(10, ground.X);
        Assert.Equal(20, ground.Y);
        Assert.Equal(1, ground.Amount);
    }

    [Fact]
    public void LastPickupIsReadAndNullByPlayerStaysNullNotFalse()
    {
        GameplayPanelRead read = Read("""
            { "lastPickup": { "value": { "dropId": 8, "takerType": 1, "takerId": 5, "byPlayer": null }, "source": "LIVE", "hasObservedValue": true } }
            """);

        Assert.True(read.LastPickup.HasValue);
        Assert.Equal(8, read.LastPickup.Value.DropId);
        Assert.Null(read.LastPickup.Value.ByPlayer);
    }

    [Fact]
    public void SkillsReadyAreReadWithSlot()
    {
        GameplayPanelRead read = Read("""
            { "skillsReady": { "value": [ { "slot": 3 }, { "slot": 7 } ], "source": "DERIVED", "hasObservedValue": true } }
            """);

        Assert.Equal(2, read.SkillsReady.Value.Count);
        Assert.Equal(3, read.SkillsReady.Value[0].Slot);
        Assert.Equal(7, read.SkillsReady.Value[1].Slot);
    }

    [Fact]
    public void InCombatIsReadAsABool()
    {
        GameplayPanelRead read = Read("""
            { "inCombat": { "value": true, "source": "LIVE", "hasObservedValue": true } }
            """);

        Assert.True(read.InCombat.HasValue);
        Assert.True(read.InCombat.Value);
    }

    [Fact]
    public void MissingFieldsAreUnknownWithTheProducersReason()
    {
        GameplayPanelRead read = Read("{}");

        Assert.False(read.Inventory.HasValue);
        Assert.False(read.SelectedTarget.HasValue);
        Assert.False(read.GroundItems.HasValue);
        Assert.False(read.LastPickup.HasValue);
        Assert.False(read.SkillsReady.HasValue);
        Assert.False(read.InCombat.HasValue);
        Assert.Equal("not_published_by_provider", read.Inventory.FailureReason);
    }

    [Fact]
    public void AnEmptyInventoryListIsEmptyNotUnknown()
    {
        GameplayPanelRead read = Read("""
            { "inventory": { "value": [], "source": "LIVE", "hasObservedValue": true } }
            """);

        Assert.True(read.Inventory.HasValue);
        Assert.Empty(read.Inventory.Value);
    }

    [Fact]
    public void CachedProvenanceIsPreservedNotCollapsedIntoLive()
    {
        GameplayPanelRead read = Read("""
            { "inventory": { "value": [ { "inventoryKind": 2, "slot": 1, "vnum": 2006, "amount": 1, "rarity": 0 } ], "source": "CACHED", "hasObservedValue": true } }
            """);

        Assert.Equal(DataSourceKind.Cached, read.Inventory.Source);
        Assert.Equal(DataSourceKind.Cached, read.Inventory.Value[0].Source);
    }
}
