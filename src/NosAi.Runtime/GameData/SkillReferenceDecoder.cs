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
/// <remarks>
/// <see cref="Target"/> is left as the record's raw code rather than an
/// enum. A community reference (https://nt-research.github.io/, "BCard
/// reference fields") documents 0=self pre-attack, 1=bonus pre-attack,
/// 2=caster-targeted, 3=all targets post-attack, 4=enemy-related, but
/// this decoder has not cross-checked any of those five against an
/// observed in-game effect, so encoding them as a typed enum would sell
/// a lead as a fact.
/// </remarks>
public sealed record BCardApplication(
    int BCardVnum,
    int BCardSub,
    int EffectVal1,
    int EffectVal2,
    int Target);

/// <summary>
/// The static rules one skill's record declares: classification, cost,
/// per-class level requirement, targeting, timing and the effects it
/// applies. Distinct from <c>NosAi.Core.WorldModel.Skill</c>, which is a
/// player's live, observed skill state -- this is catalog data, the same
/// "real reference, not a live fact" category <see cref="GameReferenceDatabase"/>
/// already documents for every table it imports.
/// </summary>
/// <remarks>
/// <para>
/// The tag layout below -- which position inside <c>TYPE</c>/<c>COST</c>/
/// <c>LEVEL</c>/<c>TARGET</c>/<c>DATA</c>/<c>BASIC</c> holds which value --
/// comes from a community reference for the client's file formats
/// (https://nt-research.github.io/, "NOS files / NSgtdData / Skill.dat"),
/// per this project's rule to prefer a verifiable external source over
/// guessing a tuple position. That source is a lead, not a ground truth,
/// and has now been cross-checked twice against a live client's own
/// captured skill (vnum 201): once directly, and once through a second,
/// independent third-party NosTale database
/// (<c>itempicker.atlagaming.eu/skills.json</c>, see
/// <c>docs/research/NOSTALE_COMMUNITY_KNOWLEDGE_2026-09-05.md</c>) whose
/// own parse of that same skill matches this decoder's tag layout field
/// for field: <see cref="CpCost"/>, <see cref="GoldCost"/>,
/// <see cref="MpCost"/>, <see cref="CastTimeRaw"/>, <see cref="CooldownRaw"/>,
/// <see cref="Range"/>, <see cref="TargetGroup"/> and <see cref="JobLevel"/>
/// all matched exactly. The tuple <b>position</b> of those eight tags is
/// therefore confirmed; every other tag position in this record, and the
/// in-game <b>meaning</b> of every coded value (see below), remains
/// provisional until independently confirmed the same way.
/// </para>
/// <para>
/// Coded values with no verified in-game meaning -- <see cref="SkillType"/>,
/// <see cref="JobClass"/>, <see cref="AttackType"/>, <see cref="SecondaryWeapon"/>,
/// <see cref="Element"/>, <see cref="TargetType"/>, <see cref="HitType"/>,
/// <see cref="TargetGroup"/> -- are kept as raw integers rather than enums
/// for the same reason: the source names what each code is claimed to
/// mean, but this decoder has not verified any of those claims against
/// an observed skill, so promoting them to an enum would present a guess
/// as a fact. Every property is null when its own tag is absent from the
/// record, never a fabricated default.
/// </para>
/// </remarks>
public sealed record SkillReference(
    int Vnum,
    string NameKey,
    int? SkillType,
    int? CastId,
    int? JobClass,
    int? AttackType,
    int? SecondaryWeapon,
    int? Element,
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
    int? UpgradeSkill,
    int? PartnerSkillId,
    int? CastTimeRaw,
    int? CooldownRaw,
    int? MpCost,
    int? DashSpeed,
    int? RequiredItemVnum,
    int? DataRange,
    int? DataTargetRange,
    IReadOnlyList<BCardApplication> Effects);

/// <summary>
/// Decodes one skill-table <see cref="NosRecord"/> into a
/// <see cref="SkillReference"/>. Pure and deterministic: the same record
/// always decodes to the same result, and a tag the record does not
/// carry leaves the properties it would have fed null rather than a
/// guessed value.
/// </summary>
/// <remarks>
/// Its tag layout is cross-checked for eight fields (skill vnum 201, see
/// <see cref="SkillReference"/>'s own remarks); every other tag position
/// and every coded value's in-game meaning remains provisional.
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

        NosField? type = record.Field("TYPE");
        NosField? name = record.Field("NAME");
        NosField? cost = record.Field("COST");
        NosField? level = record.Field("LEVEL");
        NosField? target = record.Field("TARGET");
        NosField? data = record.Field("DATA");

        return new SkillReference(
            vnum,
            name?.Value(0) ?? string.Empty,
            SkillType: type?.Int(0),
            CastId: type?.Int(1),
            JobClass: type?.Int(2),
            AttackType: type?.Int(3),
            SecondaryWeapon: type?.Int(4),
            Element: type?.Int(5),
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
            UpgradeSkill: data?.Int(0),
            PartnerSkillId: data?.Int(1),
            CastTimeRaw: data?.Int(4),
            CooldownRaw: data?.Int(5),
            MpCost: data?.Int(8),
            DashSpeed: data?.Int(9),
            RequiredItemVnum: data?.Int(10),
            DataRange: data?.Int(11),
            DataTargetRange: data?.Int(12),
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
