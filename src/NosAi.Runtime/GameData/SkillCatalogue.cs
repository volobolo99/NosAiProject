using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.GameData;

/// <summary>
/// One skill from the reference catalogue, with each field marked by whether its
/// in-game meaning has been cross-checked against the wire (AP-05/A2+A4) or is
/// still a provisional read of the client's own table.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SkillReferenceDecoder"/> reads <c>Skill.dat</c>'s tags; the positions
/// are confirmed but the in-game <b>meaning</b> of most coded values is a lead, not
/// a fact. This type is where that distinction is made structural: a confirmed
/// field is published with a real source, a provisional one is published as
/// <see cref="ClassifiedValue{T}.Unknown"/> with a named reason, so a caller cannot
/// read a provisional number as if it were a confirmed one.
/// </para>
/// </remarks>
public sealed record CataloguedSkill
{
    /// <summary>The skill's vnum, the key the wire carries (<c>su</c> field 5, <c>ct</c> field 7).</summary>
    public required int Vnum { get; init; }

    /// <summary>The client's own display name, or null when the language table has none.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// <c>TYPE[1]</c>: the skill's position on the bar. Confirmed by A2 — every
    /// observed <c>sr &lt;n&gt;</c> names a position of a skill used in that capture.
    /// </summary>
    public required ClassifiedValue<int> CastId { get; init; }

    /// <summary>
    /// <c>TYPE[2]</c>: the class that owns the skill. Confirmed by A6, on two
    /// players with disjoint repertoires — the caveat is on the value, not hidden.
    /// </summary>
    public required ClassifiedValue<int> JobClass { get; init; }

    /// <summary>
    /// <c>DATA[5]</c>: the cooldown, in tenths of a second. Confirmed by A3 on four
    /// skills with distinct values (50, 100, 250) plus the two basic attacks (6, 7).
    /// </summary>
    public required ClassifiedValue<int> CooldownTenths { get; init; }

    /// <summary>
    /// <c>TARGET[3]</c>: how many targets the skill hits — 0/1 for a single target,
    /// 3 for an area. Confirmed by A5: the one area skill produces many hits per cast.
    /// </summary>
    public required ClassifiedValue<int> AreaTargets { get; init; }

    /// <summary>
    /// <c>COST[0]</c>. Provisional: A1 could not decide whether this or
    /// <see cref="MpCost"/> is the MP cost, so neither is presented as one.
    /// </summary>
    public required ClassifiedValue<int> CpCost { get; init; }

    /// <summary>
    /// <c>DATA[8]</c>. Provisional for the same reason as <see cref="CpCost"/>.
    /// </summary>
    public required ClassifiedValue<int> MpCost { get; init; }

    /// <summary>
    /// <c>TARGET[2]</c>: attack range. Provisional: A4 could not measure it — the
    /// player's own position is never on the wire, so attacker-to-target distance
    /// has no attacker end.
    /// </summary>
    public required ClassifiedValue<int> Range { get; init; }

    /// <summary>The full raw decode, for a report that needs the uninterpreted catalogue.</summary>
    public required SkillReference Raw { get; init; }
}

/// <summary>A skill lookup: the skill, or the named reason there is none.</summary>
/// <remarks>
/// <c>Ok</c> false is never an empty skill. It is a distinct state — the vnum is
/// not in the catalogue, or the record is there but undecodable — and the reason
/// says which, the same way <see cref="GameReferenceLocator"/> distinguishes a
/// missing volume from a missing file.
/// </remarks>
public sealed record SkillCatalogueLookup(CataloguedSkill? Skill, string? FailureReason)
{
    public bool Ok => Skill is not null;

    public static SkillCatalogueLookup NotFound(string reason) => new(null, reason);
}

