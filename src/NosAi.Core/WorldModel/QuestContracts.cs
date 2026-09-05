namespace NosAi.Core.WorldModel;

/// <summary>Tipo di obiettivo di missione supportato dal quest engine (spec §4.6).</summary>
public enum QuestObjectiveKind : byte
{
    Unknown = 0,
    Travel = 1,
    Dialogue = 2,
    Collect = 3,
    Kill = 4,
    Interact = 5,
    Deliver = 6,
    Conditional = 7
}

public enum QuestStatus : byte
{
    Unknown = 0,
    Available = 1,
    Active = 2,
    ReadyToTurnIn = 3,
    Completed = 4,
    Failed = 5
}

/// <summary>Ricompensa dichiarata di una missione, come letta da UI/rete.</summary>
public readonly record struct QuestReward(Fact<long> Gold, Fact<long> Experience, Fact<TemplateId> Item, Fact<int> ItemQuantity) : IFactCarrier
{
    public FactSummary Summarize()
    {
        FactSummary summary = FactSummary.Empty;
        summary.Add(Gold);
        summary.Add(Experience);
        summary.Add(Item);
        summary.Add(ItemQuantity);
        return summary;
    }

    public static QuestReward Unknown(string reason, long observedAtUnixMillis = 0) => new(
        Fact<long>.Unknown(reason, observedAtUnixMillis),
        Fact<long>.Unknown(reason, observedAtUnixMillis),
        Fact<TemplateId>.Unknown(reason, observedAtUnixMillis),
        Fact<int>.Unknown(reason, observedAtUnixMillis));
}

/// <summary>
/// Obiettivo di missione. Un obiettivo non ancorabile a osservazioni permesse
/// resta con <see cref="Kind"/> UNKNOWN: non viene mai "indovinato".
/// </summary>
/// <param name="Prerequisites">Obiettivi della stessa missione che devono essere completati prima.</param>
public sealed record QuestObjective(
    ObjectiveId Id,
    Fact<QuestObjectiveKind> Kind,
    Fact<TemplateId> TargetTemplate,
    Fact<MapId> TargetMap,
    Fact<MapPosition> TargetPosition,
    Fact<int> RequiredCount,
    Fact<int> CurrentCount,
    Fact<bool> Completed,
    ReadOnlyMemory<ObjectiveId> Prerequisites) : IFactCarrier
{
    /// <summary>Progresso ∈ [0,1] quando richiesto e corrente sono conosciuti e richiesto &gt; 0.</summary>
    public bool TryGetProgress(out float progress)
    {
        if (RequiredCount.TryGetValue(out int required) && CurrentCount.TryGetValue(out int current) && required > 0)
        {
            progress = Math.Clamp((float)current / required, 0f, 1f);
            return true;
        }

        progress = 0f;
        return false;
    }

    public FactSummary Summarize()
    {
        FactSummary summary = FactSummary.Empty;
        summary.Add(Kind);
        summary.Add(TargetTemplate);
        summary.Add(TargetMap);
        summary.Add(TargetPosition);
        summary.Add(RequiredCount);
        summary.Add(CurrentCount);
        summary.Add(Completed);
        return summary;
    }

    public static QuestObjective Unknown(ObjectiveId id, string reason, long observedAtUnixMillis = 0) => new(
        id,
        Fact<QuestObjectiveKind>.Unknown(reason, observedAtUnixMillis),
        Fact<TemplateId>.Unknown(reason, observedAtUnixMillis),
        Fact<MapId>.Unknown(reason, observedAtUnixMillis),
        Fact<MapPosition>.Unknown(reason, observedAtUnixMillis),
        Fact<int>.Unknown(reason, observedAtUnixMillis),
        Fact<int>.Unknown(reason, observedAtUnixMillis),
        Fact<bool>.Unknown(reason, observedAtUnixMillis),
        ReadOnlyMemory<ObjectiveId>.Empty);
}

/// <summary>Missione conosciuta, con i suoi obiettivi e la ricompensa dichiarata.</summary>
public sealed record QuestState(
    QuestId Id,
    Fact<string> Title,
    Fact<QuestStatus> Status,
    Fact<TemplateId> Giver,
    ReadOnlyMemory<QuestObjective> Objectives,
    QuestReward Reward) : IFactCarrier
{
    /// <summary>Vero solo se tutti gli obiettivi hanno <c>Completed</c> conosciuto e vero. Un obiettivo UNKNOWN impedisce la conclusione.</summary>
    public bool AllObjectivesObservedComplete
    {
        get
        {
            ReadOnlySpan<QuestObjective> objectives = Objectives.Span;
            if (objectives.Length == 0) return false;
            for (int i = 0; i < objectives.Length; i++)
                if (!(objectives[i].Completed.TryGetValue(out bool done) && done))
                    return false;
            return true;
        }
    }

    /// <summary>Obiettivi i cui prerequisiti risultano osservati completi e che non sono a loro volta completi.</summary>
    public int CopyReadyObjectives(Span<ObjectiveId> destination)
    {
        ReadOnlySpan<QuestObjective> objectives = Objectives.Span;
        int written = 0;
        for (int i = 0; i < objectives.Length && written < destination.Length; i++)
        {
            QuestObjective candidate = objectives[i];
            if (candidate.Completed.TryGetValue(out bool done) && done) continue;
            if (!PrerequisitesObservedComplete(objectives, candidate.Prerequisites.Span)) continue;
            destination[written++] = candidate.Id;
        }

        return written;
    }

    private static bool PrerequisitesObservedComplete(ReadOnlySpan<QuestObjective> objectives, ReadOnlySpan<ObjectiveId> prerequisites)
    {
        for (int p = 0; p < prerequisites.Length; p++)
        {
            bool satisfied = false;
            for (int i = 0; i < objectives.Length; i++)
            {
                if (objectives[i].Id != prerequisites[p]) continue;
                satisfied = objectives[i].Completed.TryGetValue(out bool done) && done;
                break;
            }

            if (!satisfied) return false;
        }

        return true;
    }

    public FactSummary Summarize()
    {
        FactSummary summary = FactSummary.Empty;
        summary.Add(Title);
        summary.Add(Status);
        summary.Add(Giver);
        summary.AddAll(Objectives.Span);
        FactSummary reward = Reward.Summarize();
        summary.Merge(in reward);
        return summary;
    }

    public static QuestState Unknown(QuestId id, string reason, long observedAtUnixMillis = 0) => new(
        id,
        Fact<string>.Unknown(reason, observedAtUnixMillis),
        Fact<QuestStatus>.Unknown(reason, observedAtUnixMillis),
        Fact<TemplateId>.Unknown(reason, observedAtUnixMillis),
        ReadOnlyMemory<QuestObjective>.Empty,
        QuestReward.Unknown(reason, observedAtUnixMillis));
}
