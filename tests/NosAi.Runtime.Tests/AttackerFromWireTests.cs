using System.Text;
using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// C1-2: the fact "I was hit by <i>whom</i>, and when" reaches the world state.
/// </summary>
/// <remarks>
/// <para>
/// <c>DecodeHit</c> used to compute the attacker's id and throw it away, emitting
/// an event that named only the target. The reactive rule (C6-1) cannot exist on
/// that: it needs the aggressor and the instant of the hit, so that an old
/// aggression can stop being a reason.
/// </para>
/// <para>
/// The gate is the own entity id from <c>cond</c>. Target type 1 alone says "a
/// player was hit"; naming an aggressor on that would let a stranger's fight next
/// to the character nominate somebody for it to attack. That is ADR-0018's
/// asymmetry resolved the other way: the contradiction check may run on the type
/// because its false positive is UNKNOWN, the aggressor may not because its false
/// positive would be an act. The three cases the definition of done names are the
/// first three tests.
/// </para>
/// </remarks>
public sealed class AttackerFromWireTests
{
    private static readonly DateTime At = new(2026, 9, 1, 17, 55, 0, DateTimeKind.Utc);
    private static readonly GameEndpoint Endpoint = new("79.110.84.175", 4002);

    private const long OwnId = 3443217;
    private const long Monster = 313816;
    private const long OtherMonster = 313803;
    private const long Stranger = 9999999;
    private const string Cond = "cond 1 3443217 0 0 11";
    private const string Stat = "stat 7288 7305 1420 1420 0 1184";

    private static ObservedPacket Packet(string line, DateTime? at = null, DataSourceKind source = DataSourceKind.Live)
        => new(at ?? At, NetworkDirection.Inbound, Endpoint.Host, Endpoint.Port, Encoding.ASCII.GetBytes(line), source);

    /// <summary>The monster-attacks shape of the catalogue, verbatim from the capture.</summary>
    private static string MonsterHitsPlayer(long attacker, long target = OwnId)
        => $"su 3 {attacker} 1 {target} 0 12 11 200 0 0 1 99 0 1 0 7289 7305";

    /// <summary>The player-attacks shape, verbatim from the capture.</summary>
    private static string PlayerHitsMonster(long attacker, long target)
        => $"su 1 {attacker} 3 {target} 226 250 12 522 0 0 0 0 698 5 0 0 310";

    /// <summary>A self-buff as the capture recorded it: attacker and target are both the own id.</summary>
    private const string SelfCast = "su 1 3443217 1 3443217 226 250 12 522 0 0 1 99 0 -2 0 7245 7305";

    // ------------------------------------------------- the three cases

    [Fact]
    public void Own_id_known_and_the_target_is_me_names_the_aggressor()
    {
        var decoder = new NosTaleWorldProtocolDecoder();
        decoder.Decode(Packet(Cond));

        DecodedObservations decoded = decoder.Decode(Packet(MonsterHitsPlayer(Monster)));

        PlayerHit hit = Assert.IsType<PlayerHit>(decoded.PlayerHit);
        Assert.Equal(Monster, hit.By.EntityId);
        Assert.Equal(3, hit.By.EntityType);
        // The fact carries its own instant: the packet's, not a clock of the decoder's.
        Assert.Equal(At, hit.ObservedAtUtc);
        Assert.Equal(DataSourceKind.Live, hit.Source);

        // The event the seven-opcode decoder always emitted is still emitted, unchanged
        // in meaning, and now knows when it happened.
        GameEvent combat = Assert.Single(decoded.Events);
        Assert.Equal(GameEventKind.CombatHit, combat.Kind);
        Assert.Equal(OwnId, combat.EntityId);
        Assert.Equal(At, combat.ObservedAtUtc);
    }

