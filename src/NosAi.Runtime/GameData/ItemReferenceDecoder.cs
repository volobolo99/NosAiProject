using NosAi.Core.WorldModel;

namespace NosAi.Runtime.GameData;

/// <summary>
/// The static rules one item-table record declares: identification,
/// classification, equip slot, flags, context-dependent stat data and the
/// effects it applies. Distinct from <c>NosAi.Core.WorldModel.InventoryItem</c>/
/// <c>EquipmentItem</c>, which are a player's live, observed item state --
/// this is catalog data, the same "real reference, not a live fact"
/// category <see cref="GameReferenceDatabase"/> already documents for
/// every table it imports.
/// </summary>
/// <remarks>
/// <para>
/// The tag layout below comes from a community reference for the client's
/// file formats (https://nt-research.github.io/, "NOS files / NSgtdData /
/// Item.dat"), the same source already cited for <see cref="SkillReference"/>
/// and <see cref="MonsterReference"/>. That page itself cites three real
/// sources for this specific file (<c>NosCore.Parser/Parsers/ItemParser.cs</c>,
/// <c>OpenNos.Import.Console/ImportFactory.cs</c>,
/// <c>itempicker.atlagaming.eu</c>) -- a lead, not a ground truth, and (like
/// <see cref="MonsterReference"/>) not yet cross-checked against a real
/// client capture in this repository (this sandbox has no real NosTale
/// client).
/// </para>
/// <para>
/// <see cref="Slot"/> is the one field here promoted past a raw integer,
/// and for a specific, stronger reason than the others: the source's own
/// <c>EquipmentSlot</c> values (citing <c>OpenNos.Domain/EquipmentType.cs</c>)
/// match <see cref="NosAi.Core.WorldModel.EquipmentSlot"/>'s eighteen values
/// position-for-position and name-for-name (modulo spelling --
/// <c>MainWeapon</c>/<c>Sp</c>/<c>Wings</c> there are this project's
/// <see cref="EquipmentSlot.Weapon"/>/<see cref="EquipmentSlot.SpecialistCard"/>/
/// <see cref="EquipmentSlot.CostumeWings"/>) -- independently confirming the
/// same 18-value scheme this project already derived from
/// <c>itempicker.atlagaming.eu/items.json</c> and cross-checked against real
/// Italian item names (see <see cref="EquipmentSlot"/>'s own remarks). Three
/// independent sources agreeing on the same eighteen positions is strong
/// enough to type this field, even without a live client capture. A code
/// this project's enum does not define (the source's own <c>-1</c>,
/// "cannot be equipped") decodes to null rather than a fabricated member.
/// </para>
/// <para>
/// Everything else stays a raw integer for the same reason
/// <see cref="SkillReference"/> and <see cref="MonsterReference"/> keep
/// their own coded fields raw: the source names what each code or flag is
/// claimed to mean, but this decoder has not verified any of those claims
/// against an observed item, so promoting them would present a guess as a
/// fact. <see cref="Data"/> doubly so -- the source itself states its
/// twenty values mean different things depending on the item's own
/// <see cref="ItemType"/>, and resolving that needs the parser source code
/// this project does not vendor, not a guess made here. Every property is
/// null when its own tag or tag position is absent from the record, never
/// a fabricated default.
/// </para>
/// </remarks>
public sealed record ItemReference(
    int Vnum,
    int? Price,
    string NameKey,
    int? InventoryType,
    int? ItemType,
    int? ItemSubType,
    EquipmentSlot? Slot,
    int? IconId,
    int? VisualChangeId,
    int? AttackType,
    int? RequiredClass,
    int? Flag1,
    int? Flag2,
    int? Flag3,
    int? NoSelling,
    int? NoDropping,
    int? NoTrading,
    int? UseableMinilandItem,
    int? UseableMinilandItem2,
    int? ShowWarningOnUse,
    int? IsTimespaceRewardBox,
    int? ShowDescriptionOwner,
    int? Flag4,
    int? FollowMouseOnUse,
    int? ShowSomethingOnHover,
    int? CanBeColored,
    int? FemaleCanWear,
    int? MaleCanWear,
    int? NotUsed,
    int? PlaySoundOnPickup,
    int? UseReputationAsPrice,
    int? IsHeroLevelEquip,
    int? Flag5,
    int? IsLimited,
    IReadOnlyList<int?> Data,
    IReadOnlyList<BCardApplication> Effects,
    string? DescriptionKey);

