namespace NosAi.Core.CharacterControl;

public enum CharacterActionKind
{
    Move,
    Stop,
    BasicAttack,
    UseSkill,
    Interact,
    Pickup,
    UseItem
}

public readonly record struct CharacterTarget(string Id, double X, double Y);

public readonly record struct CharacterAction(
    string Id,
    CharacterActionKind Kind,
    CharacterTarget? Target,
    string? FunctionId,
    int Priority,
    double Confidence);

public interface ICharacterController
{
    ValueTask<bool> ExecuteAsync(CharacterAction action, CancellationToken cancellationToken);
}

public interface ICharacterActionGuard
{
    bool IsAllowed(CharacterAction action, CharacterControlContext context);
}

/// <summary>What the caller knows about the picture the action was decided from.</summary>
/// <param name="HasFreshObservation">Whether an observation exists at all. False is not "old": it is "none".</param>
/// <param name="IsSafetyGateOpen">Whether the boundary that emits would accept an act right now.</param>
/// <param name="ObservationAgeMs">How old that observation is, in milliseconds, measured by the caller against its own clock.</param>
/// <param name="ConfidenceThreshold">The confidence an action must carry to be allowed.</param>
/// <param name="MaxObservationAgeMs">
/// How old the observation may be.
/// <para>
/// A parameter rather than a constant in the guard, because the answer belongs
/// to the observation channel and not to the guard. 500 ms is right for a
/// channel that restates the world continuously; it is wrong for one that
/// announces a change and then says nothing, where an unchanged fact grows old
/// without becoming less true. NosTale's wire is the second kind -- a monster
/// standing still is not mentioned again for as long as it stands there, which
/// <c>NosAi.Runtime.Autonomy.TargetSelectionPolicy</c> already argues for the
/// same sightings and answers with 30 seconds.
/// </para>
/// <para>
/// The default is unchanged, so a caller that says nothing gets the strict
/// bound. A caller that widens it is stating a fact about its own channel, and
/// should name the reason where it does.
/// </para>
/// </param>
public sealed record CharacterControlContext(
    bool HasFreshObservation,
    bool IsSafetyGateOpen,
    double ObservationAgeMs,
    double ConfidenceThreshold = 0.80,
    double MaxObservationAgeMs = 500);

public interface ICharacterActionPlanner
{
    CharacterAction? Select(CharacterWorldSnapshot snapshot);
}

public sealed record CharacterWorldSnapshot(
    string CharacterId,
    double X,
    double Y,
    int Hp,
    int MaxHp,
    int Mp,
    int MaxMp,
    bool InCombat,
    string? TargetId,
    double TargetDistance,
    DateTimeOffset ObservedAt,
    IReadOnlyDictionary<string, double> Stats,
    IReadOnlyDictionary<string, int> CooldownsMs);