    /// <summary>
    /// A player was hit, and it was not this one. The id decides, and a stranger's
    /// fight next to the character nominates nobody.
    /// </summary>
    [Fact]
    public void Own_id_known_and_the_target_is_another_player_names_nobody()
    {
        var decoder = new NosTaleWorldProtocolDecoder();
        decoder.Decode(Packet(Cond));

        DecodedObservations decoded = decoder.Decode(Packet(MonsterHitsPlayer(Monster, target: Stranger)));

        Assert.Null(decoded.PlayerHit);
        Assert.False(decoded.IsEmpty);
        Assert.Equal(Stranger, Assert.Single(decoded.Events).EntityId);
    }

    /// <summary>
    /// Before <c>cond</c> has named the character, a hit on a player — even on the
    /// id that will turn out to be this one — names nobody. "A player was hit" is
    /// not "I was hit", and the fact is withheld rather than guessed.
    /// </summary>
    [Fact]
    public void Own_id_not_yet_known_names_nobody_even_when_the_target_is_my_id()
    {
        DecodedObservations decoded = new NosTaleWorldProtocolDecoder()
            .Decode(Packet(MonsterHitsPlayer(Monster)));

        Assert.Null(decoded.PlayerHit);
        // Not a lost packet: the hit event is there, it just cannot be attributed.
        Assert.Single(decoded.Events);
        Assert.Null(decoded.PlayerAttackedAtUtc);
    }

    // ------------------------------------------------- the edges the captures showed

    /// <summary>
    /// The combat capture holds a <c>su</c> whose attacker and target are both the
    /// own id — a self-buff. It is the character attacking, for ADR-0018's
    /// contradiction, and it is nobody's aggression.
    /// </summary>
    [Fact]
    public void A_self_cast_is_not_an_aggression()
    {
        var decoder = new NosTaleWorldProtocolDecoder();
        decoder.Decode(Packet(Cond));

        DecodedObservations decoded = decoder.Decode(Packet(SelfCast));

        Assert.Null(decoded.PlayerHit);
        Assert.Equal(At, decoded.PlayerAttackedAtUtc);
    }

    [Fact]
    public void The_character_hitting_a_monster_is_not_an_aggression()
    {
        var decoder = new NosTaleWorldProtocolDecoder();
        decoder.Decode(Packet(Cond));

        DecodedObservations decoded = decoder.Decode(Packet(PlayerHitsMonster(OwnId, Monster)));

        Assert.Null(decoded.PlayerHit);
        Assert.Equal(At, decoded.PlayerAttackedAtUtc);
    }

    /// <summary>Another player is an aggressor like any other, and the type says which kind.</summary>
    [Fact]
    public void Another_player_hitting_me_is_an_aggressor_of_type_one()
    {
        var decoder = new NosTaleWorldProtocolDecoder();
        decoder.Decode(Packet(Cond));

        DecodedObservations decoded = decoder.Decode(
            Packet($"su 1 {Stranger} 1 {OwnId} 226 250 12 522 0 0 1 99 0 -2 0 7245 7305"));

        PlayerHit hit = Assert.IsType<PlayerHit>(decoded.PlayerHit);
        Assert.Equal(new Aggressor(Stranger, 1), hit.By);
    }

    [Theory]
    [InlineData("su 3 313816 1")]
    [InlineData("su x 313816 1 3443217 0")]
    [InlineData("su 3 313816 y 3443217 0")]
    [InlineData("su 3 313816 1 z 0")]
    public void A_malformed_hit_names_nobody_and_reports_nothing(string line)
    {
        var decoder = new NosTaleWorldProtocolDecoder();
        decoder.Decode(Packet(Cond));

        DecodedObservations decoded = decoder.Decode(Packet(line));

        Assert.True(decoded.IsEmpty);
        Assert.Null(decoded.PlayerHit);
    }

    // ------------------------------------------------- through the observer

