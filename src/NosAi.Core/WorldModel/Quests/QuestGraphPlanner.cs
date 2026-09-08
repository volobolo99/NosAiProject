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
///
/// <para>
/// One exception, confirmed by direct investigation rather than assumed:
/// <see cref="AssessCollectProgress"/> covers the one slice of "AP-06/A2's
/// job" that does <b>not</b> need OCR. A <see cref="QuestObjectiveKind.Collect"/>
/// objective's progress is just an item count, and item counts are already
/// a real World Model fact -- <c>NosAi.Runtime.WorldModel.Fusion.GameplayObservationProjector</c>
/// (AP-01/A2, already <c>Integrated</c>) fuses the wire's own <c>ivn</c>
/// packets into <c>Player.Inventory</c> today. Every other objective kind
/// still needs the blocked OCR/UI path (to learn a quest exists at all) or
/// a still-missing execution primitive (Dialogue/Interact/Kill/Deliver) --
/// this one exception does not reopen those.
/// </para>
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

    /// <summary>
    /// The observed current count for a <see cref="QuestObjectiveKind.Collect"/>
    /// objective, read from the player's own already-fused inventory --
    /// real <c>ivn</c> wire evidence (see this type's own remarks), not a
    /// guess and not OCR.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An <b>unobserved</b> inventory returns <c>Unknown</c>, carrying the
    /// reason the fact itself gives. An <b>observed</b> one that happens to be
    /// empty returns a known zero: the channel stated every slot, and none of
    /// them holds this item.
    /// </para>
    /// <para>
    /// Those used to be the same answer. While <c>Player.Inventory</c> was a
    /// bare <see cref="EquatableArray{T}"/> the two collapsed into one empty
    /// array, and this method could only refuse both together --
    /// <c>inventory_not_observed_or_confirmed_empty</c>, a reason that named
    /// two possibilities because it could not tell them apart. It reported a
    /// genuinely empty backpack as Unknown, which is the mirror image of the
    /// defect it was avoiding. <c>docs/adr/ADR-0027</c> made the distinction
    /// observable and this paragraph is what it bought.
    /// </para>
    /// <para>
    /// A non-empty <paramref name="inventory"/> that simply does not list
    /// <paramref name="target"/>'s item is the same known-zero case: the
    /// absence of this one item really does mean zero of it.
    /// </para>
    /// <para>
    /// When the item appears in one or more slots, their
    /// <see cref="InventoryItem.Quantity"/> is summed -- a stackable item
    /// can occupy more than one slot. Any matching slot whose own quantity
    /// is itself <c>Unknown</c> makes the total <c>Unknown</c>: a partial
    /// sum would silently under-report.
    /// </para>
    /// </remarks>
    public static WorldFact<int> AssessCollectProgress(
        QuestObjectiveTarget target,
        WorldFact<EquatableArray<InventoryItem>> inventory,
        DateTime observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(inventory);
        if (target.Kind != QuestObjectiveKind.Collect)
        {
            throw new ArgumentException(
                $"{nameof(AssessCollectProgress)} only applies to {QuestObjectiveKind.Collect} objectives.",
                nameof(target));
        }

        if (!inventory.HasValue)
            return WorldFact<int>.Unknown(inventory.Reason ?? "inventory_not_observed", observedAtUtc);

        ItemId item = target.Item!.Value;
        var total = 0;
        var found = false;
        foreach (InventoryItem slot in inventory.Value)
        {
            if (!slot.Id.Equals(item))
                continue;

            found = true;
            if (!slot.Quantity.HasValue)
                return WorldFact<int>.Unknown("matching_slot_quantity_unknown", observedAtUtc);

            total += slot.Quantity.Value;
        }

        return WorldFact<int>.Live(
            total, confidence: 1d, observedAtUtc,
            reason: found ? null : "item_absent_from_observed_inventory");
    }

    /// <summary>
    /// Why <see cref="GetStartableQuests"/> returned nothing, so an empty list is never read as
    /// "there is nothing left to do".
    /// </summary>
    public static QuestProgressVerdict ExplainProgress(QuestGraph graph, EquatableArray<Quest> knownQuests)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (graph.Nodes.Count == 0)
        {
            return QuestProgressVerdict.NothingKnown;
        }

        bool anyUnknown = false;
        bool anyInProgress = false;
        bool allCompleted = true;

        foreach (QuestNode node in graph.Nodes)
        {
            if (IsStartable(node, knownQuests))
            {
                return QuestProgressVerdict.Startable;
            }

            Quest? quest = TryFindQuest(node.Id, knownQuests);
            if (quest is not { } known || !known.OverallStatus.HasValue)
            {
                anyUnknown = true;
                allCompleted = false;
                continue;
            }

            QuestObjectiveStatus status = known.OverallStatus.Value;
            if (status == QuestObjectiveStatus.InProgress)
            {
                anyInProgress = true;
            }

            if (status != QuestObjectiveStatus.Completed)
            {
                allCompleted = false;
            }
        }

        // A quest seen in progress is an observed fact, so it outranks the doubt raised by
        // another quest whose status was never read.
        if (anyInProgress)
        {
            return QuestProgressVerdict.InProgress;
        }

        // Without every status in hand, "finished" and "stuck" are both guesses.
        if (anyUnknown)
        {
            return QuestProgressVerdict.Unknown;
        }

        return allCompleted ? QuestProgressVerdict.AllCompleted : QuestProgressVerdict.Blocked;
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

/// <summary>
/// Why nothing can be started right now. An empty startable list on its own cannot tell
/// "every quest is done" from "the chain is stuck", and those two demand opposite behaviour
/// from the player: one means move on, the other means something must be unblocked.
/// </summary>
public enum QuestProgressVerdict
{
    /// <summary>The graph holds no quest at all: nothing has been observed yet.</summary>
    NothingKnown = 0,

    /// <summary>At least one quest can be started now.</summary>
    Startable = 1,

    /// <summary>Nothing to start because a quest is already under way.</summary>
    InProgress = 2,

    /// <summary>Every quest in the graph is known to be completed.</summary>
    AllCompleted = 3,

    /// <summary>Quests remain, none can be started, and none is under way: the chain is stuck.</summary>
    Blocked = 4,

    /// <summary>
    /// At least one quest status was never observed, so neither completion nor blockage can be
    /// claimed. Not a synonym for <see cref="Blocked"/>.
    /// </summary>
    Unknown = 5
}
