using System.Buffers.Binary;
using System.Net;
using System.Text;
using NosAi.LiveIntegration;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// S1: <c>icon type id ? vnum</c> carries the item vnum the client shows, read
/// from the wire and never guessed. Fields 1 and 3 stay unread — constant 1 in
/// every observed packet but with no meaning established.
/// </summary>
public sealed class IconItemVnumTests
{
    private static readonly DateTime At = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
    private static readonly GameEndpoint Endpoint = new("79.110.84.175", 4002);

    private static ObservedPacket Ascii(string packet, DataSourceKind source = DataSourceKind.Live)
        => new(At, NetworkDirection.Inbound, Endpoint.Host, Endpoint.Port, Encoding.ASCII.GetBytes(packet), source);

    [Fact]
    public void Icon_decodes_to_the_entity_and_the_item_vnum()
    {
        DecodedObservations decoded = new NosTaleWorldProtocolDecoder().Decode(Ascii("icon 1 3443217 1 2006"));

        ItemIconShown icon = Assert.IsType<ItemIconShown>(decoded.ItemIcon);
        Assert.Equal(3443217, icon.EntityId);
        Assert.Equal(2006, icon.ItemVnum);
        Assert.Equal(At, icon.ObservedAtUtc);
        Assert.Empty(decoded.Sightings);
        Assert.Empty(decoded.Events);
        Assert.False(decoded.IsEmpty);
    }

    [Fact]
    public void The_icon_is_self_contained_and_needs_no_own_id_from_cond()
    {
        // Field 2 is the entity and field 4 the vnum, on the packet itself, so a
        // fresh decoder with no cond reads it.
        DecodedObservations decoded = new NosTaleWorldProtocolDecoder().Decode(Ascii("icon 1 3548294 1 8"));

        Assert.Equal(8, Assert.IsType<ItemIconShown>(decoded.ItemIcon).ItemVnum);
    }

    [Fact]
    public void The_provenance_is_the_packets_own()
    {
        DecodedObservations cached = new NosTaleWorldProtocolDecoder().Decode(Ascii("icon 1 3443217 1 2006", DataSourceKind.Cached));
        Assert.Equal(DataSourceKind.Cached, cached.ItemIcon!.Source);

        DecodedObservations live = new NosTaleWorldProtocolDecoder().Decode(Ascii("icon 1 3443217 1 2006"));
        Assert.Equal(DataSourceKind.Live, live.ItemIcon!.Source);
    }

    [Theory]
    [InlineData("icon 1 3443217 1")]           // too short: no vnum
    [InlineData("icon 1 3443217 1 0")]         // vnum 0 is not an item
    [InlineData("icon 1 0 1 2006")]            // entity id 0 is nobody
    [InlineData("icon 1 notanid 1 2006")]      // the id is not a number
    [InlineData("icon 1 3443217 1 notavnum")]  // the vnum is not a number
    public void A_malformed_icon_produces_nothing(string line)
    {
        Assert.True(new NosTaleWorldProtocolDecoder().Decode(Ascii(line)).IsEmpty);
    }

    [Fact]
    public void The_observer_folds_icons_into_the_report()
    {
        NetworkObservationReport report = Observe("icon 1 3548294 1 8", "icon 1 3548294 1 2006");

        Assert.Equal(2, report.ItemIcons.Length);
        Assert.Equal(8, report.ItemIcons[0].ItemVnum);
        Assert.Equal(2006, report.ItemIcons[1].ItemVnum);
        Assert.Equal(2, report.DecodedPackets);
    }

    // ---------------------------------------------------------------- helpers

    private static NetworkObservationReport Observe(params string[] lines)
    {
        var packets = new List<CapturedPacket>();
        uint seq = 1000;
        foreach (string line in lines)
        {
            byte[] body = Encoded(line);
            packets.Add(new CapturedPacket(At, TcpPacket(seq, body)));
            seq += (uint)body.Length;
        }

        using var source = ReassembledObservationSource.ForNosTaleWorld(
            new InMemoryPacketSource(IPAddress.Parse(Endpoint.Host), Endpoint.Port, packets),
            DataSourceKind.Live);
        var observer = new GameTrafficObserver(
            source, new ScopedGameTrafficFilter(Endpoint), new NosTaleWorldProtocolDecoder());
        return observer.ObservePending(budget: Timeout.InfiniteTimeSpan);
    }

    /// <summary>Encodes a NosTale world line the way the server does.</summary>
    private static byte[] Encoded(string line)
    {
        var bytes = new List<byte>();
        for (int i = 0; i < line.Length; i += 0x7F)
        {
            string chunk = line.Substring(i, Math.Min(0x7F, line.Length - i));
            bytes.Add((byte)chunk.Length);
            foreach (char c in chunk)
                bytes.Add((byte)(c ^ 0xFF));
        }
        bytes.Add(NosTaleWorldDecoder.PacketTerminator);
        return bytes.ToArray();
    }

    private static byte[] TcpPacket(uint seq, ReadOnlySpan<byte> body)
    {
        var packet = new byte[20 + 20 + body.Length];
        packet[0] = 0x45;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), (ushort)packet.Length);
        packet[9] = 6;
        IPAddress.Parse(Endpoint.Host).GetAddressBytes().CopyTo(packet, 12);
        IPAddress.Parse("192.168.0.4").GetAddressBytes().CopyTo(packet, 16);
        var tcp = packet.AsSpan(20);
        BinaryPrimitives.WriteUInt16BigEndian(tcp[..2], (ushort)Endpoint.Port);
        BinaryPrimitives.WriteUInt16BigEndian(tcp.Slice(2, 2), 56027);
        BinaryPrimitives.WriteUInt32BigEndian(tcp.Slice(4, 4), seq);
        tcp[12] = 5 << 4;
        body.CopyTo(tcp[20..]);
        return packet;
    }
}
