using System.Buffers.Binary;
using System.Net;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The "senza osservazione" number, broken into the reasons a packet can produce
/// no observation: opcode not read, line refused, legitimately empty by design,
/// or unexplained. Each reason is counted apart, and the four sums must equal
/// the total the census reports.
/// </summary>
/// <remarks>
/// The hand-built packets pin the three reasons on shapes nobody writes for the
/// tests; the <c>RecordedCaptureFact</c> tests hold the sum invariant against
/// the real bytes, where a fourth reason would surface if one existed.
/// </remarks>
public sealed class UnobservedBreakdownTests
{
    private static readonly IPAddress Server = IPAddress.Parse("79.110.84.175");
    private const int ServerPort = 4002;
    private const string Client = "192.168.0.4";
    private const int ClientPort = 56027;

    /// <summary>
    /// An opcode nobody reads is not silence: it is traffic that arrived and was
    /// not read, counted under its own name.
    /// </summary>
    [Fact]
    public void An_unknown_opcode_is_not_read_and_counted_under_its_own_name()
    {
        WorldChannelReplaySummary summary = WorldChannelReplay.Replay(Recording(
            Encoded("guri 2 1 3443217 0")));
        UnobservedBreakdown b = summary.Unobserved;

        Assert.Equal(1, summary.UndecodedMessages);
        Assert.Equal(1, b.NotReadTotal);
        Assert.Contains(b.NotRead, o => o.Key == "guri" && o.Value == 1);
        Assert.Equal(0, b.RejectedTotal);
        Assert.Equal(0, b.EmptyByDesignTotal);
        Assert.Equal(0, b.UnexplainedTotal);
    }

    /// <summary>
    /// A read opcode whose line the decoder refuses — a value outside its bounds
    /// — is a refusal, not an absence. A refusal is a fact, and here it stops
    /// disappearing into the single number.
    /// </summary>
    [Fact]
    public void A_read_opcode_with_a_refused_line_is_rejected()
    {
        // mp 500 above maxMp 400: the decoder refuses the packet whole.
        WorldChannelReplaySummary summary = WorldChannelReplay.Replay(Recording(
            Encoded("stat 1000 1000 500 400 0 1184")));
        UnobservedBreakdown b = summary.Unobserved;

        Assert.Equal(1, summary.UndecodedMessages);
        Assert.Equal(1, b.RejectedTotal);
        Assert.Contains(b.Rejected, o => o.Key == "stat" && o.Value == 1);
        Assert.Equal(0, b.NotReadTotal);
        Assert.Equal(0, b.EmptyByDesignTotal);
    }

    /// <summary>
    /// An entity type the decoder does not read — type 2, never observed in the
    /// captures the decoder was derived from — is refused whole. This is the
    /// reason <c>equip_test.noscap</c> has so many unobserved <c>mv</c>.
    /// </summary>
    [Fact]
    public void An_entity_type_the_decoder_does_not_read_is_rejected()
    {
        WorldChannelReplaySummary summary = WorldChannelReplay.Replay(Recording(
            Encoded("mv 2 3062 153 23 5")));
        UnobservedBreakdown b = summary.Unobserved;

        Assert.Equal(1, summary.UndecodedMessages);
        Assert.Equal(1, b.RejectedTotal);
        Assert.Contains(b.Rejected, o => o.Key == "mv" && o.Value == 1);
    }

    /// <summary>
    /// A <c>st</c> for an entity no <c>in</c>/<c>mv</c> introduced carries health
    /// and no position, so no sighting can be placed: the line is valid and the
    /// result is legitimately empty. The same <c>st</c> after an <c>in</c> is not
    /// empty, because the position now exists.
    /// </summary>
    [Fact]
    public void A_st_for_an_unpositioned_entity_is_empty_by_design_and_not_after_an_in()
    {
        WorldChannelReplaySummary before = WorldChannelReplay.Replay(Recording(
            Encoded("st 3 999 8 0 66 100 198 52 310 52 0")));
        UnobservedBreakdown b = before.Unobserved;

        Assert.Equal(1, b.EmptyByDesignTotal);
        Assert.Contains(b.EmptyByDesign, o => o.Key == "st" && o.Value == 1);
        Assert.Equal(0, b.RejectedTotal);

        WorldChannelReplaySummary after = WorldChannelReplay.Replay(Recording(
            Encoded("in 3 36 999 10 20 2 100 100"),
            Encoded("st 3 999 8 0 66 100 198 52 310 52 0")));

        Assert.Equal(0, after.UndecodedMessages);
        Assert.Equal(0, after.Unobserved.EmptyByDesignTotal);
    }

