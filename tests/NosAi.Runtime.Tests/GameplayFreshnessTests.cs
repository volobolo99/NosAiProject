using System.Text;
using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// What the provider says between two packets, and what it refuses to say.
/// </summary>
/// <remarks>
/// <para>
/// These are the questions a replay of the real recordings raised.
/// <c>data/nostale_combat.noscap</c> carries 62 <c>stat</c> packets in 90 s and
/// <c>data/nostale_01.noscap</c> 22 in an idle session, against 7685 and 2468
/// movement packets — so a runtime polling in small bites finds vitals in barely
/// a third of its polls, and finds no entity at all in every idle poll while
/// thousands of movements go by.
/// </para>
/// <para>
/// Both gaps have a wrong answer that looks like data: republish the last HP as
/// though it were current, and report zero entities because none were mentioned.
/// The tests below pin the honest answers — CACHED with the time it was really
/// observed, and UNKNOWN with a reason — and pin the boundary where a remembered
/// reading stops being one.
/// </para>
/// </remarks>
public sealed class GameplayFreshnessTests
{
    private static readonly GameEndpoint Endpoint = new("79.110.84.175", 4002);
    private static readonly DateTime Start = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>A clock the test moves by hand.</summary>
    private sealed class StubClock : TimeProvider
    {
        private DateTimeOffset _now = Start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    /// <summary>
    /// A channel the test feeds one printable packet at a time.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="SyntheticNetworkSource"/>, which forces every
    /// packet to SIMULATED: half of what is under test here is whether a LIVE
    /// reading keeps its provenance and whether a remembered one loses it.
    /// </remarks>
    private sealed class ScriptedSource : INetworkObservationSource
    {
        private readonly Queue<ObservedPacket> _packets = new();
        private readonly Func<DateTime> _clock;
        public DataSourceKind Source { get; }

        /// <param name="clock">
        /// Stamps each packet as it is sent, the way a wire does. A fixed stamp
        /// would make every packet look equally old however far the test's clock
        /// had moved, which is the one thing these tests are about.
        /// </param>
        public ScriptedSource(DataSourceKind source, Func<DateTime> clock)
        {
            Source = source;
            _clock = clock;
        }

        /// <param name="capturedUtc">When the packet crossed the wire; now by default.</param>
        public void Send(string line, DateTime? capturedUtc = null) => _packets.Enqueue(new ObservedPacket(
            capturedUtc ?? _clock(), NetworkDirection.Inbound, Endpoint.Host, Endpoint.Port,
            Encoding.ASCII.GetBytes(line), Source));

        public bool TryObserve(out ObservedPacket packet)
        {
            if (_packets.Count == 0) { packet = null!; return false; }
            packet = _packets.Dequeue();
            return true;
        }
    }

    private static (ScriptedSource Wire, StubClock Clock, NetworkGameplayProvider Provider) Chain(
        TimeSpan? maxVitalsAge = null, DataSourceKind source = DataSourceKind.Live)
    {
        var clock = new StubClock();
        var wire = new ScriptedSource(source, () => clock.GetUtcNow().UtcDateTime);
        var feed = new NetworkWorldFeed(new GameTrafficObserver(
            wire, new ScopedGameTrafficFilter(Endpoint), new NosTaleWorldProtocolDecoder()));
        return (wire, clock, new NetworkGameplayProvider(feed, clock, maxVitalsAge));
    }

    // -------------------------------------------------------- the wire's own time

    /// <summary>
    /// The reading carries the time the packet crossed the wire, not the time the
    /// poll ran.
    /// </summary>
    /// <remarks>
    /// This is what keeps a replay from looking current. A recording is CACHED and
    /// so is a reading the provider remembered between two packets; if both were
    /// stamped "now", nothing downstream could tell a session from two days ago
    /// apart from a value read a moment ago, and ADR-0016 lets a fresh CACHED
    /// reading drive an action.
    /// </remarks>
    [Fact]
    public void A_reading_carries_the_time_the_packet_crossed_the_wire()
    {
        (ScriptedSource wire, _, NetworkGameplayProvider provider) = Chain();
        wire.Send("stat 7305 7305 1420 1420 0 1184");     // stamped at Start

        GameplayObservation observation = provider.Observe();

        Assert.True(observation.HasVitals);
        Assert.Equal(Start, observation.Hp.ObservedAtUtc);
    }

