namespace NosAi.Core.WorldModel;

/// <summary>
/// Progress state of a <see cref="Quest"/> or one of its <see cref="QuestObjective"/>s.
/// Has no "Unknown" member by design (see <see cref="TileTraversability"/>'s
/// remarks): a quest whose status has not been observed is expressed by
/// wrapping this enum in <see cref="WorldFact{T}.Unknown"/> instead.
/// </summary>
public enum QuestObjectiveStatus
{
    NotStarted = 0,
    InProgress = 1,
    Completed = 2,
    Failed = 3
}

/// <summary>One measurable sub-goal of a <see cref="Quest"/> (docs/ROADMAP_ESECUTIVA.md S:AP-06 owns turning UI/OCR evidence into these; this is only the shape).</summary>
public sealed record QuestObjective(
    WorldFact<string> Description,
    WorldFact<int> CurrentCount,
    WorldFact<int> RequiredCount,
    WorldFact<QuestObjectiveStatus> Status);

/// <summary>A quest and its known objectives.</summary>
public sealed record Quest(
    QuestId Id,
    WorldFact<string> Name,
    WorldFact<QuestObjectiveStatus> OverallStatus,
    EquatableArray<QuestObjective> Objectives);
