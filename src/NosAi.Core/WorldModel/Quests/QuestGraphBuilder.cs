namespace NosAi.Core.WorldModel.Quests;

/// <summary>
/// A prerequisite edge that points at a quest the graph does not contain.
/// </summary>
/// <param name="Quest">The quest declaring the prerequisite.</param>
/// <param name="MissingPrerequisite">The prerequisite that is not among the observed quests.</param>
public sealed record DanglingPrerequisite(QuestId Quest, QuestId MissingPrerequisite);

/// <summary>
/// Outcome of assembling observed quests into a graph, carrying both the graph and every
/// defect found while assembling it.
/// </summary>
/// <remarks>
/// Defects are reported, never silently repaired. A quest chain arrives from observation —
/// OCR, the wire, the quest window — and observation can be wrong: dropping a malformed edge
/// without saying so would turn a reading error into a confident plan.
/// </remarks>
/// <param name="Graph">The graph built from the observations, with duplicates collapsed to their first occurrence.</param>
/// <param name="DuplicateIds">Quest ids observed more than once; only the first occurrence entered the graph.</param>
/// <param name="DanglingPrerequisites">Prerequisite edges pointing outside the observed set.</param>
/// <param name="Cycles">Prerequisite cycles, each listed as the quests forming it.</param>
public sealed record QuestGraphBuildResult(
    QuestGraph Graph,
    EquatableArray<QuestId> DuplicateIds,
    EquatableArray<DanglingPrerequisite> DanglingPrerequisites,
    EquatableArray<EquatableArray<QuestId>> Cycles)
{
    /// <summary>
    /// True when the graph carries no defect, so every prerequisite resolves and every chain
    /// has a beginning. A graph that is not sound may still be planned against, but the caller
    /// has been told what is wrong with it.
    /// </summary>
    public bool IsSound =>
        DuplicateIds.Count == 0 && DanglingPrerequisites.Count == 0 && Cycles.Count == 0;
}

/// <summary>
/// Assembles observed quests into a <see cref="QuestGraph"/> and certifies that the result can
/// actually be planned against.
/// </summary>
/// <remarks>
/// <see cref="QuestGraphPlanner.GetStartableQuests"/> answers "what can I begin now" by looking
/// for quests whose prerequisites are met. On a graph whose prerequisites form a cycle that
/// question has no answer at all, and the planner returns an empty list that is indistinguishable
/// from "everything is done". This builder makes that difference explicit before planning starts.
/// </remarks>
public static class QuestGraphBuilder
{
    /// <summary>
    /// Builds a graph from observed quests, reporting duplicates, prerequisites pointing outside
    /// the observed set, and prerequisite cycles.
    /// </summary>
    /// <param name="observed">Quests as observed; may be empty, and may contain duplicates.</param>
    /// <returns>The graph together with every defect found; never null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="observed"/> is null.</exception>
    public static QuestGraphBuildResult Build(IEnumerable<QuestNode> observed)
    {
        ArgumentNullException.ThrowIfNull(observed);

        var accepted = new List<QuestNode>();
        var byId = new Dictionary<QuestId, QuestNode>();
        var duplicates = new List<QuestId>();

        foreach (QuestNode node in observed)
        {
            if (node is null)
            {
                continue;
            }

            if (!byId.TryAdd(node.Id, node))
            {
                // First occurrence wins: a later reading of the same quest is not more
                // authoritative than the earlier one, and choosing between them silently
                // would hide that the two disagreed.
                duplicates.Add(node.Id);
                continue;
            }

            accepted.Add(node);
        }

        var dangling = new List<DanglingPrerequisite>();
        foreach (QuestNode node in accepted)
        {
            foreach (QuestId prerequisite in node.Prerequisites)
            {
                if (!byId.ContainsKey(prerequisite))
                {
                    dangling.Add(new DanglingPrerequisite(node.Id, prerequisite));
                }
            }
        }

        return new QuestGraphBuildResult(
            new QuestGraph(EquatableArray<QuestNode>.From(accepted)),
            EquatableArray<QuestId>.From(duplicates),
            EquatableArray<DanglingPrerequisite>.From(dangling),
            EquatableArray<EquatableArray<QuestId>>.From(FindCycles(accepted, byId)));
    }

    /// <summary>
    /// Finds every prerequisite cycle with an iterative depth-first search.
    /// </summary>
    /// <remarks>
    /// Iterative rather than recursive on purpose: the depth is the length of a quest chain as
    /// observed, and an observation error can make that chain arbitrarily long. Recursion would
    /// turn a bad reading into a stack overflow, which is a crash instead of a diagnosis.
    /// </remarks>
    /// <param name="nodes">Accepted nodes, already de-duplicated.</param>
    /// <param name="byId">Lookup for the accepted nodes.</param>
    /// <returns>One entry per cycle, each listing the quests that form it in traversal order.</returns>
    private static List<EquatableArray<QuestId>> FindCycles(
        List<QuestNode> nodes,
        Dictionary<QuestId, QuestNode> byId)
    {
        var cycles = new List<EquatableArray<QuestId>>();
        var seenCycles = new HashSet<string>(StringComparer.Ordinal);
        var finished = new HashSet<QuestId>();

        foreach (QuestNode start in nodes)
        {
            if (finished.Contains(start.Id))
            {
                continue;
            }

            var path = new List<QuestId>();
            var onPath = new HashSet<QuestId>();
            var frontier = new Stack<(QuestId Id, int NextEdge, bool Entered)>();
            frontier.Push((start.Id, 0, false));

            while (frontier.Count > 0)
            {
                (QuestId id, int nextEdge, bool entered) = frontier.Pop();

                if (!entered)
                {
                    if (finished.Contains(id))
                    {
                        continue;
                    }

                    path.Add(id);
                    onPath.Add(id);
                }

                EquatableArray<QuestId> prerequisites = byId.TryGetValue(id, out QuestNode? node)
                    ? node.Prerequisites
                    : EquatableArray<QuestId>.Empty;

                if (nextEdge >= prerequisites.Count)
                {
                    path.RemoveAt(path.Count - 1);
                    onPath.Remove(id);
                    finished.Add(id);
                    continue;
                }

                QuestId next = prerequisites[nextEdge];
                frontier.Push((id, nextEdge + 1, true));

                if (onPath.Contains(next))
                {
                    int from = path.IndexOf(next);
                    var cycle = path.GetRange(from, path.Count - from);
                    if (seenCycles.Add(CycleKey(cycle)))
                    {
                        cycles.Add(EquatableArray<QuestId>.From(cycle));
                    }

                    continue;
                }

                if (!finished.Contains(next) && byId.ContainsKey(next))
                {
                    frontier.Push((next, 0, false));
                }
            }
        }

        return cycles;
    }

    /// <summary>
    /// Builds a rotation-independent key for a cycle, so the same loop reached from a different
    /// starting quest is reported once instead of once per member.
    /// </summary>
    /// <param name="cycle">The quests forming the cycle, in traversal order.</param>
    /// <returns>A stable key for the cycle.</returns>
    private static string CycleKey(List<QuestId> cycle)
    {
        var rendered = new List<string>(cycle.Count);
        foreach (QuestId id in cycle)
        {
            rendered.Add(id.ToString());
        }

        rendered.Sort(StringComparer.Ordinal);
        return string.Join("|", rendered);
    }
}
