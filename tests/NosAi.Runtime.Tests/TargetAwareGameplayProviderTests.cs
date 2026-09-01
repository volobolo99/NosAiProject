using System.Text;
using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The link from the composer to <c>GameplayObservation.HasTarget</c>, and the
/// wire fact the composer checks against.
/// </summary>
/// <remarks>
/// ADR-0018. Everything downstream already handles an unknown target correctly —
/// ADR-0016 makes the planner skip the rules that read it — so what these pin is
/// that the fact arrives, that it arrives UNKNOWN while the ROI is uncalibrated,
/// and that a mapped wire flag is not replaced by a derived reading.
/// </remarks>
public sealed class TargetAwareGameplayProviderTests
{
    private static readonly DateTime At = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly GameEndpoint Endpoint = new("79.110.84.175", 4002);

    private sealed class FixedGameplayProvider : IGameplayProvider
    {
        private readonly GameplayObservation _observation;
        public FixedGameplayProvider(GameplayObservation observation) => _observation = observation;
        public string Name => "fixed";
        public GameplayObservation Observe() => _observation;
    }

    private sealed class FixedTargetFrameSource : ITargetFrameSource
    {
        private readonly TargetFrameObservation _observation;
        public FixedTargetFrameSource(TargetFrameState state, DateTime at, string? reason = null)
            => _observation = new TargetFrameObservation(
                new TargetFrameReading(state, HpRatio: null, Confidence: 0.9, reason), at);
        public TargetFrameObservation Read() => _observation;
    }

    private sealed class FixedAttackObserver : IPlayerAttackObserver
    {
        public FixedAttackObserver(DateTime? at) => LastPlayerAttackAtUtc = at;
        public DateTime? LastPlayerAttackAtUtc { get; }
    }

    private static GameplayObservation Vitals(ClassifiedValue<bool>? hasTarget = null) => new(
        Hp: ClassifiedValue<int>.Live(7305, At),
        MaxHp: ClassifiedValue<int>.Live(7305, At),
        Mp: ClassifiedValue<int>.Live(1420, At),
        MaxMp: ClassifiedValue<int>.Live(1420, At),
        HasTarget: hasTarget ?? ClassifiedValue<bool>.Unknown("target_flag_not_mapped"),
        InCombat: ClassifiedValue<bool>.Unknown("combat_flag_not_mapped"),
        EntitiesInView: ClassifiedValue<int>.Live(3, At),
        ObservedAtUtc: At);

    private static TargetRoiCalibration Calibrated() =>
        TargetRoiCalibration.Confirmed(0.40, 0.06, 0.20, 0.02, 1920, 1080, At);

    // -------------------------------------------------------------- the link

    [Fact]
    public void A_present_frame_reaches_the_observation_as_a_target()
    {
        var provider = new TargetAwareGameplayProvider(
            new FixedGameplayProvider(Vitals()),
            new FixedTargetFrameSource(TargetFrameState.Present, At),
            Calibrated());

        GameplayObservation observation = provider.Observe();

        Assert.True(observation.HasTarget.HasValue);
        Assert.True(observation.HasTarget.Value);
        Assert.Equal(DataSourceKind.Derived, observation.HasTarget.Source);
    }

    /// <summary>
    /// Until the operator aims the ROI the fact stays UNKNOWN, whatever the
    /// reader happened to measure. This is the precondition ADR-0018 puts in code.
    /// </summary>
    [Fact]
    public void Without_a_calibration_the_observation_keeps_an_unknown_target()
    {
        var provider = new TargetAwareGameplayProvider(
            new FixedGameplayProvider(Vitals()),
            new FixedTargetFrameSource(TargetFrameState.Absent, At),
            TargetRoiCalibration.Uncalibrated);

        GameplayObservation observation = provider.Observe();

        Assert.False(observation.HasTarget.HasValue);
        Assert.Equal(TargetRoiCalibration.NotCalibratedReason, observation.HasTarget.FailureReason);
    }

    /// <summary>Everything the inner provider read is passed through untouched.</summary>
    [Fact]
    public void The_rest_of_the_observation_is_left_alone()
    {
        var provider = new TargetAwareGameplayProvider(
            new FixedGameplayProvider(Vitals()),
            new FixedTargetFrameSource(TargetFrameState.Present, At),
            Calibrated());

        GameplayObservation observation = provider.Observe();

        Assert.Equal(7305, observation.Hp.Value);
        Assert.Equal(1420, observation.Mp.Value);
        Assert.Equal(3, observation.EntitiesInView.Value);
        Assert.False(observation.InCombat.HasValue);
    }