/// <summary>
/// Decodes an item-table <see cref="NosRecord"/> into an
/// <see cref="ItemReference"/>. Pure and deterministic: the same record
/// always decodes to the same result, and a tag the record does not carry
/// leaves the properties it would have fed null rather than a guessed
/// value.
/// </summary>
/// <remarks>
/// Not yet cross-checked against a real client capture. See
/// <see cref="ItemReference"/>'s own remarks for the source of the tag
/// layout and why <see cref="ItemReference.Slot"/> alone is typed.
/// </remarks>
public static class ItemReferenceDecoder
{
    /// <summary>
    /// Decodes <paramref name="record"/>, or returns null when it carries
    /// no <c>VNUM</c> -- an item this decoder cannot identify is not worth
    /// a half-built result.
    /// </summary>
    public static ItemReference? Decode(NosRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Vnum is not int vnum)
            return null;

        NosField? name = record.Field("NAME");
        NosField? index = record.Field("INDEX");
        NosField? type = record.Field("TYPE");
        NosField? flag = record.Field("FLAG");
        NosField? data = record.Field("DATA");
        NosField? lineDesc = record.Field("LINEDESC");

        return new ItemReference(
            vnum,
            Price: record.Field("VNUM")?.Int(1),
            name?.Value(0) ?? string.Empty,
            InventoryType: index?.Int(0),
            ItemType: index?.Int(1),
            ItemSubType: index?.Int(2),
            Slot: DecodeSlot(index?.Int(3)),
            IconId: index?.Int(4),
            VisualChangeId: index?.Int(5),
            AttackType: type?.Int(0),
            RequiredClass: type?.Int(1),
            Flag1: flag?.Int(0),
            Flag2: flag?.Int(1),
            Flag3: flag?.Int(2),
            NoSelling: flag?.Int(3),
            NoDropping: flag?.Int(4),
            NoTrading: flag?.Int(5),
            UseableMinilandItem: flag?.Int(6),
            UseableMinilandItem2: flag?.Int(7),
            ShowWarningOnUse: flag?.Int(8),
            IsTimespaceRewardBox: flag?.Int(9),
            ShowDescriptionOwner: flag?.Int(10),
            Flag4: flag?.Int(11),
            FollowMouseOnUse: flag?.Int(12),
            ShowSomethingOnHover: flag?.Int(13),
            CanBeColored: flag?.Int(14),
            FemaleCanWear: flag?.Int(15),
            MaleCanWear: flag?.Int(16),
            NotUsed: flag?.Int(17),
            PlaySoundOnPickup: flag?.Int(18),
            UseReputationAsPrice: flag?.Int(19),
            IsHeroLevelEquip: flag?.Int(20),
            Flag5: flag?.Int(21),
            IsLimited: flag?.Int(22),
            Data: DecodeData(data),
            Effects: DecodeEffects(record),
            DescriptionKey: lineDesc?.Value(1));
    }

    /// <summary>
    /// Maps the source's raw <c>EquipmentSlot</c> code onto this project's
    /// own <see cref="EquipmentSlot"/> enum -- see <see cref="ItemReference"/>'s
    /// remarks for why this one field is typed. A code the enum does not
    /// define (the source's own <c>-1</c>, "cannot be equipped", or
    /// anything else unexpected) decodes to null, never a fabricated member.
    /// </summary>
    private static EquipmentSlot? DecodeSlot(int? raw) =>
        raw is int code && Enum.IsDefined(typeof(EquipmentSlot), code) ? (EquipmentSlot)code : null;

    /// <summary>
    /// The twenty <c>DATA</c> values, each preserved as read (or null when
    /// unparsable) rather than interpreted -- see <see cref="ItemReference"/>'s
    /// remarks on why this field cannot be given fixed names.
    /// </summary>
    private static IReadOnlyList<int?> DecodeData(NosField? data)
    {
        if (data is null)
            return Array.Empty<int?>();

        var values = new int?[data.Values.Count];
        for (int i = 0; i < values.Length; i++)
            values[i] = data.Int(i);
        return values;
    }

    private static IReadOnlyList<BCardApplication> DecodeEffects(NosRecord record)
    {
        var effects = new List<BCardApplication>();
        foreach (NosField buff in record.AllFields("BUFF"))
        {
            // Item.dat's own BUFF order, per the source: BCardVNUM,
            // EffectVal_1, EffectVal_2, BCardSub, Target -- no leading slot
            // index, matching monster.dat's BASIC/CARD (see
            // MonsterReferenceDecoder) rather than Skill.dat's own BASIC.
            int? bcardVnum = buff.Int(0);
            int? effectVal1 = buff.Int(1);
            int? effectVal2 = buff.Int(2);
            int? bcardSub = buff.Int(3);
            int? target = buff.Int(4);

            if (bcardVnum is int bv && effectVal1 is int e1
                && effectVal2 is int e2 && bcardSub is int bs && target is int t)
            {
                effects.Add(new BCardApplication(bv, bs, e1, e2, t));
            }
        }

        return effects;
    }
}
