using System.Text;
using NosAi.Runtime.AI.Decision;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The player's own health, from the message that carries it.
/// </summary>
/// <remarks>
/// <para>
/// Both consumers of the feed used to look for the player in the sightings, under
/// the channel's convention that entity id 0 is the controlled character. On the
/// real NosTale wire that finds nothing, ever: the server never sights the player.
/// Every one of the 7685 movement packets in the combat capture is another entity,
/// because position is client-authoritative and that direction is separately
/// encrypted (docs/PROTOCOLLO_NOSTALE.md).
/// </para>
/// <para>
/// So the world model and the decision engine reported "player_not_sighted" while
/// the exact HP and max HP sat unused in the same report. These tests pin the fix
/// and, as much, the two things it must not do: invent a ratio when there are no
/// vitals, and stop working for a decoder that does sight the player.
/// </para>
/// </remarks>
public sealed class NetworkWorldFeedVitalsTests
{
    private static readonly GameEndpoint Endpoint = new("79.110.84.175", 4002);
    private static readonly DateTime At = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

    private sealed class ScriptedSource : INetworkObservationSource
    {
        private readonly Queue<ObservedPacket> _packets = new();
        public DataSourceKind Source { get; }

        public ScriptedSource(DataSourceKind source) => Source = source;

        public void Send(string line) => _packets.Enqueue(new ObservedPacket(
            At, NetworkDirection.Inbound, Endpoint.Host, Endpoint.Port,
            Encoding.ASCII.GetBytes(line), Source));

        public bool TryObserve(out ObservedPacket packet)
        {
            if (_packets.Count == 0) { packet = null!; return false; }
            packet = _packets.Dequeue();
            return true;
        }
    }

    /// <summary>A decoder that does sight the player, the way the synthetic protocol does.</summary>
    private sealed class PlayerSightingDecoder : IGamePacketDecoder
    {
        public string ProtocolName => "test-player-sighting";
        public bool ReadsPlayerVitals => false;
        public bool CanDecode(ObservedPacket packet) => true;

        public DecodedObservations Decode(ObservedPacket packet) => new(
            System.Collections.Immutable.ImmutableArray.Create(
                new EntitySighting(0, "Player", 10, 20, 0.5, packet.Source)),
            System.Collections.Immutable.ImmutableArray<GameEvent>.Empty);
    }

    /// <summary>A decoder that sights the player but never reads its health.</summary>
    private sealed class PlayerSightingWithoutHealthDecoder : IGamePacketDecoder
    {
        public string ProtocolName => "test-player-sighting-no-health";
        public bool ReadsPlayerVitals => false;
        public bool CanDecode(ObservedPacket packet) => true;

        public DecodedObservations Decode(ObservedPacket packet) => new(
            System.Collections.Immutable.ImmutableArray.Create(
                new EntitySighting(0, "Player", 10, 20, null, packet.Source)),
            System.Collections.Immutable.ImmutableArray<GameEvent>.Empty);
    }

    private static NetworkWorldFeed Feed(ScriptedSource wire, IGamePacketDecoder? decoder = null)
        => new(new GameTrafficObserver(
            wire, new ScopedGameTrafficFilter(Endpoint), decoder ?? new NosTaleWorldProtocolDecoder()));

    // ---------------------------------------------------------- world state

    [Fact]
    public void The_player_hp_comes_from_the_vitals_message()
    {
        var wire = new ScriptedSource(DataSourceKind.Live);
        wire.Send("stat 3652 7305 1420 1420 0 1184");     // exactly half health
        NetworkWorldFeed feed = Feed(wire);
        feed.Poll();

        Assert.True(feed.TryToWorldState(out NosAi.Runtime.WorldModel.WorldState state, out string? reason));

        Assert.Null(reason);
        Assert.Equal(3652.0 / 7305.0, state.PlayerHpRatio, 6);
        Assert.True(state.PlayerAlive);
    }

    [Fact]
    public void Entities_seen_in_the_same_batch_come_with_it()
    {
        var wire = new ScriptedSource(DataSourceKind.Live);
        wire.Send("stat 7305 7305 1420 1420 0 1184");
        wire.Send("in 3 36 313816 109 63 2 80 100");
        NetworkWorldFeed feed = Feed(wire);
        feed.Poll();

        Assert.True(feed.TryToWorldState(out NosAi.Runtime.WorldModel.WorldState state, out _));

        NosAi.Runtime.WorldModel.EntityState entity = Assert.Single(state.Entities);
        Assert.Equal(109, entity.X);
        Assert.Equal(0.80, entity.HpRatio!.Value, 6);
    }

    /// <summary>
    /// A dead character is a reading, not a failure: zero HP with a real maximum
    /// must reach the world model rather than be refused as unobserved.
    /// </summary>
    [Fact]
    public void Zero_hp_is_a_reading_and_the_player_is_not_alive()
    {
        var wire = new ScriptedSource(DataSourceKind.Live);
        wire.Send("stat 0 7305 0 1420 0 1184");
        NetworkWorldFeed feed = Feed(wire);
        feed.Poll();

        Assert.True(feed.TryToWorldState(out NosAi.Runtime.WorldModel.WorldState state, out _));

        Assert.Equal(0.0, state.PlayerHpRatio);
        Assert.False(state.PlayerAlive);
    }