    /// <summary>
    /// A protocol map that names a real target flag is a direct wire reading, and
    /// a derived one does not replace it.
    /// </summary>
    [Fact]
    public void A_mapped_wire_flag_is_not_overridden_by_the_screen()
    {
        var provider = new TargetAwareGameplayProvider(
            new FixedGameplayProvider(Vitals(ClassifiedValue<bool>.Live(true, At))),
            new FixedTargetFrameSource(TargetFrameState.Absent, At),
            Calibrated());

        GameplayObservation observation = provider.Observe();

        Assert.True(observation.HasTarget.Value);
        Assert.Equal(DataSourceKind.Live, observation.HasTarget.Source);
    }

    [Fact]
    public void A_hit_after_an_absent_frame_reaches_the_observation_as_a_disagreement()
    {
        var provider = new TargetAwareGameplayProvider(
            new FixedGameplayProvider(Vitals()),
            new FixedTargetFrameSource(TargetFrameState.Absent, At),
            Calibrated(),
            new FixedAttackObserver(At.AddMilliseconds(200)));

        GameplayObservation observation = provider.Observe();

        Assert.False(observation.HasTarget.HasValue);
        Assert.Equal(TargetStateComposer.SourcesDisagreeReason, observation.HasTarget.FailureReason);
    }

    // -------------------------------------------------------- the wire's fact

    private sealed class ScriptedSource : INetworkObservationSource
    {
        private readonly Queue<ObservedPacket> _packets = new();
        public DataSourceKind Source => DataSourceKind.Live;

        public void Send(string line, DateTime at) => _packets.Enqueue(new ObservedPacket(
            at, NetworkDirection.Inbound, Endpoint.Host, Endpoint.Port,
            Encoding.ASCII.GetBytes(line), DataSourceKind.Live));

        public bool TryObserve(out ObservedPacket packet)
        {
            if (_packets.Count == 0) { packet = null!; return false; }
            packet = _packets.Dequeue();
            return true;
        }
    }

    private static NetworkWorldFeed Feed(ScriptedSource wire) => new(new GameTrafficObserver(
        wire, new ScopedGameTrafficFilter(Endpoint), new NosTaleWorldProtocolDecoder()));

    /// <summary>
    /// The player-attacks shape of <c>su</c> — attacker type 1 — carries its
    /// capture time out as the wire's contribution.
    /// </summary>
    [Fact]
    public void A_player_attack_is_reported_with_the_time_it_crossed_the_wire()
    {
        var wire = new ScriptedSource();
        wire.Send("su 1 3443217 3 313816 226 250 12 522 0 0 0 0 698 5 0 0 310", At);
        NetworkWorldFeed feed = Feed(wire);

        NetworkObservationReport report = feed.Poll();

        Assert.Equal(At, report.PlayerAttackedAtUtc);
        Assert.Equal(At, feed.LastPlayerAttackAtUtc);
    }

    /// <summary>
    /// A monster hitting the player is not the player attacking. Reporting it
    /// would contradict the screen every time the character stood still and took
    /// damage with nothing selected.
    /// </summary>
    [Fact]
    public void A_monster_attacking_the_player_is_not_a_player_attack()
    {
        var wire = new ScriptedSource();
        wire.Send("su 3 313816 1 3443217 0 12 11 200 0 0 1 99 0 1 0 7289 7305", At);
        NetworkWorldFeed feed = Feed(wire);

        NetworkObservationReport report = feed.Poll();

        Assert.Null(report.PlayerAttackedAtUtc);
        Assert.Null(feed.LastPlayerAttackAtUtc);
    }

    /// <summary>
    /// A hit is an instant, and the batch that carried it is usually not the batch
    /// the composer asks about. Forgetting it at the next poll would make the
    /// contradiction check blind a fraction of a second after the hit.
    /// </summary>
    [Fact]
    public void The_last_attack_survives_a_poll_that_carried_none()
    {
        var wire = new ScriptedSource();
        wire.Send("su 1 3443217 3 313816 226 250 12 522 0 0 0 0 698 5 0 0 310", At);
        NetworkWorldFeed feed = Feed(wire);
        feed.Poll();

        NetworkObservationReport quiet = feed.Poll();

        Assert.Null(quiet.PlayerAttackedAtUtc);
        Assert.Equal(At, feed.LastPlayerAttackAtUtc);
    }

    /// <summary>
    /// The remembered instant never moves backwards: an out-of-order packet must
    /// not make the most recent observed hit older than one already seen.
    /// </summary>
    [Fact]
    public void An_older_attack_does_not_move_the_remembered_instant_back()
    {
        var wire = new ScriptedSource();
        wire.Send("su 1 3443217 3 313816 226 250 12 522 0 0 0 0 698 5 0 0 310", At);
        NetworkWorldFeed feed = Feed(wire);
        feed.Poll();

        wire.Send("su 1 3443217 3 313816 226 250 12 522 0 0 0 0 698 5 0 0 310", At.AddSeconds(-5));
        feed.Poll();

        Assert.Equal(At, feed.LastPlayerAttackAtUtc);
    }
}
