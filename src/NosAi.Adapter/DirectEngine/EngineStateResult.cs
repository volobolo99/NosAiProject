namespace NosAi.Adapter.DirectEngine;

/// <summary>
/// What the client's own structures say about the character, at one instant.
/// </summary>
/// <remarks>
/// <para>
/// Every field is nullable and null means <i>unknown</i>, never zero. That is the
/// architecture invariant this whole surface exists under: a wrong offset does not
/// fail, it returns a plausible number, so a reading that could not be validated
/// has to be absent rather than confident. Zero hit points is a character that is
/// dead; no hit points is a read that did not happen.
/// </para>
/// <para>
/// The fields are the ones the reference actually walked to — position, vitals and
/// the attack range it derived every decision from — so a profile that resolves
/// covers what the legacy bot covered.
/// </para>
/// </remarks>
public readonly record struct EngineStateSnapshot(
    int? MapX,
    int? MapY,
    int? Hp,
    int? MaxHp,
    int? Mp,
    int? MaxMp,
    int? AttackRange,
    int? VisibleMonsters,
    int? VisibleGroundItems,
    DateTime ObservedAtUtc)
{
    /// <summary>Whether this snapshot says anything at all.</summary>
    public bool IsEmpty =>
        MapX is null && MapY is null && Hp is null && MaxHp is null && Mp is null && MaxMp is null
        && AttackRange is null && VisibleMonsters is null && VisibleGroundItems is null;

    /// <summary>Whether the character's cell is known. Both halves or neither.</summary>
    public bool HasPosition => MapX is not null && MapY is not null;
}

/// <summary>A state read, or the reason there is none.</summary>
/// <param name="Snapshot">Populated exactly when <paramref name="Refusal"/> is null.</param>
/// <param name="Refusal">Non-null exactly when the read did not happen.</param>
public sealed record EngineStateResult(EngineStateSnapshot Snapshot, EngineRefusal? Refusal)
{
    public bool Ok => Refusal is null;

    public static EngineStateResult Read(EngineStateSnapshot snapshot) => new(snapshot, null);

    public static EngineStateResult Refused(EngineRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return new EngineStateResult(default, refusal);
    }
}