    /// <summary>
    /// The batch that carried the hit is rarely the batch a reactive rule asks
    /// about, and an out-of-order packet must not make the last hit older.
    /// </summary>
    [Fact]
    public void The_report_carries_the_latest_hit_by_its_own_instant_not_by_batch_order()
    {
        var source = new ListSource(
            Packet(Cond),
            Packet(MonsterHitsPlayer(Monster), At.AddSeconds(2)),
            Packet(MonsterHitsPlayer(OtherMonster), At.AddSeconds(1)));
        var observer = new GameTrafficObserver(
            source, new ScopedGameTrafficFilter(Endpoint), new NosTaleWorldProtocolDecoder());

        NetworkObservationReport report = observer.ObservePending(16);

        PlayerHit last = Assert.IsType<PlayerHit>(report.LastPlayerHit);
        Assert.Equal(Monster, last.By.EntityId);
        Assert.Equal(At.AddSeconds(2), last.ObservedAtUtc);
    }

    // ------------------------------------------------- the provider

    [Fact]
    public void HitBy_is_unknown_with_a_reason_until_the_own_id_is_known()
    {
        (ScriptedSource wire, _, NetworkGameplayProvider provider) = Chain();
        wire.Send(Stat);
        wire.Send(MonsterHitsPlayer(Monster));

        GameplayObservation observation = provider.Observe();

        Assert.False(observation.HitBy.HasValue);
        Assert.Equal("player_entity_id_not_observed", observation.HitBy.FailureReason);
    }

    [Fact]
    public void HitBy_is_unknown_with_a_different_reason_when_the_id_is_known_and_nobody_hit()
    {
        (ScriptedSource wire, _, NetworkGameplayProvider provider) = Chain();
        wire.Send(Cond);
        wire.Send(Stat);

        GameplayObservation observation = provider.Observe();

        Assert.False(observation.HitBy.HasValue);
        Assert.Equal("no_hit_on_player_observed", observation.HitBy.FailureReason);
    }

    [Fact]
    public void HitBy_reaches_the_observation_with_the_hits_own_instant_and_provenance()
    {
        (ScriptedSource wire, _, NetworkGameplayProvider provider) = Chain();
        wire.Send(Cond);
        wire.Send(Stat);
        wire.Send(MonsterHitsPlayer(Monster), At);

        GameplayObservation observation = provider.Observe();

        Assert.True(observation.HitBy.HasValue);
        Assert.Equal(new Aggressor(Monster, 3), observation.HitBy.Value);
        Assert.Equal(At, observation.HitBy.ObservedAtUtc);
        Assert.Equal(DataSourceKind.Live, observation.HitBy.Source);
    }

    /// <summary>
    /// A remembered hit is republished as what it is — observed, no longer current —
    /// with the instant it really happened. It does not expire here: how long an
    /// aggression stays a reason is the reactive rule's window (C6-1), and a
    /// provider that forgot the hit would take that decision for it.
    /// </summary>
    [Fact]
    public void A_remembered_hit_is_republished_cached_with_the_same_instant_and_does_not_expire()
    {
        (ScriptedSource wire, StubClock clock, NetworkGameplayProvider provider) = Chain();
        wire.Send(Cond);
        wire.Send(Stat);
        wire.Send(MonsterHitsPlayer(Monster), At);
        provider.Observe();

        clock.Advance(TimeSpan.FromMinutes(1));
        GameplayObservation later = provider.Observe();

        Assert.True(later.HitBy.HasValue);
        Assert.Equal(new Aggressor(Monster, 3), later.HitBy.Value);
        Assert.Equal(DataSourceKind.Cached, later.HitBy.Source);
        Assert.Equal(At, later.HitBy.ObservedAtUtc);
    }

    [Fact]
    public void An_older_hit_never_moves_the_aggressor_backwards()
    {
        (ScriptedSource wire, _, NetworkGameplayProvider provider) = Chain();
        wire.Send(Cond);
        wire.Send(Stat);
        wire.Send(MonsterHitsPlayer(Monster), At.AddSeconds(5));
        provider.Observe();

        wire.Send(MonsterHitsPlayer(OtherMonster), At.AddSeconds(1));
        GameplayObservation observation = provider.Observe();

        Assert.Equal(Monster, observation.HitBy.Value.EntityId);
        Assert.Equal(At.AddSeconds(5), observation.HitBy.ObservedAtUtc);
    }

