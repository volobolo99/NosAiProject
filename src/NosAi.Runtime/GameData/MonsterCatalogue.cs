using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.GameData;

/// <summary>
/// One monster from the reference catalogue, with each field marked by whether its
/// in-game meaning has been cross-checked against the wire or is still a
/// provisional read of the client's own table.
/// </summary>
/// <remarks>
/// <see cref="MonsterReferenceDecoder"/> reads <c>monster.dat</c>'s tags; the tag
/// positions come from a community reference (nt-research.github.io) and most
/// remain a lead, not a fact. This type makes that distinction structural: a
/// confirmed field is published with a real source, a provisional one as
/// <see cref="ClassifiedValue{T}.Unknown"/> with a named reason, so a caller
/// cannot read a provisional number as if it were a confirmed one.
/// </remarks>
public sealed record CataloguedMonster
{
    /// <summary>The monster's vnum, the key the wire carries (<c>in</c> field 2).</summary>
    public required int Vnum { get; init; }

    /// <summary>The client's own display name, or null when the language table has none.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// The monster's level. Confirmed: <c>st</c> field 3 matched <c>entity.level</c>
    /// for the vnum <c>in</c> declared in the same capture — 26 comparisons on four
    /// recordings, 26 agree, zero disagree (2026-09-08).
    /// </summary>
    public required ClassifiedValue<int> Level { get; init; }

    /// <summary>
    /// <c>HP/MP[0]</c>: a bonus to maximum HP, not the total. Provisional: the
    /// wire's <c>st</c> field 9 is the computed total maximum (base plus bonus),
    /// so the bonus cannot be isolated from it and has no wire cross-check.
    /// </summary>
    public required ClassifiedValue<int> MaxHpBonus { get; init; }

    /// <summary>
    /// <c>HP/MP[1]</c>: a bonus to maximum MP, provisional for the same reason as
    /// <see cref="MaxHpBonus"/> — the wire's <c>st</c> field 10 is the total.
    /// </summary>
    public required ClassifiedValue<int> MaxMpBonus { get; init; }

    /// <summary>The full raw decode, for a report that needs the uninterpreted catalogue.</summary>
    public required MonsterReference Raw { get; init; }
}

/// <summary>A monster lookup: the monster, or the named reason there is none.</summary>
/// <remarks>
/// <c>Ok</c> false is never an empty monster. It is a distinct state — the vnum is
/// not in the catalogue, or the record is there but undecodable — and the reason
/// says which, the same way <see cref="SkillCatalogueLookup"/> does for skills.
/// </remarks>
public sealed record MonsterCatalogueLookup(CataloguedMonster? Monster, string? FailureReason)
{
    public bool Ok => Monster is not null;

    public static MonsterCatalogueLookup NotFound(string reason) => new(null, reason);
}

/// <summary>
/// Reads the reference catalogue and returns one monster with the
/// confirmed/provisional split of <see cref="CataloguedMonster"/>.
/// </summary>
/// <remarks>
/// Read-only: it never writes, migrates or imports. The catalogue itself is opened
/// by the caller through <see cref="GameReferenceLocator"/>, so the "no catalogue
/// at all" state is reported before this type is ever handed a database — a missing
/// catalogue is not an empty one, and no lookup here pretends otherwise. The same
/// table <c>monster.dat</c> holds monsters, NPCs, portals and pets together, so a
/// vnum being present says nothing about whether the entity is attackable; the only
/// measured discriminator is the species read on the wire, already carried in
/// <c>EntitySighting.Kind</c>, and this type deliberately builds no attackable
/// classification of its own.
/// </remarks>
public sealed class MonsterCatalogue
{
    /// <summary>The vnum is not in the imported <c>monster</c> table.</summary>
    public const string MonsterNotInCatalogueReason = "monster_not_in_catalogue";

    /// <summary>
    /// The record exists but <see cref="MonsterReferenceDecoder"/> could not decode
    /// it. Unreachable today — a present row always carries a vnum — and kept so a
    /// future shape change cannot hand a half-built monster to a caller unnoticed.
    /// </summary>
    public const string MonsterUndecodableReason = "monster_undecodable";

    /// <summary>The reason <see cref="CataloguedMonster.MaxHpBonus"/> carries: the wire's max HP is a total, not the bonus.</summary>
    public const string HpBonusProvisionalReason = "max_hp_bonus_not_isolable_from_live_total";

    /// <summary>The reason <see cref="CataloguedMonster.MaxMpBonus"/> carries, for the same reason as HP.</summary>
    public const string MpBonusProvisionalReason = "max_mp_bonus_not_isolable_from_live_total";

    private readonly GameReferenceDatabase _database;
    private readonly string _language;

    public MonsterCatalogue(GameReferenceDatabase database, string language = "IT")
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _language = language;
    }

    /// <summary>Looks up one vnum and splits its fields into confirmed and provisional.</summary>
    public MonsterCatalogueLookup Lookup(int vnum) => Build(_database, vnum, _language);

    /// <summary>
    /// The pure build, separated so a test can point it at an in-memory database
    /// without touching a drive letter.
    /// </summary>
    public static MonsterCatalogueLookup Build(GameReferenceDatabase database, int vnum, string language = "IT")
    {
        ArgumentNullException.ThrowIfNull(database);

        IReadOnlyList<NosField>? fields = database.Lookup("monster", vnum);
        if (fields is null)
            return MonsterCatalogueLookup.NotFound(MonsterNotInCatalogueReason);

        MonsterReference? monster = MonsterReferenceDecoder.Decode(new NosRecord(vnum, fields));
        // Decode returns null only for a record with no VNUM; this vnum came from
        // the database's own (kind, vnum) key, so the branch is unreachable today.
        // The check stays so a future shape change cannot hand a half-built monster
        // to a caller as if it were a complete one.
        if (monster is null)
            return MonsterCatalogueLookup.NotFound(MonsterUndecodableReason);

        var catalogued = new CataloguedMonster
        {
            Vnum = vnum,
            Name = database.DisplayName("monster", vnum, language),
            Level = ClassifyConfirmed(monster.Level),
            MaxHpBonus = ClassifiedValue<int>.Unknown(HpBonusProvisionalReason),
            MaxMpBonus = ClassifiedValue<int>.Unknown(MpBonusProvisionalReason),
            Raw = monster,
        };
        return new MonsterCatalogueLookup(catalogued, null);
    }

    /// <summary>A confirmed field: cross-checked against the wire, published as derived.</summary>
    private static ClassifiedValue<int> ClassifyConfirmed(int? value, string? warning = null)
        => value is int number
            ? ClassifiedValue<int>.Derived(number, warning: warning)
            : ClassifiedValue<int>.Unknown("field_absent_from_record", warning: warning);
}
