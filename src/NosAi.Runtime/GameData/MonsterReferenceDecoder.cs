namespace NosAi.Runtime.GameData;

/// <summary>One skill a monster may use instead of a basic attack.</summary>
public sealed record MonsterSkillReference(int Vnum, int? Chance, int? Force);

/// <summary>
/// One drop slot: an item this monster can leave behind, and how often.
/// </summary>
/// <remarks>
/// <see cref="Chance"/> is the file's own raw scale, not a percentage: per
/// the source cited on <see cref="MonsterReference"/>, it runs 0-100000,
/// where 1000 means 1% and 1 means 0.001%. Converting it here would bake an
/// assumption about the caller's preferred unit into the decoder; every
/// caller divides by 1000 for a percentage itself.
/// </remarks>
public sealed record MonsterDrop(int ItemVnum, int? Chance, int? Amount);

/// <summary>
/// The static rules one monster-table record declares: identification,
/// level/race, resistances, base stats, basic-attack profile and drop
/// table. Distinct from any live, observed monster/target state elsewhere
/// in the World Model -- this is catalog data, the same "real reference,
/// not a live fact" category <see cref="GameReferenceDatabase"/> already
/// documents for every table it imports.
/// </summary>
/// <remarks>
/// <para>
/// The tag layout below comes from a community reference for the client's
/// file formats (https://nt-research.github.io/, "NOS files / NSgtdData /
/// monster.dat"), per this project's rule to prefer a verifiable external
/// source over guessing a tuple position. That page documents monster.dat
/// as tab-separated tag lines, one tag per physical line, several tags
/// (<c>SKILL</c>, <c>BASIC</c>, <c>CARD</c>, <c>ITEM</c>) repeating as
/// separate lines under the same tag name -- the same convention already
/// used for <c>Skill.dat</c>'s own <c>BASIC</c> tag in
/// <see cref="SkillReferenceDecoder"/>, which this decoder follows for
/// consistency. This is a lead, not a ground truth: unlike
/// <see cref="SkillReference"/>, no field here has been cross-checked
/// against a real client monster.dat capture in this repository (this
/// sandbox has no real NosTale client to capture one from). It is,
/// however, independently corroborated for the drop table specifically:
/// a second, independent third-party NosTale database
/// (<c>itempicker.atlagaming.eu/monsters.json</c>, see
/// <c>docs/research/NOSTALE_COMMUNITY_KNOWLEDGE_2026-09-05.md</c>) exposes
/// each monster's drops as an array of exactly three fields named
/// <c>itemVnum</c>/<c>chance</c>/<c>amount</c> -- the same three fields,
/// same order, as this decoder's <see cref="MonsterDrop"/>, and its
/// displayed <c>chance</c> values (e.g. <c>9.0</c>, <c>0.8</c>) are
/// consistent with the raw 0-100000 scale documented here divided by
/// 1000. Two independent sources agreeing on shape is corroboration, not
/// proof: treat every value this produces as provisional until a real
/// client capture confirms at least one monster's fields directly, the
/// same standard already applied and later met for
/// <see cref="SkillReference"/>.
/// </para>
/// <para>
/// Two tags documented on the same source page are deliberately not
/// decoded here. <c>PARTNER</c> is always twenty zeros with no known
/// meaning ("No idea. All monsters have there 0", per the source) -- a
/// field with zero information content is not worth a property. <c>MODE</c>
/// is skipped because its own structure is genuinely ambiguous from the
/// documentation alone: unlike the other repeating tags, its five BCard
/// slots are followed by seven trailing configuration values described as
/// part of the same tag, which is only decodable correctly as one combined
/// line or as five repeated lines plus a sixth -- and guessing which would
/// risk exactly the kind of wrong-position decode this project's external-
/// reference rule exists to prevent. Every other tag the source documents
/// for this file is decoded below.
/// </para>
/// <para>
/// Coded values with no verified in-game meaning beyond what the source
/// names -- <see cref="RaceType"/>, <see cref="RaceSubType"/>,
/// <see cref="Element"/>, <see cref="EtcFlags"/>,
/// <see cref="BasicAttackType"/>, <see cref="WeaponInfoAttackType"/>,
/// <see cref="ArmorInfoDefenseType"/> -- are kept as raw integers rather
/// than enums for the same reason <see cref="SkillReference"/> keeps its
/// own coded fields raw: the source names what each code is claimed to
/// mean, but promoting a claim to an enum before it is cross-checked would
/// present a guess as a fact. Every property is null when its own tag (or
/// that tag's position) is absent from the record, never a fabricated
/// default. One exception: <see cref="IsSpecialNonMonsterEntity"/> derives
/// a single boolean from <see cref="RaceType"/> despite the same lack of
/// cross-check, because it is used only to narrow a decision that already
/// tolerates being wrong in the safe direction -- see its own remarks.
/// </para>
/// </remarks>
public sealed record MonsterReference(
    int Vnum,
    string NameKey,
    int? Level,
    int? RaceType,
    int? RaceSubType,
    int? HeroLevel,
    int? Element,
    int? ElementRate,
    int? FireResistance,
    int? WaterResistance,
    int? LightResistance,
    int? DarkResistance,
    int? MaxHpBonus,
    int? MaxMpBonus,
    int? XpBonus,
    int? JobXpBonus,
    int? Hostility,
    int? GroupAttack,
    int? SeekRange,
    int? MovementSpeed,
    int? RespawnTimeDeciseconds,
    int? IconId,
    int? SpawnMobOrColor,
    int? AmountOrItem,
    int? SpriteSizeBonusPercent,
    int? CellSizeBonus,
    int? EtcFlags,
    int? IsPercentileDamage,
    int? CanOnlyBeDamagedByJajamaruLastSkill,
    int? VisibleOnMinimapAsGreenDot,
    int? IsValhallaPartner,
    int? PetInfoVal1,
    int? PetInfoVal2,
    int? PetInfoVal3,
    int? PetInfoVal4,
    int? PetInfoVal5,
    int? EffectIdOnAttack,
    int? EffectIdConstantly,
    int? EffectIdOnDeath,
    int? BasicAttackType,
    int? BasicAttackRange,
    int? BasicAttackHitChance,
    int? BasicAttackCastTimeDeciseconds,
    int? BasicAttackCooldownDeciseconds,
    int? BasicAttackDashSpeed,
    int? WeaponInfoAttackType,
    int? WeaponInfoGrade,
    int? WeaponLevel,
    int? WeaponRange,
    int? WeaponDamageMin,
    int? WeaponDamageMax,
    int? WeaponHitRate,
    int? WeaponCritChance,
    int? WeaponCritDamage,
    int? ArmorInfoDefenseType,
    int? ArmorInfoGrade,
    int? ArmorLevel,
    int? ArmorMeleeDefense,
    int? ArmorRangedDefense,
    int? ArmorMagicDefense,
    int? ArmorDodge,
    IReadOnlyList<MonsterSkillReference> Skills,
    IReadOnlyList<BCardApplication> BasicEffects,
    IReadOnlyList<BCardApplication> CardEffects,
    IReadOnlyList<MonsterDrop> Drops)
{
    /// <summary>
    /// True when <see cref="RaceType"/> is <c>8</c> -- the same source cited
    /// above (nt-research.github.io, "monster.dat", the <c>RACE</c> tag)
    /// names this exact value "Special NPCs" and documents its
    /// <see cref="RaceSubType"/> values by name: fixed traps (0), energy
    /// balls (1), cannon balls (2), talkable NPCs (3), generics (4),
    /// teleporters (5), quest spawners (6), collectibles (7) -- nine
    /// distinct kinds, none of them a monster to fight, and this source's
    /// own text is the reason none of them gets a dedicated
    /// <see cref="RaceSubType"/> property here: knowing "not a monster" is
    /// the one fact this project currently needs from RaceType 8, not which
    /// of the nine it is. Null, never false, when <see cref="RaceType"/>
    /// itself is unknown -- a record this decoder could not read a RACE tag
    /// from is never asserted to be a fightable monster by omission.
    /// </summary>
    /// <remarks>
    /// Not yet cross-checked against a real client capture, the same
    /// standing caveat as every other field here. Used by
    /// <see cref="NosAi.Runtime.Autonomy.TargetEstablishment"/> to narrow its
    /// <c>catalogue.Exists("monster", vnum)</c> check: that check alone would
    /// treat a talkable NPC or a teleporter as an attackable monster merely
    /// because both live in the same <c>monster.dat</c> table. Safe to trust
    /// there even while unverified, because of that method's own asymmetry
    /// (docs/TASTI_E_BERSAGLIO.md § 6.2): this property can only ever narrow
    /// which vnums count as an established monster, never widen it, so a
    /// wrong guess here costs a refused attack on a real monster, never an
    /// attack on something it should not have targeted.
    /// </remarks>
    public bool? IsSpecialNonMonsterEntity => RaceType is int race ? race == 8 : null;
}

