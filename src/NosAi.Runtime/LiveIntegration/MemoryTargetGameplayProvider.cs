using NosAi.Runtime.Contracts;

namespace NosAi.LiveIntegration;

/// <summary>
/// Reads whether the character has a target from the client's own memory.
/// </summary>
/// <remarks>
/// <para>
/// ADR-0021 § 1: memory establishes <c>HasTarget</c>. What makes that possible is the
/// offset the behavioural oracle found on 2 September 2026 —
/// <see cref="NosTaleClientLayout.TargetPointerOffset"/>, a pointer to the selected
/// entity's object, and zero when there is none. Non-zero is a target; zero is no
/// target; anything that cannot be read is UNKNOWN with the reason. The three states
/// ADR-0018 insisted on stay three.
/// </para>
/// <para>
/// <b>Why DERIVED and not LIVE.</b> The client does not store a boolean anywhere. What
/// is read is a pointer, and the boolean is <i>inferred</i> from it being non-zero —
/// a short inference, and a sound one, but an inference. Classifying it LIVE would claim
/// the client publishes a flag it does not publish, and the distinction between what was
/// read and what was concluded from it is one this project keeps.
/// </para>
/// <para>
/// <b>A decorator, and it does not overwrite.</b> Like
/// <see cref="TargetAwareGameplayProvider"/>, and for the same reason: an inner
/// observation that already carries a value came from a source that is not this one, and
/// this fills a gap rather than winning an argument. The screen reader stays available
/// for a runtime with no memory access — ADR-0021 § 5 keeps it as the second source.
/// </para>
/// <para>
/// <b>What it deliberately does not publish: which entity.</b> The id behind the pointer
/// is a hypothesis by analogy with the player object and has never been checked against
/// <c>ct</c>. Publishing it would be exactly the move this codebase refuses everywhere
/// else — a plausible number with no second source — so
/// <see cref="GameplayObservation.SelectedTarget"/> is left alone, and the wire's answer
/// to <i>which</i> stands or stays unknown on its own merits.
/// </para>
/// </remarks>
public sealed class MemoryTargetGameplayProvider : IGameplayProvider
{
    /// <summary>Reported when the client's memory could not be reached at all.</summary>
    public const string SessionUnavailableReason = "target_memory_session_unavailable";

    private readonly IGameplayProvider _inner;
    private readonly Func<TargetPointerReading?> _read;
    private readonly Func<string?> _failureReason;

    /// <param name="inner">The provider that reads everything else.</param>
    /// <param name="read">
    /// The target pointer as it stands now, or null when it could not be read. A
    /// delegate rather than a session so this can be exercised without a client, and so a
    /// runtime whose attach has gone away reports UNKNOWN instead of holding a dead
    /// handle.
    /// </param>
    /// <param name="failureReason">Why there is no reading, when there is none.</param>
    public MemoryTargetGameplayProvider(
        IGameplayProvider inner,
        Func<TargetPointerReading?> read,
        Func<string?>? failureReason = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _failureReason = failureReason ?? (() => null);
    }

    /// <inheritdoc />
    public string Name => $"{_inner.Name}+target-memory";

    /// <inheritdoc />
    public GameplayObservation Observe()
    {
        GameplayObservation observation = _inner.Observe();

        if (observation.HasTarget.HasValue)
            return observation;

        TargetPointerReading? reading = _read();
        if (reading is not { } target)
        {
            return observation with
            {
                HasTarget = ClassifiedValue<bool>.Unknown(_failureReason() ?? SessionUnavailableReason),
            };
        }

        return observation with
        {
            HasTarget = ClassifiedValue<bool>.Derived(target.HasTarget),
        };
    }
}
