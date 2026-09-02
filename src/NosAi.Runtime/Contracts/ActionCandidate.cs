namespace NosAi.Runtime.Contracts;

/// <summary>The kinds of action a cycle can propose.</summary>
public enum ActionType : byte
{
    None = 0,
    MoveToPosition = 1,
    TargetEntity = 2,
    UseBasicAttack = 3,
    UseSkill = 4,
    UseConsumable = 5,
    CollectGroundItem = 6,
    RestAndRecover = 7,
    EmergencyFlee = 8
}

/// <summary>
/// What an action is aimed at: an entity, a place, an inventory slot, or nothing.
/// </summary>
/// <remarks>
/// <para>
/// It used to be a string plus two integers — <c>"TARGET_MOB_01"</c> at a
/// constant <c>125, 85</c>, <c>"WAYPOINT_A"</c> at <c>130, 90</c>,
/// <c>"ITEM_POTION_HP"</c> at <c>0, 0</c>. None of those named anything the
/// runtime had observed, every caller read them its own way, and nothing checked
/// them. An effector connected to that would have acted on targets that do not
/// exist.
/// </para>
/// <para>
/// A string can hold anything. These four cases cannot: an attack carries an
/// entity id the runtime actually saw, a move carries a map point, a consumable
/// carries a slot, and <see cref="None"/> is a deliberate absence rather than an
/// empty string that might have been a mistake.
/// </para>
/// <para>
/// The hierarchy is closed by a private constructor, so the set of things an
/// action can be aimed at is exactly these four and a caller cannot add a fifth
/// that nothing knows how to execute.
/// </para>
/// </remarks>
public abstract record ActionTarget
{
    private ActionTarget()
    {
    }

    /// <summary>
    /// An entity the runtime has observed, by the id the wire gave it.
    /// </summary>
    /// <param name="At">
    /// Where it was seen, or null when its position is not known. Optional for
    /// the same reason <c>EntitySighting.HpRatio</c> is: the wire routinely
    /// reports one half of an entity without the other, and an effector that
    /// needs a point on the screen refuses rather than clicking at 0,0.
    /// </param>
    public sealed record Entity(long EntityId, MapPoint? At = null) : ActionTarget
    {
        /// <summary>
        /// The id of an entity that has not been chosen yet.
        /// </summary>
        /// <remarks>
        /// The planner knows <i>that</i> there is a target — ADR-0018 establishes
        /// the flag from the screen — and not <i>which</i>, because choosing the
        /// nearest observed sighting is F2-2. Negative so it can never collide
        /// with a real id from the wire, and never zero, which is the controlled
        /// player by the channel's convention.
        /// </remarks>
        public const long Unresolved = -1;

        /// <summary>Whether this names an entity the runtime actually observed.</summary>
        public bool IsResolved => EntityId >= 0;

        /// <summary>A target known to exist but not yet identified.</summary>
        public static Entity Unidentified { get; } = new(Unresolved);
    }

    /// <summary>A place on the map, with no entity involved.</summary>
    public sealed record Position(MapPoint At) : ActionTarget;

    /// <summary>A slot in the operator's inventory or quickbar.</summary>
    public sealed record InventorySlot(int Slot) : ActionTarget;

    /// <summary>Nothing is aimed at, and that is the intended state.</summary>
    public sealed record None : ActionTarget
    {
        public static None Instance { get; } = new();
    }
}

/// <param name="Target">
/// What this action is aimed at. Checked against <paramref name="Type"/> at
/// construction: see the remarks.
/// </param>
/// <remarks>
/// <para>
/// The pairing of action and target is validated here rather than left to each
/// consumer, because "attack nothing" and "walk to an entity" are not runtime
/// conditions to handle — they are mistakes in the code that built the
/// candidate, and the point of the typed target is that they stop being
/// possible to express.
/// </para>
/// <para>
/// It throws rather than yielding a refused candidate on purpose. A planner is
/// code, not input: a mismatch here means a rule was written wrong, and finding
/// that at the moment of construction is better than at the moment of acting.
/// </para>
/// </remarks>
public sealed record ActionCandidate
{
    public ActionCandidate(
        Guid CandidateId,
        ActionType Type,
        ActionTarget Target,
        int SkillOrItemId,
        TrustTier RequiredTrust,
        string Rationale)
    {
        ArgumentNullException.ThrowIfNull(Target);

        RequireTarget(Type, Target);

        this.CandidateId = CandidateId;
        this.Type = Type;
        this.Target = Target;
        this.SkillOrItemId = SkillOrItemId;
        this.RequiredTrust = RequiredTrust;
        this.Rationale = Rationale;
    }

    public Guid CandidateId { get; init; }
    public ActionType Type { get; init; }
    public ActionTarget Target { get; init; }
    public int SkillOrItemId { get; init; }
    public TrustTier RequiredTrust { get; init; }
    public string Rationale { get; init; }

    private static void RequireTarget(ActionType type, ActionTarget target)
    {
        bool valid = type switch
        {
            // Aimed at something the runtime saw. An attack on ActionTarget.None
            // is the candidate this type exists to make unbuildable.
            ActionType.UseBasicAttack or ActionType.TargetEntity or ActionType.UseSkill
                => target is ActionTarget.Entity,

            // Aimed at a place. An entity is not a destination: it moves, and the
            // point clicked would be where it used to be.
            ActionType.MoveToPosition or ActionType.EmergencyFlee
                => target is ActionTarget.Position,

            ActionType.UseConsumable => target is ActionTarget.InventorySlot,

            // Ground items are picked up where they lie; resting is aimed at
            // nobody. Neither has an effector yet, and the shape is fixed now so
            // that whoever writes one does not have to guess.
            ActionType.CollectGroundItem => target is ActionTarget.Position or ActionTarget.Entity,
            ActionType.RestAndRecover or ActionType.None => target is ActionTarget.None,

            _ => false,
        };

        if (!valid)
        {
            throw new ArgumentException(
                $"An action of type {type} cannot be aimed at {target.GetType().Name}.",
                nameof(target));
        }
    }
}
