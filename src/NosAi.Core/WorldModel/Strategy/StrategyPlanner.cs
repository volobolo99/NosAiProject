using NosAi.Core.WorldModel.Exploration;
using NosAi.Core.WorldModel.Quests;

namespace NosAi.Core.WorldModel.Strategy;

/// <summary>
/// Assesses which <see cref="StrategicGoalKind"/> is most urgent this cycle
/// from whichever domain facts are already known, and selects one. Pure
/// and stateless, mirroring every other phase's planner in this project
/// (<c>ExplorationPlanner</c>, <c>Combat.CombatPlanner</c>,
/// <c>QuestGraphPlanner</c>, <c>Loadout.LoadoutPlanner</c>): no I/O, no
/// clock reads, safe to call once per decision cycle.
/// </summary>
/// <remarks>
/// <b>Scope, honestly restricted.</b> Only three of the DoD's seven goal
/// kinds are assessed here, because only three currently have a real,
/// already-known signal to assess from:
/// <list type="bullet">
/// <item><see cref="StrategicGoalKind.Survival"/> -- the player's fused HP fraction (AP-02).</item>
/// <item><see cref="StrategicGoalKind.QuestUrgency"/> -- AP-06's Quest Graph (<see cref="QuestGraphPlanner"/>).</item>
/// <item><see cref="StrategicGoalKind.Exploration"/> -- AP-04's <see cref="ExplorationFootprint"/>.</item>
/// </list>
/// <see cref="StrategicGoalKind.Recovery"/> needs a real "currently in
/// combat" signal to mean anything different from Survival (not yet
/// assessed -- a threshold-only guess would double-count Survival's own
/// signal under a different name). <see cref="StrategicGoalKind.Progression"/>,
/// <see cref="StrategicGoalKind.Farming"/> and
/// <see cref="StrategicGoalKind.Optimization"/> need real economic/loot
/// value data that exists nowhere in this repository (same class of gap
/// already found for combat/equipment stats in AP-05 and AP-07). Returning
/// a fabricated urgency for any of these would be exactly the kind of
/// simulated-data-as-real this project forbids, so no method is offered
/// for them at all -- an omitted assessor, not one that quietly returns
/// zero.
/// </remarks>
public static class StrategyPlanner
{
    /// <summary>Below this HP fraction, Survival becomes the loudest signal this method knows how to raise.</summary>
    public const double CriticalHealthFraction = 0.30;

    /// <summary>
    /// Survival urgency from the player's own fused HP fraction: <see langword="null"/>
    /// when no <see cref="ResourceKind.Health"/> resource is known, or its
    /// current/maximum values are not both known -- an unknown HP fraction
    /// is never treated as "not urgent" by omission.
    /// </summary>
    public static StrategicSignal? AssessSurvivalUrgency(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (!TryGetFraction(player.Status, ResourceKind.Health, out double fraction))
            return null;

        double urgency = Math.Clamp(1.0 - fraction, 0.0, 1.0);
        return new StrategicSignal(
            StrategicGoalKind.Survival,
            urgency,
            fraction < CriticalHealthFraction ? "health_critical" : "health_fraction");
    }

    /// <summary>
    /// Quest urgency from AP-06's Quest Graph: how many quests are
    /// currently startable, plus how many known in-progress quests have at
    /// least one objective (real progress toward it). <see langword="null"/>
    /// when nothing is known about any quest yet -- not zero urgency, just
    /// nothing to report.
    /// </summary>
    public static StrategicSignal? AssessQuestUrgency(QuestGraph graph, EquatableArray<Quest> knownQuests)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (graph.Nodes.Count == 0 && knownQuests.Count == 0)
            return null;

        int startable = QuestGraphPlanner.GetStartableQuests(graph, knownQuests).Count;

        int inProgressWithNextObjective = 0;
        foreach (Quest quest in knownQuests)
        {
            if (quest.OverallStatus is { HasValue: true, Value: QuestObjectiveStatus.InProgress }
                && QuestGraphPlanner.NextIncompleteObjective(quest) is not null)
            {
                inProgressWithNextObjective++;
            }
        }

        double urgency = startable + inProgressWithNextObjective;
        return new StrategicSignal(StrategicGoalKind.QuestUrgency, urgency, "startable_and_in_progress_quests");
    }

    /// <summary>
    /// Exploration urgency from AP-04's <see cref="ExplorationFootprint"/>:
    /// a fixed baseline while the map is not confirmed fully explored, zero
    /// once it is. <see langword="null"/> when <see cref="ExplorationFootprint.FullyExplored"/>
    /// has no value yet -- nothing to report, not "not urgent."
    /// </summary>
    public static StrategicSignal? AssessExplorationUrgency(ExplorationFootprint footprint)
    {
        ArgumentNullException.ThrowIfNull(footprint);

        if (!footprint.FullyExplored.HasValue)
            return null;

        double urgency = footprint.FullyExplored.Value ? 0.0 : 1.0;
        return new StrategicSignal(
            StrategicGoalKind.Exploration,
            urgency,
            footprint.FullyExplored.Value ? "map_fully_explored" : "map_not_fully_explored");
    }

    /// <summary>
    /// The highest-urgency signal, or <see cref="StrategicPlan.Unselected"/>
    /// when none were given. Ties keep whichever signal appears first in
    /// <paramref name="signals"/>, so two calls given the same inputs in the
    /// same order always choose the same goal kind.
    /// </summary>
    public static StrategicPlan SelectStrategicPlan(IReadOnlyList<StrategicSignal> signals, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(signals);

        if (signals.Count == 0)
            return StrategicPlan.Unselected("no_strategic_signal_available", nowUtc);

        StrategicSignal best = signals[0];
        for (int i = 1; i < signals.Count; i++)
        {
            if (signals[i].Urgency > best.Urgency)
                best = signals[i];
        }

        return new StrategicPlan(best.Kind, WorldFact<bool>.Derived(true, confidence: 1d, nowUtc), nowUtc);
    }

    private static bool TryGetFraction(CombatantStatus status, ResourceKind kind, out double fraction)
    {
        fraction = 0d;
        foreach (Resource resource in status.Resources)
        {
            if (resource.Kind != kind)
                continue;

            if (!resource.Current.HasValue || !resource.Maximum.HasValue || resource.Maximum.Value <= 0)
                return false;

            fraction = resource.Current.Value / resource.Maximum.Value;
            return true;
        }

        return false;
    }
}
