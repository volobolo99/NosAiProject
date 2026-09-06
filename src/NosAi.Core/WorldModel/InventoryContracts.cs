namespace NosAi.Core.WorldModel;

/// <summary>One stack of items in a <see cref="Player"/>'s inventory (not equipped -- see <see cref="EquipmentItem"/>).</summary>
public sealed record InventoryItem(
    ItemId Id,
    WorldFact<string> Name,
    WorldFact<int> Quantity,
    WorldFact<int> SlotIndex);

/// <summary>
/// Body/gear slot an <see cref="EquipmentItem"/> occupies.
/// </summary>
/// <remarks>
/// <para>
/// These eighteen values and their order are NosTale's own, not a
/// generic placeholder: cross-checked against a real third-party NosTale
/// item database (<c>itempicker.atlagaming.eu</c>, an independently
/// verified source -- its <c>items.json</c> carries exactly 7727 entries,
/// matching this project's own real client import count exactly, and its
/// <c>skills.json</c> matches this project's own <c>SkillReferenceDecoder</c>
/// field-for-field against a real captured skill; see
/// <c>docs/research/NOSTALE_COMMUNITY_KNOWLEDGE_2026-09-05.md</c>). Each
/// slot's identity here is read off real, named Italian item examples in
/// that dataset, not guessed from the slot's numeric position alone --
/// e.g. slot 10 carries "Fata del fuoco"/"Fire Fairy" items, slot 17
/// carries items whose own name literally contains "Mini pet".
/// </para>
/// <para>
/// One value from that same source is deliberately <b>not</b> a case
/// here: item records also carry <c>-1</c>, meaning "this item cannot be
/// equipped at all" (a raid chest, a pearl, ...). That is a catalog fact
/// about an item, not a slot an <see cref="EquipmentItem"/> could ever
/// occupy, so it has no member here.
/// </para>
/// </remarks>
public enum EquipmentSlot
{
    Weapon = 0,
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
    SpecialistCard = 12,
    CostumeSuit = 13,
    CostumeHat = 14,
    WeaponSkin = 15,
    CostumeWings = 16,
    MiniPet = 17
}

/// <summary>One item currently equipped by a <see cref="Player"/>.</summary>
public sealed record EquipmentItem(
    ItemId Id,
    WorldFact<string> Name,
    EquipmentSlot Slot,
    WorldFact<bool> IsEquipped);
