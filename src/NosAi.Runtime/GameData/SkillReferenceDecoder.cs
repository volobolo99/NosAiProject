namespace NosAi.Runtime.GameData;

/// <summary>
/// One effect a skill applies, exactly as its record names it -- a
/// reference into the separate BCard catalog, never interpreted into a
/// human-readable description here. Resolving <see cref="BCardVnum"/>
/// into what it actually does needs the BCard table's own definition and,
/// for several of its variants, other tables this decoder does not touch;
/// inventing that interpretation here would put an unverified effect in
/// front of any caller that reads it as fact.
/// </summary>
public sealed record BCardApplication(
    int BCardVnum,
    int BCardSub,
    int EffectVal1,
    int EffectVal2,
    int Target);

/// <summary>
/// The static rules one skill's record declares: cost, per-class level
/// requirement, targeting and the effects it applies. Distinct from
/// <c>NosAi.Core.WorldModel.Skill</c>, which is a player's live, observed
/// skill state -- this is catalog data, the same "real reference, not a
/// live fact" category <see cref="GameReferenceDatabase"/> already
/// documents for every table it imports.
/// </summary>
/// <remarks>
/// Only the tuple positions this decoder resolved with confidence are
/// promoted to a named property. Several coded values the record also
/// carries (element, attack type, secondary weapon, hit type, target
/// group, ...) are deliberately left undecoded: this decoder does not
/// have a verified mapping from those integer codes to their in-game
/// meaning, and <see cref="TargetType"/>/<see cref="HitType"/>/
/// <see cref="TargetGroup"/> below are kept as raw codes for the same
/// reason -- a caller that needs the actual meaning must not read a
/// guess. Every property is null when its own tag is absent from the
/// record, never a fabricated default.
/// </remarks>
public sealed record SkillReference(
    int Vnum,
    string NameKey,
    int? CpCost,
    int? GoldCost,
    int? SpecialCost,
    int? JobLevel,
    int? AdventurerLevel,
    int? SwordsmanLevel,
    int? ArcherLevel,
    int? MageLevel,
    int? TargetType,
    int? HitType,
    int? Range,
    int? TargetRange,
    int? TargetGroup,
    int? CastTimeRaw,
    int? CooldownRaw,
    int? MpCost,
    IReadOnlyList<BCardApplication> Effects);

/// <summary>
/// Decodes one skill-table <see cref="NosRecord"/> into a
/// <see cref="SkillReference"/>. Pure and deterministic: the same record
/// always decodes to the same result, and a tag the record does not
/// carry leaves the properties it would have fed null rather than a
/// guessed value.
/// </summary>
/// <remarks>
/// <b>Not yet cross-checked against a live client value.</b> The tuple
/// positions below come from this decoder's own analysis of the record
/// shape, not from anything the client declares by name (only the tag
/// names themselves -- <c>COST</c>, <c>LEVEL</c>, <c>TARGET</c>, ...--
/// are self-describing; which slot inside each is which value is this
/// decoder's own claim). Treat every value this produces as provisional
/// until at least one has been checked against a real skill's cost/
/// cooldown as shown by a live client, per this project's own rule that
/// an unverified external/derived mapping is a lead, not a fact.
/// </remarks>
public static class SkillReferenceDecoder
{
    /// <summary>
    /// Decodes <paramref name="record"/>, or returns null when it carries
    /// no <c>VNUM</c> -- a skill this decoder cannot identify is not
    /// worth a half-built result.
    /// </summary>
    public static SkillReference? Decode(NosRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Vnum is not int vnum)
            return null;

        NosField? name = record.Field("NAME");
        NosField? cost = record.Field("COST");
        NosField? level = record.Field("LEVEL");
        NosField? target = record.Field("TARGET");
        NosField? data = record.Field("DATA");

        return new SkillReference(
            vnum,
            name?.Value(0) ?? string.Empty,
            CpCost: cost?.Int(0),
            GoldCost: cost?.Int(1),
            SpecialCost: cost?.Int(2),
            JobLevel: level?.Int(0),
            AdventurerLevel: level?.Int(1),
            SwordsmanLevel: level?.Int(2),
            ArcherLevel: level?.Int(3),
            MageLevel: level?.Int(4),
            TargetType: target?.Int(0),
            HitType: target?.Int(1),
            Range: target?.Int(2),
            TargetRange: target?.Int(3),
            TargetGroup: target?.Int(4),
            CastTimeRaw: data?.Int(4),
            CooldownRaw: data?.Int(5),
            MpCost: data?.Int(8),
            Effects: DecodeEffects(record));
    }

    private static IReadOnlyList<BCardApplication> DecodeEffects(NosRecord record)
    {
        var effects = new List<BCardApplication>();
        foreach (NosField basic in record.AllFields("BASIC"))
        {
            // BASIC's own first value is a repeated 0-based slot index --
            // the real payload starts one position later. An entry
            // missing any of the five is skipped rather than added with
            // a partial/zeroed field: a half-decoded effect is worse
            // than a missing one, the same principle NosDataTable itself
            // already applies to a number it cannot decode.
            int? bcardVnum = basic.Int(1);
            int? bcardSub = basic.Int(2);
            int? effectVal1 = basic.Int(3);
            int? effectVal2 = basic.Int(4);
            int? targetRaw = basic.Int(5);

            if (bcardVnum is int bv && bcardSub is int bs
                && effectVal1 is int e1 && effectVal2 is int e2 && targetRaw is int t)
            {
                effects.Add(new BCardApplication(bv, bs, e1, e2, t));
            }
        }

        return effects;
    }
}
