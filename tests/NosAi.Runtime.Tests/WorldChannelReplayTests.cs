using System.Buffers.Binary;
using System.Net;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The offline reading a recording gets, and the two things it must never claim.
/// </summary>
/// <remarks>
/// <para>
/// <c>WinDivertProbe.exe --world &lt;file.noscap&gt;</c> is how the operator repeats
/// the check that the world decoder was written against, with no driver and no
/// client running. On <c>data/nostale_combat.noscap</c> it reports 62 <c>stat</c>
/// readings, HP 7218..7305 against a constant max of 7305 — the same numbers
/// <c>docs/PROTOCOLLO_NOSTALE.md</c> derived by hand, arrived at through the code
/// that ships.
/// </para>
/// <para>
/// The recordings are the operator's own session data and are not in the
/// repository, so what is pinned here is the behaviour on a capture built from
/// the same golden bytes.
/// </para>
/// </remarks>
public sealed class WorldChannelReplayTests
{
    private static readonly IPAddress Server = IPAddress.Parse("79.110.84.175");
    private const int ServerPort = 4002;
    private const string Client = "192.168.0.4";
    private const int ClientPort = 56027;

    /// <summary>The 35 bytes of <see cref="NosTaleWorldDecoderTests"/>: an <c>mv</c> then a <c>stat</c>.</summary>
    private const string GoldenHex =
        "0292899217175D81565155419EFF048C8B9E8B9C1B7491B749158641586414155C8EFF";

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

    /// <summary>A source the replay can open twice, as it must.</summary>
    private static Func<IPacketSource> Recording(params byte[][] bodies)
    {
        var packets = new List<CapturedPacket>();
        uint seq = 1000;
        foreach (byte[] body in bodies)
        {
            packets.Add(new CapturedPacket(new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc), TcpPacket(seq, body)));
            seq += (uint)body.Length;
        }
        return () => new InMemoryPacketSource(Server, ServerPort, packets);
    }

    [Fact]
    public void A_recording_reports_every_vitals_reading_it_contains()
    {
        // Two stat packets in one recording. A runtime keeps only the most recent
        // per poll — it wants the current HP — but this report exists to be held
        // against the HUD, so it must not collapse a series into its last value.
        byte[] golden = Convert.FromHexString(GoldenHex);
        WorldChannelReplaySummary summary = WorldChannelReplay.Replay(Recording(golden, golden));

        Assert.Equal(2, summary.VitalsReadings);
        Assert.Equal(7305, summary.MinHp);
        Assert.Equal(7305, summary.MaxHp);
        Assert.Equal(new[] { 7305 }, summary.MaxHpValues);
        Assert.Equal(1420, summary.MinMp);
    }

    [Fact]
    public void The_census_counts_what_arrived_not_what_was_read()
    {
        // 'guri' is a real opcode whose meaning nobody has established. It must
        // appear as traffic that arrives and is not read — a gap the operator can
        // see — rather than as silence.
        byte[] golden = Convert.FromHexString(GoldenHex);
        WorldChannelReplaySummary summary = WorldChannelReplay.Replay(Recording(golden, Encoded("guri 2 1 3443217 0")));

        Assert.Equal(3, summary.TotalPackets);
        Assert.Equal(2, summary.ReadablePackets);
        Assert.Contains(summary.Opcodes, o => o.Key == "guri" && o.Value == 1);
        Assert.Contains(summary.Opcodes, o => o.Key == "mv" && o.Value == 1);
        Assert.Contains(summary.Opcodes, o => o.Key == "stat" && o.Value == 1);
    }

    /// <summary>
    /// The whole point of replaying rather than capturing: a recording is real
    /// bytes that are no longer current. Nothing read here may come out LIVE,
    /// however confidently it decodes.
    /// </summary>
    [Fact]
    public void A_replay_is_never_live()
    {
        WorldChannelReplaySummary summary =
            WorldChannelReplay.Replay(Recording(Convert.FromHexString(GoldenHex)));

        Assert.Equal(DataSourceKind.Cached, summary.Source);
        Assert.Contains("CACHED", summary.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_recording_reports_no_readings_rather_than_zeroes()
    {
        WorldChannelReplaySummary summary = WorldChannelReplay.Replay(Recording());

        Assert.Equal(0, summary.VitalsReadings);
        Assert.Equal(0, summary.TotalPackets);
        Assert.Contains("non e' passato un 'stat'", summary.Describe(), StringComparison.Ordinal);
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
        bytes.Add(NosAi.Runtime.Perception.Network.NosTaleWorldDecoder.PacketTerminator);
        return bytes.ToArray();
    }

    private static IEnumerable<string> Chunks(string text, int size)
    {
        for (int i = 0; i < text.Length; i += size)
            yield return text.Substring(i, Math.Min(size, text.Length - i));
    }
}
