// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Autonomy — What the runtime is trying to do, and what that names to look for
// ============================================================================

using System.Globalization;

namespace NosAi.Runtime.Autonomy;

/// <summary>
/// A reason to go looking for something, and what it names.
/// </summary>
/// <remarks>
/// <para>
/// The thing the planner has never had. Its exploration rule walked toward the
/// constant <c>(130, 90)</c> — a point nobody observed, carried over from a
/// string called <c>"WAYPOINT_A"</c> — and its attack rules fired on the mere
/// presence of a target. Neither answered <i>why</i>, and "why" is what separates
/// a runtime that plays from one that moves.
/// </para>
/// <para>
/// <b>A goal names what to look for.</b> That is the whole requirement: a goal
/// carrying no vnum names nothing, justifies no attack, and is refused at
/// construction rather than accepted as an empty licence.
/// </para>
/// </remarks>
/// <param name="Id">A short name for the goal, for logs and refusals.</param>
/// <param name="SeekVnums">
/// The game's own numbers for what this goal is looking for. Vnums rather than
/// names or categories, because the vnum is what the wire carries on <c>in</c>
/// and what <c>GameReferenceDatabase</c> answers about; a category would need a
/// classification the wire cannot support — its type 3 is monster and NPC
/// together (docs/TASTI_E_BERSAGLIO.md § 5.2).
/// </param>
/// <param name="SearchAt">
/// Where the goal says to look, or null when it names no place. Null is not the
/// map origin and is not a waypoint: with no place named, the exploration rule
/// has nowhere to go and plans nothing.
/// </param>
/// <param name="Rationale">The sentence an operator reads to see why.</param>
public sealed record Goal(
    string Id,
    IReadOnlyCollection<int> SeekVnums,
    MapPoint? SearchAt,
    string Rationale)
{
    /// <summary>
    /// A goal to hunt one kind of entity, optionally around a place.
    /// </summary>
    /// <exception cref="ArgumentException">The goal names nothing to look for.</exception>
    public static Goal Hunt(string id, IReadOnlyCollection<int> vnums, MapPoint? searchAt = null, string? rationale = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(vnums);
        if (vnums.Count == 0)
        {
            // An empty goal would be an active goal that names nothing, which is
            // the exact shape of "attack at random with paperwork".
            throw new ArgumentException("A goal must name at least one vnum to look for.", nameof(vnums));
        }

        return new Goal(
            id,
            vnums.Distinct().OrderBy(v => v).ToArray(),
            searchAt,
            rationale ?? string.Create(
                CultureInfo.InvariantCulture,
                $"Obiettivo {id}: cercare vnum {string.Join(", ", vnums.Distinct().OrderBy(v => v))}"));
    }

    /// <summary>Whether this goal is looking for that vnum.</summary>
    public bool Names(int vnum) => SeekVnums.Contains(vnum);
}

/// <summary>
/// The goals in force, most recent first.
/// </summary>
/// <remarks>
/// <para>
/// <b>Empty means no proactive attack.</b> Not "attack anything", not "attack
/// nothing in particular": the rules that would pick a fight are skipped, and the
/// refusal says so by name. That is the same treatment ADR-0016 gives an unknown
/// fact, applied to an absent reason.
/// </para>
/// <para>
/// A stack rather than a single goal because the runtime will interrupt one goal
/// for another — a hunt suspended to heal, resumed afterwards — and the order
/// matters. Nothing pops it on its own: a goal ends when something says it has,
/// which is measured against observations (C6-3) and not against a timer here.
/// </para>
/// </remarks>
public sealed class GoalStack
{
    /// <summary>The refusal a rule carries when nothing has been asked of the runtime.</summary>
    public const string NoActiveGoalReason = "no_active_goal";

    /// <summary>The refusal when a goal is active and does not name that entity.</summary>
    public const string NotNamedByGoalReason = "not_named_by_active_goal";

    private readonly List<Goal> _goals = new();

    /// <summary>The goals in force, most recent first.</summary>
    public IReadOnlyList<Goal> Active => _goals;

    /// <summary>The goal in force now, or null when nothing has been asked.</summary>
    public Goal? Current => _goals.Count > 0 ? _goals[0] : null;

    /// <summary>Whether anything at all is being pursued.</summary>
    public bool HasActiveGoal => _goals.Count > 0;

    /// <summary>An empty stack: nothing is being pursued, so nothing is attacked.</summary>
    public static GoalStack Empty() => new();

    /// <summary>A stack holding one goal, for a caller with a single purpose.</summary>
    public static GoalStack With(Goal goal)
    {
        var stack = new GoalStack();
        stack.Push(goal);
        return stack;
    }

    /// <summary>Puts a goal in force, above any already there.</summary>
    public void Push(Goal goal)
    {
        ArgumentNullException.ThrowIfNull(goal);
        _goals.Insert(0, goal);
    }

    /// <summary>Takes the current goal out of force, or false when there is none.</summary>
    public bool TryPop(out Goal goal)
    {
        if (_goals.Count == 0)
        {
            goal = null!;
            return false;
        }

        goal = _goals[0];
        _goals.RemoveAt(0);
        return true;
    }

    /// <summary>Removes a goal by name, wherever it sits.</summary>
    public bool Remove(string id) => _goals.RemoveAll(g => g.Id == id) > 0;

    /// <summary>
    /// Whether some goal in force is looking for that vnum.
    /// </summary>
    /// <remarks>
    /// A null vnum is not a match. An entity whose vnum nobody has read is not an
    /// entity a goal names — it is an entity nothing is known about, and the
    /// unknown does not authorise an act.
    /// </remarks>
    public bool Names(int? vnum) => vnum is { } value && _goals.Any(g => g.Names(value));

    /// <summary>
    /// Where the goals say to look, or null when none names a place.
    /// </summary>
    /// <remarks>
    /// The current goal's place wins, then the next one down. Null is the honest
    /// answer for a goal that names what to seek and not where: the runtime knows
    /// what it wants and not where to walk, and inventing a waypoint is what this
    /// type replaces.
    /// </remarks>
    public MapPoint? SearchAt
    {
        get
        {
            foreach (Goal goal in _goals)
                if (goal.SearchAt is { } at) return at;
            return null;
        }
    }
}