    [Fact]
    public void A_packet_recorded_long_ago_does_not_become_fresh_by_being_replayed()
    {
        var clock = new StubClock();
        var wire = new ScriptedSource(DataSourceKind.Cached, () => clock.GetUtcNow().UtcDateTime);
        var feed = new NetworkWorldFeed(new GameTrafficObserver(
            wire, new ScopedGameTrafficFilter(Endpoint), new NosTaleWorldProtocolDecoder()));
        var provider = new NetworkGameplayProvider(feed, clock);

        // The packet was recorded at Start; the runtime replaying it is two days later.
        clock.Advance(TimeSpan.FromDays(2));
        wire.Send("stat 7305 7305 1420 1420 0 1184", capturedUtc: Start);

        GameplayObservation observation = provider.Observe();

        Assert.True(observation.HasVitals);
        Assert.Equal(7305, observation.Hp.Value);
        Assert.Equal(DataSourceKind.Cached, observation.Hp.Source);
        // The age is the recording's, so a freshness rule refuses it on its own.
        Assert.Equal(Start, observation.Hp.ObservedAtUtc);
        Assert.Equal(Start.AddDays(2), observation.ObservedAtUtc);
    }

    // ---------------------------------------------------------------- freshness

    [Fact]
    public void A_batch_carrying_stat_publishes_it_with_the_packet_provenance()
    {
        (ScriptedSource wire, _, NetworkGameplayProvider provider) = Chain();
        wire.Send("stat 7305 7305 1420 1420 0 1184");

        GameplayObservation observation = provider.Observe();

        Assert.True(observation.HasVitals);
        Assert.Equal(7305, observation.Hp.Value);
        Assert.Equal(DataSourceKind.Live, observation.Hp.Source);
        Assert.Equal(Start, observation.Hp.ObservedAtUtc);
    }

    /// <summary>
    /// The gap this exists for. <c>stat</c> is sent when the number changes, so
    /// most polls carry none — and an HP that was read a second ago is not
    /// unknown, it is not current. CACHED says exactly that, and the timestamp is
    /// the one the reading really has, not the time of the poll that repeated it.
    /// </summary>
    [Fact]
    public void Between_two_stat_packets_the_last_reading_is_republished_cached()
    {
        (ScriptedSource wire, StubClock clock, NetworkGameplayProvider provider) = Chain();
        wire.Send("stat 7305 7305 1420 1420 0 1184");
        provider.Observe();

        clock.Advance(TimeSpan.FromSeconds(1));
        wire.Send("mv 3 3194 121 110 5");          // traffic, but nothing about the player
        GameplayObservation observation = provider.Observe();

        Assert.True(observation.HasVitals);
        Assert.Equal(7305, observation.Hp.Value);
        Assert.Equal(DataSourceKind.Cached, observation.Hp.Source);
        Assert.Equal(DataSourceKind.Cached, observation.MaxHp.Source);
        Assert.Equal(DataSourceKind.Cached, observation.Mp.Source);
        Assert.Equal(Start, observation.Hp.ObservedAtUtc);
        Assert.Equal(Start.AddSeconds(1), observation.ObservedAtUtc);
    }

    [Fact]
    public void A_reading_older_than_the_bound_is_unknown_not_repeated()
    {
        (ScriptedSource wire, StubClock clock, NetworkGameplayProvider provider) =
            Chain(maxVitalsAge: TimeSpan.FromSeconds(5));
        wire.Send("stat 7305 7305 1420 1420 0 1184");
        provider.Observe();

        clock.Advance(TimeSpan.FromSeconds(5.001));
        wire.Send("mv 3 3194 121 110 5");
        GameplayObservation observation = provider.Observe();

        Assert.False(observation.HasVitals);
        Assert.Equal("player_vitals_stale", observation.UnusableReason);
        Assert.Equal(DataSourceKind.Unknown, observation.Hp.Source);
    }

