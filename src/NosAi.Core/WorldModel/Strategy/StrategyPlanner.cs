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
/// <b>Scope, honestly restricted.</b> Four of the DoD's seven goal kinds
/// are assessed here, because only four currently have a real,
/// already-known signal to assess from:
/// <list type="bullet">
/// <item><see cref="StrategicGoalKind.Survival"/> -- the player's fused HP fraction (AP-02).</item>
/// <item><see cref="StrategicGoalKind.Recovery"/> -- the same HP fraction, gated on a real "currently in combat" fact the caller supplies (see <see cref="AssessRecoveryUrgency"/>).</item>
/// <item><see cref="StrategicGoalKind.QuestUrgency"/> -- AP-06's Quest Graph (<see cref="QuestGraphPlanner"/>).</item>
/// <item><see cref="StrategicGoalKind.Exploration"/> -- AP-04's <see cref="ExplorationFootprint"/>.</item>
/// </list>
/// <see cref="StrategicGoalKind.Progression"/>, <see cref="StrategicGoalKind.Farming"/>
/// and <see cref="StrategicGoalKind.Optimization"/> need real economic/loot
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
    /// Recovery urgency: the same HP-fraction need <see cref="AssessSurvivalUrgency"/>
    /// reads, but only once <paramref name="inCombat"/> confirms the character
    /// is <i>not</i> currently fighting. <see langword="null"/> when HP is
    /// unknown, when <paramref name="inCombat"/> itself is
    /// <see cref="WorldFact{T}.HasValue"/> <see langword="false"/> (nobody has
    /// established whether the character is in combat yet -- treated as "not
    /// yet assessed", never as "assume safe"), or while
    /// <paramref name="inCombat"/> is true.
    /// </summary>
    /// <remarks>
    /// This is the whole reason <see cref="StrategicGoalKind.Recovery"/> was
    /// left unassessed in this class's own remarks until now: a
    /// threshold-only reading of HP fraction is indistinguishable from
    /// <see cref="AssessSurvivalUrgency"/>'s own signal under a different
    /// name, which would double-count the same fact as two goals. Gating on a
    /// real, caller-supplied "currently in combat" fact is what keeps the two
    /// apart -- Survival stays unconditional (it must fire even mid-fight, the
    /// regime it exists for), Recovery only ever competes for attention once
    /// the fight is confirmed over. How "in combat" is derived is deliberately
    /// not this method's concern: it takes whatever <see cref="WorldFact{T}"/>
    /// the caller observed it with (network combat-recency, a memory-side
    /// heuristic, or anything else this project later derives it from),
    /// exactly as <see cref="AssessQuestUrgency"/> takes a <see cref="QuestGraph"/>
    /// built by whichever projector fed it.
    /// </remarks>
    public static StrategicSignal? AssessRecoveryUrgency(Player player, WorldFact<bool> inCombat)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (!inCombat.HasValue || inCombat.Value)
            return null;
        if (!TryGetFraction(player.Status, ResourceKind.Health, out double fraction))
            return null;

        double urgency = Math.Clamp(1.0 - fraction, 0.0, 1.0);
        return new StrategicSignal(
            StrategicGoalKind.Recovery,
            urgency,
            urgency > 0.0 ? "safe_to_recover" : "health_full");
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

        // Zero urgency is ambiguous on its own: a finished quest line and a stuck one both
        // count nothing startable. The verdict carries which of the two it is, so a blocked
        // chain asks to be unblocked instead of reading as "nothing left to do".
        string reason = QuestGraphPlanner.ExplainProgress(graph, knownQuests) switch
        {
            QuestProgressVerdict.Blocked => "quest_chain_blocked",
            QuestProgressVerdict.Unknown => "quest_status_never_read",
            QuestProgressVerdict.AllCompleted => "all_quests_completed",
            _ => "startable_and_in_progress_quests",
        };

        return new StrategicSignal(StrategicGoalKind.QuestUrgency, urgency, reason);
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
    /// Exploration urgency judged against the map itself rather than the footprint's own
    /// <see cref="ExplorationFootprint.FullyExplored"/> flag, which can be Unknown while the
    /// tiles already say everything needed.
    /// </summary>
    /// <remarks>
    /// The flag answers "did anyone conclude this map is done"; the tiles answer "is there
    /// anywhere left to walk". The second is observable now, so an unset flag no longer costs
    /// the whole signal. A map whose remaining tiles are all blocked scores zero urgency, but
    /// says so with its own reason: it was never covered, it simply cannot be.
    /// </remarks>
    /// <param name="map">The map as currently modelled.</param>
    /// <param name="footprint">Which tiles of that map have been visited.</param>
    /// <returns>The exploration signal, or <see langword="null"/> when no tile has been observed at all.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="map"/> or <paramref name="footprint"/> is null.</exception>
    /// <exception cref="ArgumentException">The footprint belongs to a different map.</exception>
    public static StrategicSignal? AssessExplorationUrgency(MapModel map, ExplorationFootprint footprint)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(footprint);

        ExplorationVerdict verdict = ExplorationPlanner.ExplainFrontier(map, footprint);

        // Nothing observed is nothing to report, not "no urgency": an unseen map is exactly
        // the one that might need exploring most.
        if (verdict == ExplorationVerdict.MapUnknown)
        {
            return null;
        }

        (double urgency, string reason) = verdict switch
        {
            ExplorationVerdict.FrontierAvailable => (1.0, "frontier_available"),
            ExplorationVerdict.FullyExplored => (0.0, "map_fully_explored"),
            ExplorationVerdict.NoReachableFrontier => (0.0, "remaining_tiles_unreachable"),
            _ => (0.0, "exploration_verdict_unhandled"),
        };

        return new StrategicSignal(StrategicGoalKind.Exploration, urgency, reason);
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