    /// <summary>
    /// The whole point of the breakdown: every unobserved packet lands in exactly
    /// one category, none is lost and none is counted twice, and a decoded packet
    /// never appears in any of them.
    /// </summary>
    [Fact]
    public void The_categories_sum_exactly_to_the_undecoded_total()
    {
        WorldChannelReplaySummary summary = WorldChannelReplay.Replay(Recording(
            Encoded("guri 2 1 3443217 0"),                     // not read
            Encoded("stat 1000 1000 500 400 0 1184"),          // rejected (mp > maxMp)
            Encoded("st 3 999 8 0 66 100 198 52 310 52 0"),    // empty by design
            Encoded("mv 3 999 10 20 11")));                    // decoded: not counted
        UnobservedBreakdown b = summary.Unobserved;

        Assert.Equal(3, summary.UndecodedMessages);
        Assert.Equal(summary.UndecodedMessages, b.Total);
        Assert.Equal(1, b.NotReadTotal);
        Assert.Equal(1, b.RejectedTotal);
        Assert.Equal(1, b.EmptyByDesignTotal);
        Assert.Equal(0, b.UnexplainedTotal);
    }

    /// <summary>
    /// Against the real bytes: the breakdown accounts for exactly what the census
    /// reports, and nothing is left unexplained. If a fourth reason ever appears
    /// in a capture, this is where it would be caught rather than absorbed.
    /// </summary>
    [RecordedCaptureFact("equip_test.noscap")]
    public void Equip_test_breakdown_sums_to_the_census_total_and_explains_everything()
    {
        string path = RecordedCaptureFactAttribute.Resolve("equip_test.noscap")!;
        WorldChannelReplaySummary summary = WorldChannelReplay.ReplayFile(path);
        UnobservedBreakdown b = summary.Unobserved;

        Assert.Equal(summary.UndecodedMessages, b.Total);
        Assert.Equal(0, b.UnexplainedTotal);
        Assert.True(b.RejectedTotal > 0, "equip_test carries refused lines (mv type 2)");
    }

    /// <summary>
    /// The idle capture produces an observation for every packet, so the whole
    /// breakdown is zero — the control that the breakdown does not invent rows.
    /// </summary>
    [RecordedCaptureFact("nostale_01.noscap")]
    public void The_idle_recording_has_zero_unobserved_so_all_categories_are_zero()
    {
        string path = RecordedCaptureFactAttribute.Resolve("nostale_01.noscap")!;
        WorldChannelReplaySummary summary = WorldChannelReplay.ReplayFile(path);
        UnobservedBreakdown b = summary.Unobserved;

        Assert.Equal(0, summary.UndecodedMessages);
        Assert.Equal(0, b.Total);
        Assert.Equal(0, b.NotReadTotal);
        Assert.Equal(0, b.RejectedTotal);
        Assert.Equal(0, b.EmptyByDesignTotal);
        Assert.Equal(0, b.UnexplainedTotal);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>A source the replay can open three times, as it must.</summary>
    private static Func<IPacketSource> Recording(params byte[][] bodies)
    {
        var packets = new List<CapturedPacket>();
        uint seq = 1000;
        foreach (byte[] body in bodies)
        {
            packets.Add(new CapturedPacket(
                new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc), TcpPacket(seq, body)));
            seq += (uint)body.Length;
        }

        return () => new InMemoryPacketSource(Server, ServerPort, packets);
    }

    private static byte[] TcpPacket(uint seq, ReadOnlySpan<byte> body)
    {
        var packet = new byte[20 + 20 + body.Length];
        packet[0] = 0x45;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), (ushort)packet.Length);
        packet[9] = 6;
        Server.GetAddressBytes().CopyTo(packet, 12);
        IPAddress.Parse(Client).GetAddressBytes().CopyTo(packet, 16);
        var tcp = packet.AsSpan(20);
        BinaryPrimitives.WriteUInt16BigEndian(tcp[..2], ServerPort);
        BinaryPrimitives.WriteUInt16BigEndian(tcp.Slice(2, 2), ClientPort);
        BinaryPrimitives.WriteUInt32BigEndian(tcp.Slice(4, 4), seq);
        tcp[12] = 5 << 4;
        body.CopyTo(tcp[20..]);
        return packet;
    }

    /// <summary>
    /// Encodes a line the way the server does: a length byte with the literal
    /// branch selected, each byte complemented, then the terminator.
    /// </summary>
    private static byte[] Encoded(string line)
    {
        var bytes = new List<byte>();
        foreach (string chunk in Chunks(line, 0x7F))
        {
            bytes.Add((byte)chunk.Length);
            foreach (char c in chunk)
                bytes.Add((byte)(c ^ 0xFF));
        }

        bytes.Add(NosTaleWorldDecoder.PacketTerminator);
        return bytes.ToArray();
    }

    private static IEnumerable<string> Chunks(string text, int size)
    {
        for (int i = 0; i < text.Length; i += size)
            yield return text.Substring(i, Math.Min(size, text.Length - i));
    }
}
