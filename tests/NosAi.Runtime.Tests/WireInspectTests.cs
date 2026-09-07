using System.Buffers.Binary;
using System.Net;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Observability;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// <c>--wire-inspect</c>: measures what the wire actually carries instead of
/// guessing it, offline and read-only.
/// </summary>
/// <remarks>
/// No driver and no file for the synthetic cases: <see cref="InMemoryPacketSource"/>
/// plays the role it plays for <see cref="LiveWireMonitorTests"/>. The recorded
/// cases run only where the capture exists, via <see cref="RecordedCaptureFactAttribute"/>.
/// </remarks>
public sealed class WireInspectTests
{
    private static readonly IPAddress Server = IPAddress.Parse("79.110.84.175");
    private const int ServerPort = 4002;
    private const string Client = "192.168.0.4";
    private const int ClientPort = 56027;

    // ------------------------------------------------------------------ shapes

    [Fact]
    public void AFieldWithOneValueIsConstantAndAChangingFieldCountsItsDistinctValues()
    {
        using IPacketSource source = Recording(
            Encoded("foo 1 2 3"),
            Encoded("foo 1 20 3"),
            Encoded("foo 1 200 3"));

        IReadOnlyList<WireOpcodeCensus> census = WireInspectCommand.Census(source, DataSourceKind.Cached);
        WireOpcodeCensus foo = Find(census, "foo");

        Assert.Equal(3, foo.Count);
        Assert.Single(foo.Arities);
        Assert.Equal(3, foo.Arities[0]);
        Assert.Equal(3, foo.Fields.Count);
        Assert.Equal("1", foo.Fields[0].ConstantValue);   // position 1 never changes
        Assert.Null(foo.Fields[1].ConstantValue);          // position 2 changes
        Assert.Equal(3, foo.Fields[1].DistinctCount);
        Assert.Equal("3", foo.Fields[2].ConstantValue);    // position 3 never changes
    }

    [Fact]
    public void AnOpcodeWithDifferentAritiesReportsEveryArityNotJustTheLast()
    {
        using IPacketSource source = Recording(
            Encoded("bar 1 2"),
            Encoded("bar 1 2 3 4"));

        WireOpcodeCensus bar = Find(WireInspectCommand.Census(source, DataSourceKind.Cached), "bar");

        Assert.Equal(2, bar.Count);
        Assert.Equal(2, bar.Arities.Count);
        Assert.Equal(2, bar.Arities[0]);
        Assert.Equal(4, bar.Arities[1]);
    }

    [Fact]
    public void MaxTruncatesTheRawLinesAndStopsBeforeTheNextLine()
    {
        using IPacketSource source = Recording(
            Encoded("zz 1"),
            Encoded("zz 2"),
            Encoded("zz 3"),
            Encoded("zz 4"),
            Encoded("zz 5"));

        IReadOnlyList<string> lines = WireInspectCommand.RawLines(source, DataSourceKind.Cached, "zz", maxLines: 3);

        Assert.Equal(3, lines.Count);
        Assert.Equal("zz 1", lines[0]);
        Assert.Equal("zz 3", lines[2]);
        Assert.DoesNotContain("zz 4", lines);
    }

    [Fact]
    public void ANonNumericFieldIsKeptVerbatimRatherThanNormalised()
    {
        using IPacketSource source = Recording(
            Encoded("baz 1 #abc"),
            Encoded("baz 1 #abc"));

        WireOpcodeCensus baz = Find(WireInspectCommand.Census(source, DataSourceKind.Cached), "baz");

        Assert.Equal("#abc", baz.Fields[1].ConstantValue);
    }

