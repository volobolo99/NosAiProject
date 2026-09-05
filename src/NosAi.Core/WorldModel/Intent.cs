namespace NosAi.Core.WorldModel;

/// <summary>Category of a catalogued action (docs/CATALOGO_AZIONI_E_POSTCONDIZIONI.md).</summary>
public enum ActionCategory : byte
{
    Unknown = 0,
    Movement = 1,
    Combat = 2,
    Interaction = 3,
    Inventory = 4,
    Quest = 5,
    Recovery = 6,
    Idle = 7
}

/// <summary>Whether an action can be attempted now, as far as the model knows.</summary>
public enum ActionAvailability : byte
{
    Unknown = 0,
    Available = 1,
    Unavailable = 2,
    Forbidden = 3
}

/// <summary>Result of the last verified attempt of an action.</summary>
public enum ActionOutcome : byte
{
    Unknown = 0,
    Succeeded = 1,
    Failed = 2,
    Refused = 3,
    TimedOut = 4
}

/// <summary>
/// The model's knowledge about one action: availability and last verified
/// outcome. It records what happened; it never authorizes anything.
/// Authorization lives in Guard/Trust/Safety, downstream of the model.
/// </summary>
public sealed record ActionRecord(
    ActionId Id,
    ActionCategory Category,
    WorldFact<ActionAvailability> Availability,
    WorldFact<long> LastAttemptedAtUnixMillis,
    WorldFact<ActionOutcome> LastOutcome,
    WorldFact<int> ConsecutiveFailures);

/// <summary>Kind of a goal held in the model.</summary>
public enum GoalKind : byte
{
    Unknown = 0,
    Survival = 1,
    Safety = 2,
    Quest = 3,
    Exploration = 4,
    Combat = 5,
    Progression = 6,
    Economy = 7,
    Recovery = 8
}

/// <summary>Lifecycle of a goal.</summary>
public enum GoalStatus : byte
{
    Unknown = 0,
    Proposed = 1,
    Active = 2,
    Suspended = 3,
    Achieved = 4,
    Abandoned = 5
}

/// <summary>
/// A goal as the model records it. Goals are proposed by cognition and adopted
/// by the orchestrator; this record carries no execution authority.
/// </summary>
/// <param name="Priority">Higher wins ties in the orchestrator; the model does not rank.</param>
/// <param name="Progress">Fraction in [0, 1] when measurable, else UNKNOWN.</param>
public sealed record GoalRecord(
    WorldGoalId Id,
    GoalKind Kind,
    WorldFact<GoalStatus> Status,
    int Priority,
    WorldFact<float> Progress,
    WorldFact<long> DeadlineUnixMillis,
    QuestId RelatedQuest);