/// <summary>
/// Decodes a monster-table <see cref="NosRecord"/> into a
/// <see cref="MonsterReference"/>. Pure and deterministic: the same record
/// always decodes to the same result, and a tag the record does not carry
/// leaves the properties it would have fed null rather than a guessed
/// value.
/// </summary>
/// <remarks>
/// Not yet cross-checked against a real client capture. See
/// <see cref="MonsterReference"/>'s own remarks for the source of the tag
/// layout, why it remains provisional, and the two tags deliberately not
/// decoded.
/// </remarks>
public static class MonsterReferenceDecoder
{
    /// <summary>
    /// Decodes <paramref name="record"/>, or returns null when it carries
    /// no <c>VNUM</c> -- a monster this decoder cannot identify is not
    /// worth a half-built result.
    /// </summary>
    public static MonsterReference? Decode(NosRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Vnum is not int vnum)
            return null;

        NosField? name = record.Field("NAME");
        NosField? level = record.Field("LEVEL");
        NosField? race = record.Field("RACE");
        NosField? attrib = record.Field("ATTRIB");
        NosField? hpMp = record.Field("HP/MP");
        NosField? exp = record.Field("EXP");
        NosField? preatt = record.Field("PREATT");
        NosField? setting = record.Field("SETTING");
        NosField? etc = record.Field("ETC");
        NosField? petInfo = record.Field("PETINFO");
        NosField? eff = record.Field("EFF");
        NosField? zskill = record.Field("ZSKILL");
        NosField? winfo = record.Field("WINFO");
        NosField? weapon = record.Field("WEAPON");
        NosField? ainfo = record.Field("AINFO");
        NosField? armor = record.Field("ARMOR");

