using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;

namespace NosAi.LiveIntegration;

/// <summary>
/// Adds <c>HasTarget</c> to another provider's observation, from the screen,
/// checked against the wire.
/// </summary>
/// <remarks>
/// <para>
/// ADR-0018. The world channel can report the player's HP, MP and the entities in
/// view, and it cannot report whether the player has a target: <c>ct</c> and
/// <c>su</c> have no observed counterpart that clears one, so a flag derived from
/// the wire would go true once and stay true. The screen has the <i>no</i>, and
/// this is where the two are joined.
/// </para>
/// <para>
/// A decorator rather than a change to <see cref="NetworkGameplayProvider"/>,
/// because the two sources are genuinely separate: the network provider stays
/// about the network, and a runtime with no capture of the screen simply does not
/// wrap it and keeps the UNKNOWN it had.
/// </para>
/// <para>
/// The composition is skipped when the inner observation already has a value. A
/// <see cref="Perception.Network.ProtocolMap"/> that names a real target flag is a
/// direct wire reading and stands; the screen fills the gap rather than
/// overriding an answer. <c>NosTaleWorldProtocolDecoder</c> has no such field —
/// fields 5 and 6 of <c>stat</c> are unknown — so on the real client the screen
/// always decides.
/// </para>
/// </remarks>
public sealed class TargetAwareGameplayProvider : IGameplayProvider
{
    private readonly IGameplayProvider _inner;
    private readonly ITargetFrameSource _screen;
    private readonly TargetRoiCalibration _calibration;
    private readonly IPlayerAttackObserver? _wire;

    /// <param name="inner">The provider that reads everything else.</param>
    /// <param name="screen">Reads the target-frame region.</param>
    /// <param name="calibration">
    /// Where the target frame sits on this client. An uncalibrated one makes every
    /// observation report <c>target_roi_not_calibrated</c>, which is the point: an
    /// unaimed reader publishes a confident <i>no target</i> otherwise.
    /// </param>
    /// <param name="wire">
    /// The wire's side, used only to contradict. Null means no contradiction check
    /// — the screen stands alone, which is weaker and is never wrong in the
    /// direction that matters.
    /// </param>
    public TargetAwareGameplayProvider(
        IGameplayProvider inner,
        ITargetFrameSource screen,
        TargetRoiCalibration calibration,
        IPlayerAttackObserver? wire = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _screen = screen ?? throw new ArgumentNullException(nameof(screen));
        _calibration = calibration ?? throw new ArgumentNullException(nameof(calibration));
        _wire = wire;
    }

    /// <inheritdoc />
    public string Name => $"{_inner.Name}+target-frame";

    /// <inheritdoc />
    public GameplayObservation Observe()
    {
        GameplayObservation observation = _inner.Observe();

        // A mapped wire flag is a direct reading and is not replaced by a derived
        // one. Today nothing maps it, so this is the path that runs.
        if (observation.HasTarget.HasValue)
            return observation;

        TargetFrameObservation screen = _screen.Read();
        ClassifiedValue<bool> hasTarget = TargetStateComposer.Compose(
            _calibration, screen, _wire?.LastPlayerAttackAtUtc);

        return observation with { HasTarget = hasTarget };
    }
}

/// <summary>Reads the target-frame region of the client's HUD.</summary>
/// <remarks>
/// An interface so the composer can be exercised without a desktop, and so a
/// runtime with no capture is a runtime that does not supply one rather than one
/// that supplies a fake.
/// </remarks>
public interface ITargetFrameSource
{
    /// <summary>Reads the region once, with the time the pixels were captured.</summary>
    TargetFrameObservation Read();
}
