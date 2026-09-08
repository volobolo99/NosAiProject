using System.Buffers.Binary;
using System.Linq;
using System.Net;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Observability;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// <c>--wire-inspect --timeline</c>: packets in capture order, with their
/// progressive number and instant, optionally restricted to a set of opcodes.
/// </summary>
public sealed class WireInspectTimelineTests
{
    private static readonly IPAddress Server = IPAddress.Parse("79.110.84.175");
    private const int ServerPort = 4002;
    private const string Client = "192.168.0.4";
    private const int ClientPort = 56027;

    [Fact]
    public void Timeline_preserves_capture_order_and_filters_by_opcode()
    {
        IReadOnlyList<WireTimelineEntry> all = WireInspectCommand.Timeline(
            Recording(
                ("out 3 3013", 0),
                ("mv 3 100 10 10 5", 1),
                ("out 3 3012", 2),
                ("in 3 45 3205 110 62 2 100 100", 3)),
            DataSourceKind.Cached, opcodes: null, maxLines: 0);

        Assert.Equal(4, all.Count);
        Assert.Equal(new long[] { 1, 2, 3, 4 }, all.Select(e => e.Ordinal).ToArray());
        Assert.Equal("out 3 3013", all[0].Line);
        Assert.Equal("mv 3 100 10 10 5", all[1].Line);
        Assert.Equal("out 3 3012", all[2].Line);
        Assert.Equal("in 3 45 3205 110 62 2 100 100", all[3].Line);
        // The instant is the packet's own capture time, not a run-time clock.
        Assert.Equal(new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc), all[0].TimestampUtc);
        Assert.Equal(new DateTime(2026, 9, 8, 12, 0, 1, DateTimeKind.Utc), all[1].TimestampUtc);

        // Restricted to `out`: the two exits keep their true positions (1 and 3),
        // revealing the gap the filter dropped rather than renumbering them.
        IReadOnlyList<WireTimelineEntry> outs = WireInspectCommand.Timeline(
            Recording(
                ("out 3 3013", 0),
                ("mv 3 100 10 10 5", 1),
                ("out 3 3012", 2),
                ("in 3 45 3205 110 62 2 100 100", 3)),
            DataSourceKind.Cached, new HashSet<string>(StringComparer.Ordinal) { "out" }, maxLines: 0);

        Assert.Equal(2, outs.Count);
        Assert.Equal(1, outs[0].Ordinal);
        Assert.Equal(3, outs[1].Ordinal);
        Assert.All(outs, e => Assert.StartsWith("out ", e.Line, StringComparison.Ordinal));
    }

    [Fact]
    public void Max_caps_the_timeline()
    {
        IReadOnlyList<WireTimelineEntry> capped = WireInspectCommand.Timeline(
            Recording(("out 3 3013", 0), ("out 3 3012", 1), ("out 3 3003", 2)),
            DataSourceKind.Cached, opcodes: null, maxLines: 2);

        Assert.Equal(2, capped.Count);
    }

    [Theory]
    [InlineData("x.noscap --summary")]
    [InlineData("x.noscap --timeline --bogus")]
    public void An_unknown_option_is_refused(string argsText)
    {
        string[] args = ("--wire-inspect " + argsText).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        string? refusal = WireInspectCommand.Validate(args, out _, out _, out _, out _, out _, out _);

        Assert.StartsWith(WireInspectCommand.UnknownOptionReason, refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void Timeline_parses_its_opcode_filter()
    {
        string file = TempNoscap();
        try
        {
            string[] args = $"--wire-inspect {file} --timeline out,mv".Split(' ', StringSplitOptions.RemoveEmptyEntries);

            string? refusal = WireInspectCommand.Validate(
                args, out _, out _, out _, out _, out bool timeline, out IReadOnlySet<string>? opcodes);

            Assert.Null(refusal);
            Assert.True(timeline);
            Assert.Equal(new HashSet<string>(StringComparer.Ordinal) { "out", "mv" }, opcodes);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Timeline_without_a_value_means_every_opcode()
    {
        string file = TempNoscap();
        try
        {
            string[] args = $"--wire-inspect {file} --timeline".Split(' ', StringSplitOptions.RemoveEmptyEntries);

            string? refusal = WireInspectCommand.Validate(
                args, out _, out _, out _, out _, out bool timeline, out IReadOnlySet<string>? opcodes);

            Assert.Null(refusal);
            Assert.True(timeline);
            Assert.Null(opcodes);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void A_timeline_filter_that_names_no_opcode_is_refused()
    {
        string file = TempNoscap();
        try
        {
            string[] args = $"--wire-inspect {file} --timeline ,,,".Split(' ', StringSplitOptions.RemoveEmptyEntries);

            string? refusal = WireInspectCommand.Validate(
                args, out _, out _, out _, out _, out _, out _);

            Assert.Equal(WireInspectCommand.TimelineWithoutValueReason, refusal);
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static string TempNoscap()
    {
        string path = Path.Combine(Path.GetTempPath(), "nosai-timeline-" + Guid.NewGuid().ToString("N") + ".noscap");
        File.WriteAllBytes(path, Array.Empty<byte>());
        return path;
    }

    // ---------------------------------------------------------------- helpers

    private static IPacketSource Recording(params (string Line, int OffsetSeconds)[] items)
    {
        var packets = new List<CapturedPacket>();
        uint seq = 1000;
        var start = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
        foreach ((string line, int offset) in items)
        {
            byte[] body = Encoded(line);
            packets.Add(new CapturedPacket(start.AddSeconds(offset), TcpPacket(seq, body)));
            seq += (uint)body.Length;
        }
        return new InMemoryPacketSource(Server, ServerPort, packets);
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
}
