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

public sealed record CharacterControlContext(
    bool HasFreshObservation,
    bool IsSafetyGateOpen,
    double ObservationAgeMs,
    double ConfidenceThreshold = 0.80);

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
