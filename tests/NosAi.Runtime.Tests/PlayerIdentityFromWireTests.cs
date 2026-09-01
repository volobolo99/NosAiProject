using System.Text;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// What the wire says about the controlled character itself: its own entity id
/// and its movement speed.
/// </summary>
/// <remarks>
/// <para>
/// Both come from <c>cond</c>, and both were missing facts rather than missing
/// code. The id is what ADR-0018 named as the place to tighten <c>HasTarget</c>:
/// entity type 1 says "a player attacked", not "this character attacked", so
/// another player fighting nearby could contradict a screen that had read the
/// target frame correctly. The speed is the bound F1-10's continuity check
/// measures a step against, and without it that check cannot run.
/// </para>
/// <para>
/// Observed on a live capture of 1 Sep 2026: own id 3443217, speed 11 — the same
/// id and speed docs/PROTOCOLLO_NOSTALE.md recorded from the earlier captures.
/// </para>
/// </remarks>
public sealed class PlayerIdentityFromWireTests
{
    private static readonly DateTime At = new(2026, 9, 1, 17, 55, 0, DateTimeKind.Utc);
    private static readonly GameEndpoint Endpoint = new("79.110.84.175", 4007);

    private const long OwnId = 3443217;
    private const long StrangerId = 9999999;

    private static ObservedPacket Packet(string line, DateTime? at = null) => new(
        at ?? At, NetworkDirection.Inbound, Endpoint.Host, Endpoint.Port,
        Encoding.ASCII.GetBytes(line), DataSourceKind.Live);

    // ------------------------------------------------------------- cond reads

    [Fact]
    public void Cond_names_the_controlled_characters_own_entity_id()
    {
        DecodedObservations decoded = new NosTaleWorldProtocolDecoder()
            .Decode(Packet($"cond 1 {OwnId} 0 0 11"));

        Assert.Equal(OwnId, decoded.PlayerEntityId);
        Assert.Equal(11, decoded.PlayerMovementSpeed);
    }

    /// <summary>A <c>cond</c> for anything but a player is not about this character.</summary>
    [Fact]
    public void A_cond_for_another_entity_type_names_nobody()
    {
        DecodedObservations decoded = new NosTaleWorldProtocolDecoder()
            .Decode(Packet("cond 3 313816 0 0 11"));

        Assert.True(decoded.IsEmpty);
        Assert.Null(decoded.PlayerEntityId);
    }

    // -------------------------------------- the tightening ADR-0018 asked for

    /// <summary>
    /// Before <c>cond</c> has named the character, type 1 is all there is, so any
    /// player's hit counts. The error that causes is a false disagreement, whose
    /// result is UNKNOWN — a fact the planner skips, never a wrong answer.
    /// </summary>
    [Fact]
    public void Before_the_own_id_is_known_any_players_hit_counts_as_the_players()
    {
        DecodedObservations decoded = new NosTaleWorldProtocolDecoder()
            .Decode(Packet($"su 1 {StrangerId} 3 313816 226 250 12 522 0 0 0 0 698 5 0 0 310"));

        Assert.Equal(At, decoded.PlayerAttackedAtUtc);
    }

    /// <summary>
    /// Once it is known, the id decides, and a stranger fighting nearby stops
    /// contradicting the screen. This is the gap ADR-0018 recorded and left open.
    /// </summary>
    [Fact]
    public void Once_the_own_id_is_known_a_strangers_hit_no_longer_counts()
    {
        var decoder = new NosTaleWorldProtocolDecoder();
        decoder.Decode(Packet($"cond 1 {OwnId} 0 0 11"));

        DecodedObservations stranger = decoder.Decode(
            Packet($"su 1 {StrangerId} 3 313816 226 250 12 522 0 0 0 0 698 5 0 0 310"));

        Assert.Null(stranger.PlayerAttackedAtUtc);
    }

    [Fact]
    public void Once_the_own_id_is_known_this_characters_hit_still_counts()
    {
        var decoder = new NosTaleWorldProtocolDecoder();
        decoder.Decode(Packet($"cond 1 {OwnId} 0 0 11"));

        DecodedObservations mine = decoder.Decode(
            Packet($"su 1 {OwnId} 3 313816 226 250 12 522 0 0 0 0 698 5 0 0 310"));

        Assert.Equal(At, mine.PlayerAttackedAtUtc);
    }

    /// <summary>A monster hitting the player is never the player attacking.</summary>
    [Fact]
    public void A_monster_hitting_the_player_never_counts_either_way()
    {
        var decoder = new NosTaleWorldProtocolDecoder();
        decoder.Decode(Packet($"cond 1 {OwnId} 0 0 11"));

        DecodedObservations hit = decoder.Decode(
            Packet($"su 3 313816 1 {OwnId} 0 12 11 200 0 0 1 99 0 1 0 7289 7305"));

        Assert.Null(hit.PlayerAttackedAtUtc);
    }

    // ------------------------------------------------ out through the observer

    [Fact]
    public void The_report_carries_the_id_and_the_speed_out_of_the_batch()
    {
        var source = new ListSource(
            Packet($"cond 1 {OwnId} 0 0 11"),
            Packet("stat 6971 7305 1326 1420 0 1184"));
        var observer = new GameTrafficObserver(
            source, new ScopedGameTrafficFilter(Endpoint), new NosTaleWorldProtocolDecoder());

        NetworkObservationReport report = observer.ObservePending(16);

        Assert.Equal(OwnId, report.PlayerEntityId);
        Assert.Equal(11, report.PlayerMovementSpeed);
    }

    /// <summary>
    /// The id does not change within a session, so a later batch that carries no
    /// <c>cond</c> simply reports none rather than contradicting the earlier one.
    /// </summary>
    [Fact]
    public void A_batch_without_a_cond_reports_no_id_rather_than_a_wrong_one()
    {
        var source = new ListSource(Packet("stat 6971 7305 1326 1420 0 1184"));
        var observer = new GameTrafficObserver(
            source, new ScopedGameTrafficFilter(Endpoint), new NosTaleWorldProtocolDecoder());

        NetworkObservationReport report = observer.ObservePending(16);

        Assert.Null(report.PlayerEntityId);
        Assert.Null(report.PlayerMovementSpeed);
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