    [Fact]
    public void A_new_reading_restarts_the_clock_on_the_old_one()
    {
        (ScriptedSource wire, StubClock clock, NetworkGameplayProvider provider) =
            Chain(maxVitalsAge: TimeSpan.FromSeconds(5));
        wire.Send("stat 7305 7305 1420 1420 0 1184");
        provider.Observe();

        clock.Advance(TimeSpan.FromSeconds(4));
        wire.Send("stat 7218 7305 1362 1420 0 1184");
        provider.Observe();

        clock.Advance(TimeSpan.FromSeconds(4));         // 8 s after the first reading
        GameplayObservation observation = provider.Observe();

        Assert.True(observation.HasVitals);
        Assert.Equal(7218, observation.Hp.Value);
        Assert.Equal(DataSourceKind.Cached, observation.Hp.Source);
        Assert.Equal(Start.AddSeconds(4), observation.Hp.ObservedAtUtc);
    }

    [Fact]
    public void Retention_can_be_turned_off_entirely()
    {
        (ScriptedSource wire, StubClock clock, NetworkGameplayProvider provider) =
            Chain(maxVitalsAge: TimeSpan.Zero);
        wire.Send("stat 7305 7305 1420 1420 0 1184");
        provider.Observe();

        clock.Advance(TimeSpan.FromMilliseconds(1));
        wire.Send("mv 3 3194 121 110 5");

        Assert.False(provider.Observe().HasVitals);
    }

    /// <summary>
    /// A stale reading must not be resurrected by a batch that contains nothing at
    /// all: an empty poll is the case where the wire has gone quiet, which is
    /// precisely when a remembered HP is least safe to act on.
    /// </summary>
    [Fact]
    public void An_expired_reading_stays_expired_when_the_wire_goes_quiet()
    {
        (ScriptedSource wire, StubClock clock, NetworkGameplayProvider provider) =
            Chain(maxVitalsAge: TimeSpan.FromSeconds(5));
        wire.Send("stat 7305 7305 1420 1420 0 1184");
        provider.Observe();

        clock.Advance(TimeSpan.FromMinutes(1));
        GameplayObservation observation = provider.Observe();

        Assert.False(observation.HasVitals);
        Assert.Equal("player_vitals_stale", observation.UnusableReason);
    }

    // ------------------------------------------------------------------ reasons

    /// <summary>
    /// Three ways to have no vitals that look identical from outside and are not:
    /// a decoder that cannot read them, one that can and has not seen any yet, and
    /// one whose last reading has expired. Only the first is a protocol map to
    /// finish; reporting the others as that would send the operator after a
    /// problem that is not there.
    /// </summary>
    [Fact]
    public void A_decoder_that_can_read_vitals_does_not_report_them_as_unmapped()
    {
        (ScriptedSource wire, _, NetworkGameplayProvider provider) = Chain();
        wire.Send("in 3 36 3194 120 109 2 100 100");     // decodes, but says nothing about the player

        Assert.Equal("player_vitals_not_seen_yet", provider.Observe().UnusableReason);
    }

    /// <summary>
    /// Traffic that produced no observation at all is a different report from
    /// traffic that produced one without vitals: the first is a channel that may
    /// be pointed at the wrong thing, the second is a normal quiet moment.
    /// </summary>
    [Fact]
    public void A_channel_that_decoded_nothing_says_so()
    {
        (ScriptedSource wire, _, NetworkGameplayProvider provider) = Chain();
        wire.Send("guri 2 1 3443217 0");            // a real opcode this decoder does not read

        Assert.Equal("nothing_decoded", provider.Observe().UnusableReason);
    }

    // ----------------------------------------------------------------- entities

    [Fact]
    public void One_entity_moving_twice_is_one_entity()
    {
        (ScriptedSource wire, _, NetworkGameplayProvider provider) = Chain();
        wire.Send("in 3 36 3194 120 109 2 100 100");
        wire.Send("mv 3 3194 121 110 5");
        wire.Send("mv 3 3194 122 111 5");

        GameplayObservation observation = provider.Observe();

        Assert.True(observation.EntitiesInView.HasValue);
        Assert.Equal(1, observation.EntitiesInView.Value);
    }

    /// <summary>
    /// The idle recording is the whole argument: 2468 movement packets, not one
    /// <c>in</c> or <c>st</c>, so nothing the decoder can turn into a sighting —
    /// and the screen was full of monsters throughout. A zero there would have
    /// been a confident wrong answer on every poll.
    /// </summary>
    [Fact]
    public void A_batch_that_mentions_no_entity_reports_unknown_not_zero()
    {
        (ScriptedSource wire, _, NetworkGameplayProvider provider) = Chain();
        wire.Send("stat 7305 7305 1420 1420 0 1184");
        wire.Send("mv 3 3194 121 110 5");           // no prior spawn: no sighting

        GameplayObservation observation = provider.Observe();

        Assert.True(observation.HasVitals);
        Assert.False(observation.EntitiesInView.HasValue);
        Assert.Equal("no_entities_reported", observation.EntitiesInView.FailureReason);
    }

