namespace NosAi.Core.WorldModel.Strategy;

/// <summary>
/// Derives a "currently in combat" fact from consecutive HP readings, for
/// <see cref="StrategyPlanner.AssessRecoveryUrgency"/>'s gate.
/// </summary>
/// <remarks>
/// <para>
/// No packet on this project's own wire states a combat flag for the
/// controlled character (<c>NosAi.Runtime.Perception.Network.NosTaleWorldProtocolDecoder</c>'s
/// own remarks: <c>InCombat</c> "is not established on any packet this class
/// reads"). An HP drop between two consecutive readings is, however, a real
/// already-observed event -- nobody has to decode a flag to know a fight just
/// happened -- so this class turns that event into a decaying fact instead of
/// leaving <see cref="StrategicGoalKind.Recovery"/> permanently unassessed.
/// </para>
/// <para>
/// <b>Honest limits, stated rather than hidden.</b> This is a coarse proxy,
/// not a real combat-state read: a damage-over-time status effect or any
/// other non-attack HP loss would count as "in combat" too, and a fight
/// where every incoming hit is fully absorbed (no net HP loss) would not.
/// Neither is guessed around -- the fact is always exactly "HP dropped
/// recently", never asserted to mean anything more specific.
/// </para>
/// <para>
/// Pure and stateless like every other planner in this namespace: the
/// caller carries <see cref="State"/> across polls (one <c>for</c>-loop
/// cycle, in <c>AutoplayCommand.RunWindows</c>) and feeds back exactly what
/// <see cref="Update"/> returns, the same shape
/// <see cref="NosAi.Core.WorldModel.Reconstruction.PortalCrossingDetector"/>
/// uses for map-crossing detection.
/// </para>
/// </remarks>
public static class CombatRecencyTracker
{
    /// <summary>
    /// How long after the last observed HP drop the character is still
    /// considered "in combat". A judgment call, not a calibrated constant --
    /// long enough that a short lull between hits does not flip the fact
    /// back and forth every cycle, short enough that a character who took
    /// one hit an hour ago is not still reported as fighting.
    /// </summary>
    public static readonly TimeSpan DefaultDecayWindow = TimeSpan.FromSeconds(15);

    /// <summary>What one poll must carry into the next.</summary>
    /// <param name="PreviousHp">The HP this same tracker last saw, or null before the first poll.</param>
    /// <param name="LastDamageAtUtc">The instant of the most recent observed HP drop, or null if none has ever been seen.</param>
    public readonly record struct State(double? PreviousHp, DateTime? LastDamageAtUtc)
    {
        /// <summary>Before any poll has happened.</summary>
        public static readonly State Initial = new(null, null);
    }

    /// <summary>
    /// Folds one new HP reading into <paramref name="previous"/> and reports
    /// whether the character counts as in combat right now.
    /// </summary>
    /// <param name="previous">The state this tracker returned last poll, or <see cref="State.Initial"/> on the first one.</param>
    /// <param name="currentHp">This poll's HP reading.</param>
    /// <param name="nowUtc">This poll's instant.</param>
    /// <param name="decayWindow"><see cref="DefaultDecayWindow"/> when omitted.</param>
    /// <returns>
    /// The in-combat fact for this poll, and the state to pass into the next
    /// one. The fact is <see cref="WorldFact{T}.Unknown"/> on the very first
    /// poll: with only one reading there is nothing to compare it against, so
    /// there is genuinely nothing to report yet -- never defaulted to "not in
    /// combat".
    /// </returns>
    public static (WorldFact<bool> InCombat, State Next) Update(
        State previous, double currentHp, DateTime nowUtc, TimeSpan? decayWindow = null)
    {
        TimeSpan window = decayWindow ?? DefaultDecayWindow;

        DateTime? lastDamageAtUtc = previous.PreviousHp is { } priorHp && currentHp < priorHp
            ? nowUtc
            : previous.LastDamageAtUtc;
        var next = new State(currentHp, lastDamageAtUtc);

        if (previous.PreviousHp is null)
            return (WorldFact<bool>.Unknown("insufficient_history", nowUtc), next);

        bool inCombat = lastDamageAtUtc is { } damagedAt && nowUtc - damagedAt <= window;
        return (
            WorldFact<bool>.Derived(inCombat, confidence: 1d, nowUtc, inCombat ? "recent_hp_loss" : "no_recent_hp_loss"),
            next);
    }
}