        return new MonsterReference(
            vnum,
            name?.Value(0) ?? string.Empty,
            Level: level?.Int(0),
            RaceType: race?.Int(0),
            RaceSubType: race?.Int(1),
            HeroLevel: race?.Int(2),
            Element: attrib?.Int(0),
            ElementRate: attrib?.Int(1),
            FireResistance: attrib?.Int(2),
            WaterResistance: attrib?.Int(3),
            LightResistance: attrib?.Int(4),
            DarkResistance: attrib?.Int(5),
            MaxHpBonus: hpMp?.Int(0),
            MaxMpBonus: hpMp?.Int(1),
            XpBonus: exp?.Int(0),
            JobXpBonus: exp?.Int(1),
            Hostility: preatt?.Int(0),
            GroupAttack: preatt?.Int(1),
            SeekRange: preatt?.Int(2),
            MovementSpeed: preatt?.Int(3),
            RespawnTimeDeciseconds: preatt?.Int(4),
            IconId: setting?.Int(0),
            SpawnMobOrColor: setting?.Int(1),
            AmountOrItem: setting?.Int(2),
            SpriteSizeBonusPercent: setting?.Int(3),
            CellSizeBonus: setting?.Int(4),
            EtcFlags: etc?.Int(0),
            IsPercentileDamage: etc?.Int(2),
            CanOnlyBeDamagedByJajamaruLastSkill: etc?.Int(3),
            VisibleOnMinimapAsGreenDot: etc?.Int(5),
            IsValhallaPartner: etc?.Int(7),
            PetInfoVal1: petInfo?.Int(0),
            PetInfoVal2: petInfo?.Int(1),
            PetInfoVal3: petInfo?.Int(2),
            PetInfoVal4: petInfo?.Int(3),
            PetInfoVal5: petInfo?.Int(4),
            EffectIdOnAttack: eff?.Int(0),
            EffectIdConstantly: eff?.Int(1),
            EffectIdOnDeath: eff?.Int(2),
            BasicAttackType: zskill?.Int(0),
            BasicAttackRange: zskill?.Int(1),
            BasicAttackHitChance: zskill?.Int(2),
            BasicAttackCastTimeDeciseconds: zskill?.Int(3),
            BasicAttackCooldownDeciseconds: zskill?.Int(4),
            BasicAttackDashSpeed: zskill?.Int(5),
            WeaponInfoAttackType: winfo?.Int(0),
            WeaponInfoGrade: winfo?.Int(2),
            WeaponLevel: weapon?.Int(0),
            WeaponRange: weapon?.Int(1),
            WeaponDamageMin: weapon?.Int(2),
            WeaponDamageMax: weapon?.Int(3),
            WeaponHitRate: weapon?.Int(4),
            WeaponCritChance: weapon?.Int(5),
            WeaponCritDamage: weapon?.Int(6),
            ArmorInfoDefenseType: ainfo?.Int(0),
            ArmorInfoGrade: ainfo?.Int(1),
            ArmorLevel: armor?.Int(0),
            ArmorMeleeDefense: armor?.Int(1),
            ArmorRangedDefense: armor?.Int(2),
            ArmorMagicDefense: armor?.Int(3),
            ArmorDodge: armor?.Int(4),
            Skills: DecodeSkills(record),
            BasicEffects: DecodeBCards(record, "BASIC"),
            CardEffects: DecodeBCards(record, "CARD"),
            Drops: DecodeDrops(record));
    }

    private static IReadOnlyList<MonsterSkillReference> DecodeSkills(NosRecord record)
    {
        var skills = new List<MonsterSkillReference>();
        foreach (NosField field in record.AllFields("SKILL"))
        {
            if (field.Int(0) is int vnum)
                skills.Add(new MonsterSkillReference(vnum, field.Int(1), field.Int(2)));
        }

        return skills;
    }

    /// <summary>
    /// Decodes a repeated 5-field BCard tag (<c>BASIC</c> or <c>CARD</c>).
    /// Unlike <see cref="SkillReferenceDecoder"/>'s own <c>BASIC</c>
    /// decode, monster.dat's tag carries no leading slot index -- per the
    /// source, the five values are <c>BCardVNUM</c>, <c>EffectVal_1</c>,
    /// <c>EffectVal_2</c>, <c>BCardSub</c>, <c>Target</c> in that order,
    /// matching Item.dat's own BUFF/BCard definition the source points to.
    /// </summary>
    private static IReadOnlyList<BCardApplication> DecodeBCards(NosRecord record, string tag)
    {
        var cards = new List<BCardApplication>();
        foreach (NosField field in record.AllFields(tag))
        {
            int? bcardVnum = field.Int(0);
            int? effectVal1 = field.Int(1);
            int? effectVal2 = field.Int(2);
            int? bcardSub = field.Int(3);
            int? target = field.Int(4);

            if (bcardVnum is int bv && effectVal1 is int e1
                && effectVal2 is int e2 && bcardSub is int bs && target is int t)
            {
                cards.Add(new BCardApplication(bv, bs, e1, e2, t));
            }
        }

        return cards;
    }

    private static IReadOnlyList<MonsterDrop> DecodeDrops(NosRecord record)
    {
        var drops = new List<MonsterDrop>();
        foreach (NosField field in record.AllFields("ITEM"))
        {
            if (field.Int(0) is int itemVnum)
                drops.Add(new MonsterDrop(itemVnum, field.Int(1), field.Int(2)));
        }

        return drops;
    }
}
