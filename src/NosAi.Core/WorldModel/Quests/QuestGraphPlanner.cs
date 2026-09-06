namespace NosAi.Core.WorldModel.Quests;

/// <summary>
/// Pure graph/progress logic over already-known <see cref="Quest"/> facts:
/// which quests are startable given completed prerequisites, and which
/// objective within a quest to pursue next. Pure and stateless, mirroring
/// <c>NosAi.Core.WorldModel.Exploration.ExplorationPlanner</c> and
/// <c>NosAi.Core.WorldModel.Combat.CombatPlanner</c>: no I/O, no clock
/// reads, safe to call once per decision cycle.
/// </summary>
/// <remarks>
/// <b>Scope, honestly restricted.</b> Turning OCR/UI/network evidence into
/// <see cref="Quest"/>/<see cref="QuestObjective"/> facts in the first
/// place is AP-06/A2's job, and <see cref="QuestObjective"/>'s own remarks
/// already say so -- this type only reasons over facts that already exist,
/// the same boundary <c>ExplorationPlanner</c> draws around
/// <c>MapReconstructionSource</c>. It also does not decide *how* to
/// execute a chosen objective (walk there, talk to the NPC, fight the
/// mob) -- that composes with AP-04's `--scout`/movement bridge and a
/// future AP-05 combat bridge, not something this type owns.
/// </remarks>
public static class QuestGraphPlanner
{
    /// <summary>
    /// Whether <paramref name="node"/>'s quest can be started right now:
    /// every prerequisite is known and <see cref="QuestObjectiveStatus.Completed"/>,
    /// and the quest itself is not already started/completed/failed.
    /// </summary>
    /// <remarks>
    /// A prerequisite that was never observed, or whose status is
    /// <c>Unknown</c>, makes the node **not** startable -- an unconfirmed
    /// prerequisite is never treated as satisfied by omission (the same
    /// "Unknown is not zero/false/empty" discipline
    /// <c>CombatPlanner.IsViableTarget</c>/<c>IsSkillReady</c> already
    /// apply).
    /// </remarks>
    public static bool IsStartable(QuestNode node, EquatableArray<Quest> knownQuests)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (TryFindQuest(node.Id, knownQuests) is { } ownQuest
            && ownQuest.OverallStatus.HasValue
            && ownQuest.OverallStatus.Value != QuestObjectiveStatus.NotStarted)
        {
            return false;
        }

        foreach (QuestId prerequisite in node.Prerequisites)
        {
            Quest? prerequisiteQuest = TryFindQuest(prerequisite, knownQuests);
            if (prerequisiteQuest is not { } found
                || !found.OverallStatus.HasValue
                || found.OverallStatus.Value != QuestObjectiveStatus.Completed)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Every node in <paramref name="graph"/> for which <see cref="IsStartable"/> holds, in the graph's own order.</summary>
    public static IReadOnlyList<QuestId> GetStartableQuests(QuestGraph graph, EquatableArray<Quest> knownQuests)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var startable = new List<QuestId>();
        foreach (QuestNode node in graph.Nodes)
        {
            if (IsStartable(node, knownQuests))
                startable.Add(node.Id);
        }

        return startable;
    }

    /// <summary>
    /// Whether <paramref name="objective"/> is satisfied: already marked
    /// <see cref="QuestObjectiveStatus.Completed"/>, or its known current
    /// count has reached its known required count. Unknown either count
    /// -- never assumed satisfied by omission.
    /// </summary>
    public static bool IsObjectiveSatisfied(QuestObjective objective)
    {
        ArgumentNullException.ThrowIfNull(objective);

        if (objective.Status.HasValue && objective.Status.Value == QuestObjectiveStatus.Completed)
            return true;

        return objective.CurrentCount.HasValue
            && objective.RequiredCount.HasValue
            && objective.CurrentCount.Value >= objective.RequiredCount.Value;
    }

    /// <summary>
    /// The first not-yet-satisfied objective of <paramref name="quest"/>,
    /// in its own declared order, or <see langword="null"/> when every
    /// objective is already satisfied. Objective order is treated as
    /// pursuit order, the same convention <c>NavigationPlan.Waypoints</c>/
    /// <c>ComboPlan.Steps</c> already use for their own arrays.
    /// </summary>
    public static QuestObjective? NextIncompleteObjective(Quest quest)
    {
        ArgumentNullException.ThrowIfNull(quest);

        foreach (QuestObjective objective in quest.Objectives)
        {
            if (!IsObjectiveSatisfied(objective))
                return objective;
        }

        return null;
    }

    private static Quest? TryFindQuest(QuestId id, EquatableArray<Quest> quests)
    {
        foreach (Quest quest in quests)
        {
            if (quest.Id.Equals(id))
                return quest;
        }

        return null;
    }
}
