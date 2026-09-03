namespace NosAi.Adapter.DirectEngine;

/// <summary>
/// One thing the runtime asks the direct engine to do, with every parameter that
/// act needs and none that it does not.
/// </summary>
/// <remarks>
/// <para>
/// Built through the named factories rather than by hand, so an unset field is
/// never mistaken for a meaningful zero. That distinction has teeth here: the
/// reference packs a destination as <c>y * 65536 + x</c>, and <c>(0,0)</c> is both
/// "no destination" and a real corner of the map. A request that carries no
/// destination says so through <see cref="HasDestination"/>.
/// </para>
/// <para>
/// Entity handles are the client's own pointers, taken from the monster or ground
/// item list exactly as the reference took them: <see cref="TargetHandle"/> is that
/// value, not an identifier of ours. Zero is never a handle.
/// </para>
/// </remarks>
public readonly record struct EngineActionRequest
{
    private EngineActionRequest(
        EngineCapability capability,
        nuint targetHandle,
        int mapX,
        int mapY,
        bool hasDestination,
        short skillId,
        DateTime requestedAtUtc,
        string correlationId)
    {
        Capability = capability;
        TargetHandle = targetHandle;
        MapX = mapX;
        MapY = mapY;
        HasDestination = hasDestination;
        SkillId = skillId;
        RequestedAtUtc = requestedAtUtc;
        CorrelationId = correlationId;
    }

    /// <summary>Largest map coordinate the reference's packing can carry.</summary>
    /// <remarks>
    /// The destination travels as a single 32-bit word split into two 16-bit halves,
    /// so a coordinate outside this range does not overflow loudly: it silently lands
    /// somewhere else on the map. The reference had that defect in its loot walker,
    /// where the packed word was assigned to a <c>short int</c> and the Y half was
    /// discarded. The range is checked here so nothing downstream has to notice.
    /// </remarks>
    public const int MaxCoordinate = ushort.MaxValue;

    public EngineCapability Capability { get; }

    /// <summary>The client's own pointer to the entity or ground item, or zero when there is none.</summary>
    public nuint TargetHandle { get; }

    public int MapX { get; }

    public int MapY { get; }

    /// <summary>Whether <see cref="MapX"/> and <see cref="MapY"/> mean anything.</summary>
    public bool HasDestination { get; }

    /// <summary>The numbered skill, or 0 for a basic attack (the reference's convention).</summary>
    public short SkillId { get; }

    public DateTime RequestedAtUtc { get; }

    /// <summary>Ties this request to the decision that produced it, through to the result.</summary>
    public string CorrelationId { get; }

    /// <summary>The destination as the client wants it: <c>y * 65536 + x</c>.</summary>
    /// <exception cref="InvalidOperationException">There is no destination to pack.</exception>
    public uint PackedDestination => HasDestination
        ? ((uint)MapY << 16) | (ushort)MapX
        : throw new InvalidOperationException($"{Capability} request carries no destination.");

    public static EngineActionRequest Move(int mapX, int mapY, DateTime atUtc, string correlationId) =>
        ToCell(EngineCapability.Move, mapX, mapY, atUtc, correlationId);

    public static EngineActionRequest MovePet(int mapX, int mapY, DateTime atUtc, string correlationId) =>
        ToCell(EngineCapability.MovePet, mapX, mapY, atUtc, correlationId);

    public static EngineActionRequest MovePartner(int mapX, int mapY, DateTime atUtc, string correlationId) =>
        ToCell(EngineCapability.MovePartner, mapX, mapY, atUtc, correlationId);

    public static EngineActionRequest Attack(nuint targetHandle, short skillId, DateTime atUtc, string correlationId) =>
        new(EngineCapability.Attack, targetHandle, 0, 0, false, skillId, atUtc, Require(correlationId));

    public static EngineActionRequest AttackRun(nuint targetHandle, DateTime atUtc, string correlationId) =>
        new(EngineCapability.AttackRun, targetHandle, 0, 0, false, 0, atUtc, Require(correlationId));

    public static EngineActionRequest AttackWithPet(nuint targetHandle, DateTime atUtc, string correlationId) =>
        new(EngineCapability.AttackWithPet, targetHandle, 0, 0, false, 0, atUtc, Require(correlationId));

    public static EngineActionRequest AttackWithPartner(nuint targetHandle, DateTime atUtc, string correlationId) =>
        new(EngineCapability.AttackWithPartner, targetHandle, 0, 0, false, 0, atUtc, Require(correlationId));

    public static EngineActionRequest Collect(nuint itemHandle, DateTime atUtc, string correlationId) =>
        new(EngineCapability.Collect, itemHandle, 0, 0, false, 0, atUtc, Require(correlationId));

    public static EngineActionRequest Rest(DateTime atUtc, string correlationId) =>
        new(EngineCapability.Rest, 0, 0, 0, false, 0, atUtc, Require(correlationId));

    private static EngineActionRequest ToCell(
        EngineCapability capability, int mapX, int mapY, DateTime atUtc, string correlationId) =>
        new(capability, 0, mapX, mapY, true, 0, atUtc, Require(correlationId));

    private static string Require(string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        return correlationId;
    }

    /// <summary>
    /// Whether this request is internally coherent, and why not when it is not.
    /// </summary>
    /// <remarks>
    /// Checked before authorisation and before any address is consulted: a malformed
    /// request is refused on its own terms, and never becomes a well-formed command
    /// aimed at the wrong place.
    /// </remarks>
    public bool IsWellFormed(out EngineRefusal? refusal)
    {
        if (Capability is EngineCapability.None)
        {
            refusal = new EngineRefusal(EngineRefusalCode.InvalidRequest, "capability_not_stated");
            return false;
        }

        if (HasDestination && (MapX < 0 || MapY < 0 || MapX > MaxCoordinate || MapY > MaxCoordinate))
        {
            refusal = new EngineRefusal(
                EngineRefusalCode.InvalidRequest, $"destination_out_of_range:{MapX},{MapY}");
            return false;
        }

        bool needsDestination = Capability
            is EngineCapability.Move or EngineCapability.MovePet or EngineCapability.MovePartner;
        if (needsDestination && !HasDestination)
        {
            refusal = new EngineRefusal(EngineRefusalCode.InvalidRequest, $"destination_missing:{Capability}");
            return false;
        }

        bool needsTarget = Capability
            is EngineCapability.Attack or EngineCapability.AttackRun
            or EngineCapability.AttackWithPet or EngineCapability.AttackWithPartner
            or EngineCapability.Collect;
        if (needsTarget && TargetHandle == 0)
        {
            refusal = new EngineRefusal(EngineRefusalCode.InvalidRequest, $"target_handle_missing:{Capability}");
            return false;
        }

        if (SkillId < 0)
        {
            refusal = new EngineRefusal(EngineRefusalCode.InvalidRequest, $"skill_id_negative:{SkillId}");
            return false;
        }

        refusal = null;
        return true;
    }
}