    // ------------------------------------------------- the world state

    [Fact]
    public async Task The_world_state_carries_the_aggressor_from_both_sources()
    {
        (ScriptedSource wire, _, NetworkGameplayProvider provider) = Chain();
        wire.Send(Cond);
        wire.Send(Stat);
        wire.Send(MonsterHitsPlayer(Monster), At);

        Gate3WorldState state = await new GameplayProviderWorldStateSource(provider).ReadAsync();

        ClassifiedValue<Aggressor> hitBy = Assert.IsType<ClassifiedValue<Aggressor>>(state.HitBy);
        Assert.True(hitBy.HasValue);
        Assert.Equal(Monster, hitBy.Value.EntityId);
        Assert.Equal(At, hitBy.ObservedAtUtc);
    }

    [Fact]
    public void A_simulated_aggressor_keeps_the_state_off_a_real_effector()
    {
        Gate3WorldState state = Gate3WorldState.Live(7000, 7305, 1400, false, false, At) with
        {
            HitBy = ClassifiedValue<Aggressor>.Simulated(new Aggressor(Monster, 3), At),
        };

        Assert.True(state.IsSimulated);
    }

    /// <summary>A hit ten minutes ago does not make a current HP stale.</summary>
    [Fact]
    public void An_old_hit_does_not_age_the_state()
    {
        DateTime now = At.AddMinutes(10);
        Gate3WorldState state = Gate3WorldState.Live(7000, 7305, 1400, false, false, now) with
        {
            HitBy = ClassifiedValue<Aggressor>.Live(new Aggressor(Monster, 3), At),
        };

        Assert.Equal(now, state.ObservedAtUtc);
        Assert.True(state.IsActionable(now, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void An_unobserved_state_says_why_for_the_aggressor_too()
    {
        Gate3WorldState state = Gate3WorldState.Unobserved("gameplay_provider_not_available");

        Assert.NotNull(state.HitBy);
        Assert.False(state.HitBy!.HasValue);
        Assert.Equal("gameplay_provider_not_available", state.HitBy.FailureReason);
    }

    // ------------------------------------------------- harness

    private static (ScriptedSource Wire, StubClock Clock, NetworkGameplayProvider Provider) Chain()
    {
        var clock = new StubClock();
        var wire = new ScriptedSource(() => clock.GetUtcNow().UtcDateTime);
        var feed = new NetworkWorldFeed(new GameTrafficObserver(
            wire, new ScopedGameTrafficFilter(Endpoint), new NosTaleWorldProtocolDecoder()));
        return (wire, clock, new NetworkGameplayProvider(feed, clock));
    }

    private sealed class StubClock : TimeProvider
    {
        private DateTimeOffset _now = At;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    private sealed class ScriptedSource : INetworkObservationSource
    {
        private readonly Queue<ObservedPacket> _packets = new();
        private readonly Func<DateTime> _clock;
        public ScriptedSource(Func<DateTime> clock) => _clock = clock;
        public DataSourceKind Source => DataSourceKind.Live;

        public void Send(string line, DateTime? capturedUtc = null)
            => _packets.Enqueue(Packet(line, capturedUtc ?? _clock()));

        public bool TryObserve(out ObservedPacket packet)
        {
            if (_packets.Count == 0) { packet = null!; return false; }
            packet = _packets.Dequeue();
            return true;
        }
    }

    private sealed class ListSource : INetworkObservationSource
    {
        private readonly Queue<ObservedPacket> _packets;
        public ListSource(params ObservedPacket[] packets) => _packets = new Queue<ObservedPacket>(packets);
        public DataSourceKind Source => DataSourceKind.Live;

        public bool TryObserve(out ObservedPacket packet)
        {
            if (_packets.Count == 0) { packet = null!; return false; }
            packet = _packets.Dequeue();
            return true;
        }
    }
}
