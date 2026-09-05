namespace NosAi.Core.WorldModel;

/// <summary>Categoria funzionale di un oggetto, come derivabile dal catalogo o dalla borsa.</summary>
public enum ItemCategory : byte
{
    Unknown = 0,
    Weapon = 1,
    Armor = 2,
    Accessory = 3,
    Consumable = 4,
    Material = 5,
    QuestItem = 6,
    Specialist = 7,
    Costume = 8,
    Fairy = 9,
    Pet = 10,
    Miscellaneous = 11
}

/// <summary>Slot di equipaggiamento del personaggio. Gli ordinali seguono l'ordine del pacchetto <c>equip</c> del client.</summary>
public enum EquipmentSlot : byte
{
    MainWeapon = 0,
    Armor = 1,
    Hat = 2,
    Gloves = 3,
    Boots = 4,
    SecondaryWeapon = 5,
    Necklace = 6,
    Ring = 7,
    Bracelet = 8,
    Mask = 9,
    Fairy = 10,
    Amulet = 11,
    Specialist = 12,
    CostumeSuit = 13,
    CostumeHat = 14,
    WeaponSkin = 15,
    Unknown = 255
}

/// <summary>Statistiche aggregate di un pezzo equipaggiato. Sono sempre DERIVED dal catalogo + rarità/upgrade, mai LIVE.</summary>
public readonly record struct EquipmentStats(int Attack, int Defense, int MagicDefense, int RangeDefense, int Elemental);

/// <summary>Oggetto in uno slot dell'inventario.</summary>
public sealed record InventoryItemState(
    InventorySlotId Slot,
    Fact<TemplateId> Item,
    Fact<ItemCategory> Category,
    Fact<int> Quantity,
    Fact<sbyte> Rarity,
    Fact<byte> Upgrade,
    Fact<bool> Bound) : IFactCarrier
{
    public FactSummary Summarize()
    {
        FactSummary summary = FactSummary.Empty;
        summary.Add(Item);
        summary.Add(Category);
        summary.Add(Quantity);
        summary.Add(Rarity);
        summary.Add(Upgrade);
        summary.Add(Bound);
        return summary;
    }

    public static InventoryItemState Unknown(InventorySlotId slot, string reason, long observedAtUnixMillis = 0) => new(
        slot,
        Fact<TemplateId>.Unknown(reason, observedAtUnixMillis),
        Fact<ItemCategory>.Unknown(reason, observedAtUnixMillis),
        Fact<int>.Unknown(reason, observedAtUnixMillis),
        Fact<sbyte>.Unknown(reason, observedAtUnixMillis),
        Fact<byte>.Unknown(reason, observedAtUnixMillis),
        Fact<bool>.Unknown(reason, observedAtUnixMillis));
}

/// <summary>
/// Pezzo equipaggiato. Uno slot con <see cref="Item"/> UNKNOWN è uno slot che
/// nessuno ha letto, non uno slot vuoto: lo slot vuoto osservato ha
/// <see cref="Item"/> conosciuto con <see cref="TemplateId.None"/>.
/// </summary>
public sealed record EquipmentItemState(
    EquipmentSlot Slot,
    Fact<TemplateId> Item,
    Fact<sbyte> Rarity,
    Fact<byte> Upgrade,
    Fact<EquipmentStats> Stats) : IFactCarrier
{
    public bool IsObservedEmpty => Item.TryGetValue(out TemplateId item) && item.IsNone;

    public FactSummary Summarize()
    {
        FactSummary summary = FactSummary.Empty;
        summary.Add(Item);
        summary.Add(Rarity);
        summary.Add(Upgrade);
        summary.Add(Stats);
        return summary;
    }

    public static EquipmentItemState Unknown(EquipmentSlot slot, string reason, long observedAtUnixMillis = 0) => new(
        slot,
        Fact<TemplateId>.Unknown(reason, observedAtUnixMillis),
        Fact<sbyte>.Unknown(reason, observedAtUnixMillis),
        Fact<byte>.Unknown(reason, observedAtUnixMillis),
        Fact<EquipmentStats>.Unknown(reason, observedAtUnixMillis));
}