    // ------------------------------------------------- provenance of a sighting

    /// <summary>
    /// <c>mv</c> carries a position and no health, so the sighting it produces is
    /// half this packet and half whatever last mentioned that entity's HP. LIVE
    /// would claim both halves are current.
    /// </summary>
    [Fact]
    public void A_move_reusing_an_earlier_hp_is_cached_even_on_live_bytes()
    {
        var decoder = new NosTaleWorldProtocolDecoder();
        decoder.Decode(Packet("in 3 36 3194 120 109 2 80 100", DataSourceKind.Live));

        EntitySighting moved = Assert.Single(
            decoder.Decode(Packet("mv 3 3194 121 110 5", DataSourceKind.Live)).Sightings);

        Assert.Equal(0.80, moved.HpRatio, 9);
        Assert.Equal(DataSourceKind.Cached, moved.Source);
    }

    /// <summary><c>st</c> is the mirror case: fresh health, remembered position.</summary>
    [Fact]
    public void Entity_vitals_reusing_an_earlier_position_are_cached()
    {
        var decoder = new NosTaleWorldProtocolDecoder();
        decoder.Decode(Packet("in 3 36 313816 109 63 2 100 100", DataSourceKind.Live));

        EntitySighting seen = Assert.Single(
            decoder.Decode(Packet("st 3 313816 8 0 66 100 198 52 310 52 0", DataSourceKind.Live)).Sightings);

        Assert.Equal(198.0 / 310.0, seen.HpRatio, 9);
        Assert.Equal(DataSourceKind.Cached, seen.Source);
    }

    /// <summary>
    /// Spawn is the one message carrying both halves, so it is the one sighting
    /// that keeps the packet's own provenance.
    /// </summary>
    [Fact]
    public void A_spawn_keeps_the_packet_provenance_because_nothing_is_remembered()
    {
        EntitySighting spawned = Assert.Single(new NosTaleWorldProtocolDecoder()
            .Decode(Packet("in 3 36 3194 120 109 2 80 100", DataSourceKind.Live)).Sightings);

        Assert.Equal(DataSourceKind.Live, spawned.Source);
    }

    // --------------------------------------------------------- entity type gate

    /// <summary>
    /// The catalogue's shapes are entity type 3's. Type 1 was confirmed only in
    /// <c>su</c>, <c>cond</c> and <c>sayi</c>, and another player entering view
    /// carries a name where a monster carries a vnum — so reading x and y at the
    /// monster's positions would take a coordinate out of something else. The
    /// numbers below parse perfectly well; that is exactly why they are refused.
    /// </summary>
    [Theory]
    [InlineData("in 1 36 3443217 120 109 2 100 100")]
    [InlineData("in 2 36 3443217 120 109 2 100 100")]
    [InlineData("mv 1 3443217 121 110 5")]
    [InlineData("st 1 3443217 8 0 66 100 198 52 310 52 0")]
    public void An_entity_type_this_decoder_has_never_seen_is_refused(string line)
    {
        Assert.True(new NosTaleWorldProtocolDecoder()
            .Decode(Packet(line, DataSourceKind.Live)).IsEmpty);
    }

    /// <summary>
    /// Refusing the player's own type is not a loss of information here: the
    /// server never sends the player's position at all, so there was nothing to
    /// read. <c>stat</c> remains the only source for the player, and it is not an
    /// entity message.
    /// </summary>
    [Fact]
    public void Refusing_type_one_does_not_cost_the_player_position_because_it_is_never_sent()
    {
        var decoder = new NosTaleWorldProtocolDecoder();

        Assert.True(decoder.Decode(Packet("mv 1 3443217 121 110 5", DataSourceKind.Live)).IsEmpty);
        Assert.NotNull(decoder
            .Decode(Packet("stat 7305 7305 1420 1420 0 1184", DataSourceKind.Live)).Vitals);
    }

    private static ObservedPacket Packet(string line, DataSourceKind source) => new(
        Start, NetworkDirection.Inbound, Endpoint.Host, Endpoint.Port,
        Encoding.ASCII.GetBytes(line), source);
}