    [Fact]
    public void InspectWritesTheCensusForEveryOpcode()
    {
        using IPacketSource source = Recording(
            Encoded("foo 1 2"),
            Encoded("foo 1 3"),
            Encoded("bar 5"));
        var output = new StringWriter();

        WireInspectCommand.Inspect(source, output, DataSourceKind.Cached, opcode: null, maxLines: 20);

        string text = output.ToString();
        Assert.Contains("foo", text, StringComparison.Ordinal);
        Assert.Contains("campi=2", text, StringComparison.Ordinal);
        Assert.Contains("1=1", text, StringComparison.Ordinal);
        Assert.Contains("2:2var", text, StringComparison.Ordinal);
        Assert.Contains("bar", text, StringComparison.Ordinal);
        Assert.Contains("campi=1", text, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------- provenance

    [Fact]
    public void ReadingFromAFileFramesBytesAsCachedNeverLive()
    {
        // The point of the LiveWireMonitor change this task makes: Monitor used
        // to hardcode NosTaleWorldFramer.Factory(DataSourceKind.Live), which
        // would label recorded bytes LIVE. It now threads a DataSourceKind in,
        // and the file-reading path passes Cached. This pins the seam that kind
        // is threaded into: frames cut from a recorded file are Cached, never
        // Live.
        using IPacketSource source = Recording(Encoded("foo 1 2 3"));

        DataSourceKind? seen = null;
        var engine = new GameTrafficCaptureEngine(source, NosTaleWorldFramer.Factory(DataSourceKind.Cached));
        engine.FrameProduced += frame => seen = frame.Frame.Source;
        engine.Run();

        Assert.Equal(DataSourceKind.Cached, seen);
        Assert.NotEqual(DataSourceKind.Live, seen);
    }

    [Fact]
    public void MonitorAcceptsAProvenanceKindForFileReads()
    {
        using IPacketSource source = Recording(Encoded("stat 100 7305 50 500"));
        var output = new StringWriter();

        LiveWireMonitor.Summary summary = LiveWireMonitor.Monitor(source, output, default, DataSourceKind.Cached);

        Assert.Equal(1, summary.ReadableFrames);
        Assert.Contains("CLIENT vitals hp=100/7305 mp=50/500", output.ToString(), StringComparison.Ordinal);
    }

    // ----------------------------------------------------------------- refusals

    [Theory]
    [InlineData("no_such_capture.noscap", WireInspectCommand.MissingFileReason)]
    [InlineData("no_such_capture.txt", WireInspectCommand.BadExtensionReason)]
    [InlineData("x.noscap --max -1", WireInspectCommand.NegativeMaxReason)]
    [InlineData("x.noscap --opcode", WireInspectCommand.OpcodeWithoutValueReason)]
    public void RefusalsAreNamedAndNonZero(string argsText, string expectedReason)
    {
        string[] args = ("--wire-inspect " + argsText).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        string? refusal = WireInspectCommand.Validate(args, out _, out _, out _);
        Assert.Equal(expectedReason, refusal);

        Assert.NotEqual(0, WireInspectCommand.Run(args));
    }

    // --------------------------------------------------------- recorded shapes

    [RecordedCaptureFact("nostale_combat.noscap")]
    public void TheCombatCaptureCensusReportsLevsMeasuredShape()
    {
        using IPacketSource source = CaptureFile.Open(RecordingPath("nostale_combat.noscap"));
        WireOpcodeCensus lev = Find(WireInspectCommand.Census(source, DataSourceKind.Cached), "lev");

        Assert.Equal(23, lev.Count);
        Assert.Single(lev.Arities);
        Assert.Equal(12, lev.Arities[0]);
        Assert.Equal(12, lev.Fields.Count);

        Assert.Equal("56", lev.Fields[0].ConstantValue);       // field 1: level
        Assert.Null(lev.Fields[1].ConstantValue);              // field 2: XP varies
        Assert.Equal(23, lev.Fields[1].DistinctCount);
        Assert.Equal("39", lev.Fields[2].ConstantValue);       // field 3: job level
        Assert.Null(lev.Fields[3].ConstantValue);              // field 4: job XP varies
        Assert.Equal(23, lev.Fields[3].DistinctCount);
        Assert.Equal("18247900", lev.Fields[4].ConstantValue); // field 5
        Assert.Equal("185500", lev.Fields[5].ConstantValue);   // field 6
        Assert.Equal("35106", lev.Fields[6].ConstantValue);    // field 7
        Assert.Equal("7", lev.Fields[7].ConstantValue);        // field 8
        Assert.Equal("0", lev.Fields[8].ConstantValue);        // field 9
        Assert.Equal("0", lev.Fields[9].ConstantValue);        // field 10
        Assert.Equal("1", lev.Fields[10].ConstantValue);       // field 11
        Assert.Equal("0", lev.Fields[11].ConstantValue);       // field 12
    }

    [RecordedCaptureFact("nostale_combat.noscap")]
    public void TheCombatCaptureCensusConfirmsCondsNeverObservedFlags()
    {
        using IPacketSource source = CaptureFile.Open(RecordingPath("nostale_combat.noscap"));
        WireOpcodeCensus cond = Find(WireInspectCommand.Census(source, DataSourceKind.Cached), "cond");

        Assert.Equal(72, cond.Count);
        Assert.Equal("0", cond.Fields[2].ConstantValue); // field 3, never observed asserted
        Assert.Equal("0", cond.Fields[3].ConstantValue); // field 4, never observed asserted
    }

    [RecordedCaptureFact("nostale_combat.noscap")]
    public void TheRawLinesOfLevRespectMaxAndPrintTheDecodedLineVerbatim()
    {
        using IPacketSource source = CaptureFile.Open(RecordingPath("nostale_combat.noscap"));
        IReadOnlyList<string> lines = WireInspectCommand.RawLines(source, DataSourceKind.Cached, "lev", maxLines: 5);

        Assert.Equal(5, lines.Count);
        Assert.Equal("lev 56 9688533 39 43226 18247900 185500 35106 7 0 0 1 0", lines[0]);
    }

    // ------------------------------------------------------------------ wiring

    [Fact]
    public void TheRuntimeWiresTheWireInspectFlag()
    {
        string program = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "NosAi.Runtime", "Program.cs"));
        Assert.Contains("WireInspectCommand.Run", program, StringComparison.Ordinal);
        Assert.Contains("WireInspectCommand.Flag", program, StringComparison.Ordinal);
        Assert.Contains("\"--wire-inspect\"", program, StringComparison.Ordinal);
    }

    // ----------------------------------------------------------------- helpers

    private static WireOpcodeCensus Find(IReadOnlyList<WireOpcodeCensus> census, string opcode)
        => census.Single(o => o.Opcode == opcode);

    private static IPacketSource Recording(params byte[][] bodies)
    {
        var packets = new List<CapturedPacket>();
        uint seq = 1000;
        var when = new DateTime(2026, 9, 6, 10, 0, 0, DateTimeKind.Utc);
        foreach (byte[] body in bodies)
        {
            packets.Add(new CapturedPacket(when, TcpPacket(seq, body)));
            seq += (uint)body.Length;
        }
        return new InMemoryPacketSource(Server, ServerPort, packets);
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

    /// <summary>Encodes a line the way the server does: literal branch, complemented, terminator.</summary>
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

    private static string RecordingPath(string name)
        => RecordedCaptureFactAttribute.Resolve(name)!;

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
