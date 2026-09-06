namespace NosAi.Core.WorldModel.Strategy;

/// <summary>
/// The strategic context one <see cref="Goal"/> (AP-01) serves
/// (docs/ROADMAP_ESECUTIVA.md S:AP-08: "Gestire survival, recovery, quest
/// urgency, progression, farming, exploration e optimization in modo
/// contestuale"), in the DoD's own listed order. <see cref="Goal"/> itself
/// stays the bare AP-01 shape (id/description/priority); this is the typed
/// context AP-08 adds on top, mirroring
/// <c>NosAi.Core.WorldModel.Quests.QuestObjectiveKind</c>'s relationship to
/// <c>QuestObjective</c>.
/// </summary>
public enum StrategicGoalKind
{
    Survival = 0,
    Recovery = 1,
    QuestUrgency = 2,
    Progression = 3,
    Farming = 4,
    Exploration = 5,
    Optimization = 6
}

/// <summary>One <see cref="Goal"/> (AP-01, unchanged) paired with the strategic context AP-08 adds.</summary>
public sealed record EnrichedGoal(Goal Goal, StrategicGoalKind Kind);

/// <summary>
/// One goal kind's raw urgency this cycle, from whichever domain can
/// honestly compute it (survival from fused player vitals, quest urgency
/// from the AP-06 Quest Graph, exploration from the AP-04 footprint). A
/// raw proxy, not a normalized cross-domain score -- same "raw inputs
/// only" contract as <see cref="Combat.CombatSimulationResult"/> and
/// <see cref="Exploration.FrontierCandidate"/>.
/// </summary>
/// <param name="Kind">Which strategic context this signal is about.</param>
/// <param name="Urgency">Non-negative; higher means more urgent. Comparable only against other signals produced the same way this cycle.</param>
/// <param name="Reason">Named, for audit -- what this signal is actually based on.</param>
public sealed record StrategicSignal(
    StrategicGoalKind Kind,
    double Urgency,
    string Reason);

/// <summary>
/// Which <see cref="StrategicGoalKind"/> the runtime should pursue this
/// cycle (docs/ROADMAP_ESECUTIVA.md S:AP-08's "Strategic Utility" stage),
/// out of whichever <see cref="StrategicSignal"/>s were computable. Does
/// not itself decompose into an HTN/GOAP plan or authorize an act --
/// downstream HTN/GOAP/Guard/Trust/Safety stages consume this, they are
/// not defined by it, the same boundary <see cref="WorldAction"/>'s own
/// remarks already draw.
/// </summary>
/// <param name="SelectedKind">The chosen goal kind, or <see langword="null"/> when <see cref="HasSelection"/> does not hold a value or is <see langword="false"/>.</param>
/// <param name="HasSelection">Whether a selection was actually made this cycle.</param>
/// <param name="ObservedAtUtc">When this plan was produced.</param>
public sealed record StrategicPlan(
    StrategicGoalKind? SelectedKind,
    WorldFact<bool> HasSelection,
    DateTime ObservedAtUtc)
{
    /// <summary>No signal was computable this cycle, or none was given.</summary>
    public static StrategicPlan Unselected(string reason, DateTime? observedAtUtc = null)
    {
        DateTime now = observedAtUtc ?? DateTime.UtcNow;
        return new StrategicPlan(null, WorldFact<bool>.Unknown(reason, now), now);
    }
}