    [Fact]
    public void With_no_vitals_in_the_batch_no_state_is_produced()
    {
        // Traffic decoded, and none of it about the player. The caller must keep
        // its previous state or wait: WorldState carries no provenance, so a
        // placeholder inserted here would be indistinguishable from an observation
        // for the rest of its life.
        var wire = new ScriptedSource(DataSourceKind.Live);
        wire.Send("in 3 36 313816 109 63 2 80 100");
        NetworkWorldFeed feed = Feed(wire);
        feed.Poll();

        Assert.False(feed.TryToWorldState(out _, out string? reason));
        Assert.Equal("player_vitals_not_in_batch", reason);
    }

    /// <summary>
    /// A decoder that cannot read vitals at all gets the older reason, because for
    /// it the sighting really is the only way the player could have appeared.
    /// </summary>
    [Fact]
    public void A_decoder_without_vitals_still_reports_player_not_sighted()
    {
        var wire = new ScriptedSource(DataSourceKind.Live);
        wire.Send("anything");
        var feed = new NetworkWorldFeed(new GameTrafficObserver(
            wire, new ScopedGameTrafficFilter(Endpoint), new SyntheticProtocolDecoder()));
        feed.Poll();

        Assert.False(feed.TryToWorldState(out _, out string? reason));
        Assert.Equal("player_not_sighted", reason);
    }

    /// <summary>The sighting path is not removed: a decoder that uses it keeps working.</summary>
    [Fact]
    public void A_decoder_that_sights_the_player_is_still_read()
    {
        var wire = new ScriptedSource(DataSourceKind.Live);
        wire.Send("anything");
        NetworkWorldFeed feed = Feed(wire, new PlayerSightingDecoder());
        feed.Poll();

        Assert.True(feed.TryToWorldState(out NosAi.Runtime.WorldModel.WorldState state, out _));
        Assert.Equal(0.5, state.PlayerHpRatio, 6);
    }

    // ------------------------------------------------------ decision context

    [Fact]
    public void The_decision_context_gets_the_player_ratio_from_the_vitals()
    {
        var wire = new ScriptedSource(DataSourceKind.Live);
        wire.Send("stat 3652 7305 1420 1420 0 1184");
        NetworkWorldFeed feed = Feed(wire);
        feed.Poll();

        DecisionContext context = feed.ToDecisionContext();

        Assert.True(context.TryRead("player.hp_ratio", out double hp, out DataSourceKind source));
        Assert.Equal(3652.0 / 7305.0, hp, 6);
        Assert.Equal(DataSourceKind.Live, source);
    }

    /// <summary>
    /// Recorded as unknown, never defaulted and never left out. A rule that needs
    /// the player's health has to be skipped, and it can only be skipped if the
    /// fact is present as unknown rather than missing by accident.
    /// </summary>
    [Fact]
    public void Without_vitals_the_context_carries_the_fact_as_unknown()
    {
        var wire = new ScriptedSource(DataSourceKind.Live);
        wire.Send("in 3 36 313816 109 63 2 80 100");
        NetworkWorldFeed feed = Feed(wire);
        feed.Poll();

        DecisionContext context = feed.ToDecisionContext();

        Assert.Contains("player.hp_ratio", context.FactNames);
        Assert.False(context.TryRead("player.hp_ratio", out double hp, out DataSourceKind source));
        Assert.Equal(DataSourceKind.Unknown, source);
        Assert.Equal(0, hp);
    }

    /// <summary>
    /// The target was located and its health was not read, which is neither
    /// "target not sighted" nor health of zero. The reason names which of the two
    /// it is, because they are fixed in different places.
    /// </summary>
    [Fact]
    public void A_target_seen_without_health_is_unknown_with_its_own_reason()
    {
        var wire = new ScriptedSource(DataSourceKind.Live);
        wire.Send("stat 3652 7305 1420 1420 0 1184");
        wire.Send("mv 3 313816 109 63 5");           // no prior spawn: position only
        NetworkWorldFeed feed = Feed(wire);
        feed.Poll();

        DecisionContext context = feed.ToDecisionContext(currentTargetId: 313816);

        Assert.Contains("target.hp_ratio", context.FactNames);
        Assert.False(context.TryRead("target.hp_ratio", out double hp, out DataSourceKind source));
        Assert.Equal(DataSourceKind.Unknown, source);
        Assert.Equal(0, hp);
        // And the entity was still counted: the position is the part that was read.
        Assert.True(context.TryRead("monsters.count", out double monsters, out _));
        Assert.Equal(1, monsters);
    }

    /// <summary>
    /// A decoder that sights the player without health does not report the player
    /// as missing: it was found, and its health was not.
    /// </summary>
    [Fact]
    public void A_player_sighted_without_health_says_the_health_was_not_observed()
    {
        var wire = new ScriptedSource(DataSourceKind.Live);
        wire.Send("anything");
        NetworkWorldFeed feed = Feed(wire, new PlayerSightingWithoutHealthDecoder());
        feed.Poll();

        DecisionContext context = feed.ToDecisionContext();

        Assert.Contains("player.hp_ratio", context.FactNames);
        Assert.False(context.TryRead("player.hp_ratio", out double hp, out DataSourceKind source));
        Assert.Equal(DataSourceKind.Unknown, source);
        Assert.Equal(0, hp);
    }

    /// <summary>A replayed capture stays CACHED all the way into the context.</summary>
    [Fact]
    public void A_replay_reaches_the_context_as_cached()
    {
        var wire = new ScriptedSource(DataSourceKind.Cached);
        wire.Send("stat 3652 7305 1420 1420 0 1184");
        NetworkWorldFeed feed = Feed(wire);
        feed.Poll();

        Assert.True(feed.ToDecisionContext().TryRead("player.hp_ratio", out _, out DataSourceKind source));
        Assert.Equal(DataSourceKind.Cached, source);
    }
}