/// <summary>
/// Reads the reference catalogue and returns one skill with the
/// confirmed/provisional split of <see cref="CataloguedSkill"/>.
/// </summary>
/// <remarks>
/// Read-only: it never writes, migrates or imports. The catalogue itself is opened
/// by the caller through <see cref="GameReferenceLocator"/>, so the "no catalogue
/// at all" state is reported before this type is ever handed a database — a missing
/// catalogue is not an empty one, and no lookup here pretends otherwise.
/// </remarks>
public sealed class SkillCatalogue
{
    /// <summary>The vnum is not in the imported <c>skill</c> table.</summary>
    public const string SkillNotInCatalogueReason = "skill_not_in_catalogue";

    /// <summary>
    /// The record exists but <see cref="SkillReferenceDecoder"/> could not decode
    /// it. Unreachable today — a present row always carries a vnum — and kept so a
    /// future shape change cannot hand a half-built skill to a caller unnoticed.
    /// </summary>
    public const string SkillUndecodableReason = "skill_undecodable";

    /// <summary>Shared by <see cref="CataloguedSkill.CpCost"/> and <see cref="CataloguedSkill.MpCost"/>: A1 left the two undecided.</summary>
    public const string MpCostUndecidedReason = "mp_cost_undecided_between_cp_and_data8";

    /// <summary>The reason <see cref="CataloguedSkill.Range"/> carries: the player's position is not on the wire.</summary>
    public const string RangeProvisionalReason = "range_provisional_player_position_off_wire";

    /// <summary>The reason <see cref="CataloguedSkill.JobClass"/> carries for its two-player basis.</summary>
    public const string TwoPlayersOnlyWarning = "two_players_only";

    private readonly GameReferenceDatabase _database;
    private readonly string _language;

    public SkillCatalogue(GameReferenceDatabase database, string language = "IT")
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _language = language;
    }

    /// <summary>Looks up one vnum and splits its fields into confirmed and provisional.</summary>
    public SkillCatalogueLookup Lookup(int vnum) => Build(_database, vnum, _language);

    /// <summary>
    /// The pure build, separated so a test can point it at an in-memory database
    /// without touching a drive letter.
    /// </summary>
    public static SkillCatalogueLookup Build(GameReferenceDatabase database, int vnum, string language = "IT")
    {
        ArgumentNullException.ThrowIfNull(database);

        IReadOnlyList<NosField>? fields = database.Lookup("skill", vnum);
        if (fields is null)
            return SkillCatalogueLookup.NotFound(SkillNotInCatalogueReason);

        SkillReference? skill = SkillReferenceDecoder.Decode(new NosRecord(vnum, fields));
        // Decode returns null only for a record with no VNUM; this vnum came from
        // the database's own (kind, vnum) key, so the branch is unreachable today.
        // The check stays so a future shape change cannot hand a half-built skill
        // to a caller as if it were a complete one.
        if (skill is null)
            return SkillCatalogueLookup.NotFound(SkillUndecodableReason);

        var confirmed = new CataloguedSkill
        {
            Vnum = vnum,
            Name = database.DisplayName("skill", vnum, language),
            CastId = ClassifyConfirmed(skill.CastId),
            JobClass = ClassifyConfirmed(skill.JobClass, TwoPlayersOnlyWarning),
            CooldownTenths = ClassifyConfirmed(skill.CooldownRaw),
            AreaTargets = ClassifyConfirmed(skill.TargetRange),
            CpCost = ClassifiedValue<int>.Unknown(MpCostUndecidedReason),
            MpCost = ClassifiedValue<int>.Unknown(MpCostUndecidedReason),
            Range = ClassifiedValue<int>.Unknown(RangeProvisionalReason),
            Raw = skill,
        };
        return new SkillCatalogueLookup(confirmed, null);
    }

    /// <summary>A confirmed field: cross-checked against the wire, published as derived.</summary>
    private static ClassifiedValue<int> ClassifyConfirmed(int? value, string? warning = null)
        => value is int number
            ? ClassifiedValue<int>.Derived(number, warning: warning)
            : ClassifiedValue<int>.Unknown("field_absent_from_record", warning: warning);
}
