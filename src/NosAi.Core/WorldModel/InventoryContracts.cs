namespace NosAi.Core.WorldModel;

/// <summary>One stack of items in a <see cref="Player"/>'s inventory (not equipped -- see <see cref="EquipmentItem"/>).</summary>
public sealed record InventoryItem(
    ItemId Id,
    WorldFact<string> Name,
    WorldFact<int> Quantity,
    WorldFact<int> SlotIndex);

/// <summary>Body/gear slot an <see cref="EquipmentItem"/> occupies. Generic across games -- unused slots for a given client simply never appear in a <see cref="Player"/>'s equipment collection.</summary>
public enum EquipmentSlot
{
    Weapon = 0,
    Shield = 1,
    Helmet = 2,
    Armor = 3,
    Gloves = 4,
    Boots = 5,
    Accessory1 = 6,
    Accessory2 = 7
}

/// <summary>One item currently equipped by a <see cref="Player"/>.</summary>
public sealed record EquipmentItem(
    ItemId Id,
    WorldFact<string> Name,
    EquipmentSlot Slot,
    WorldFact<bool> IsEquipped);
