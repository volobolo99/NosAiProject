using System.Buffers.Binary;
using System.Net;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.Observability;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// <c>--decide-replay</c>: the plan / safety / execution / verify scale, offline.
/// </summary>
public sealed class DecideReplayCommandTests : IDisposable
{
    private static readonly IPAddress Server = IPAddress.Parse("79.110.84.175");
    private const int ServerPort = 4002;
    private const string Client = "192.168.0.4";
    private const int ClientPort = 56027;

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "nosai-decide-replay-" + Guid.NewGuid().ToString("N"));

    public DecideReplayCommandTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void TheRuntimeWiresTheDecideReplayFlag()
    {
        string program = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "NosAi.Runtime", "Program.cs"));
        Assert.Contains("DecideReplayCommand.RunAsync", program, StringComparison.Ordinal);
        Assert.Contains("\"--decide-replay\"", program, StringComparison.Ordinal);
        Assert.Contains("--decide-cycles", program, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEmptyRecordingPrintsTheScaleAndExitsZero()
    {
        string path = Path.Combine(_dir, "empty.noscap");
        using (var source = new InMemoryPacketSource(Server, ServerPort, Array.Empty<CapturedPacket>()))
            CaptureFile.Record(source, path);

        DecideReplayReport report = await DecideReplayCommand.InspectFileAsync(path, maxCycles: 8);
        string text = DecideReplayCommand.Format(report);

        Assert.True(report.Ok);
        Assert.NotEmpty(report.Cycles);
        Assert.All(report.Cycles, row =>
        {
            Assert.Equal(CycleOutcome.NoWorldState, row.Outcome);
            Assert.Equal(DecideReplayCommand.NotEvaluated, row.Plan);
            Assert.Equal(DecideReplayCommand.NotEvaluated, row.Safety);
            Assert.Equal(DecideReplayCommand.NotEvaluated, row.Execution);
            Assert.Equal(DecideReplayCommand.NotEvaluated, row.Verify);
            Assert.False(string.IsNullOrWhiteSpace(row.Summary));
        });
        Assert.Contains("plan:", text, StringComparison.Ordinal);
        Assert.Contains("safety:", text, StringComparison.Ordinal);
        Assert.Contains("execution:", text, StringComparison.Ordinal);
        Assert.Contains("verify:", text, StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", text, StringComparison.Ordinal);
        Assert.Contains("counts by reason:", text, StringComparison.Ordinal);
        Assert.Contains("stopped:", text, StringComparison.Ordinal);
        Assert.Equal(0, await DecideReplayCommand.RunAsync(path, maxCycles: 8));
    }

    [Fact]
    public async Task AMissingFileIsNonZero()
    {
        string path = Path.Combine(_dir, "absent.noscap");
        DecideReplayReport report = await DecideReplayCommand.InspectFileAsync(path);
        Assert.False(report.Ok);
        Assert.Equal("recording_not_found", report.FailureReason);
        Assert.Equal(DecideReplayCommand.ExitUnreadable, await DecideReplayCommand.RunAsync(path));
    }

    [Fact]
    public async Task AnUnreadableFileIsNonZero()
    {
        string path = Path.Combine(_dir, "junk.noscap");
        File.WriteAllText(path, "not a noscap");
        DecideReplayReport report = await DecideReplayCommand.InspectFileAsync(path);
        Assert.False(report.Ok);
        Assert.StartsWith("recording_unreadable:", report.FailureReason, StringComparison.Ordinal);
        Assert.Equal(DecideReplayCommand.ExitUnreadable, await DecideReplayCommand.RunAsync(path));
    }

    [Fact]
    public void ScaleMatchesTheOrchestratorsExistingStops()
    {
        Assert.Equal(
            (DecideReplayCommand.Refused, DecideReplayCommand.NotEvaluated,
                DecideReplayCommand.NotEvaluated, DecideReplayCommand.NotEvaluated),
            DecideReplayCommand.Scale(CycleOutcome.NoCandidate));
        Assert.Equal(
            (DecideReplayCommand.Passed, DecideReplayCommand.Refused,
                DecideReplayCommand.NotEvaluated, DecideReplayCommand.NotEvaluated),
            DecideReplayCommand.Scale(CycleOutcome.Blocked));
        Assert.Equal(
            (DecideReplayCommand.Passed, DecideReplayCommand.Passed,
                DecideReplayCommand.Refused, DecideReplayCommand.NotEvaluated),
            DecideReplayCommand.Scale(CycleOutcome.ExecutionDisabled));
        Assert.Equal(
            (DecideReplayCommand.Passed, DecideReplayCommand.Passed,
                DecideReplayCommand.Passed, DecideReplayCommand.Refused),
            DecideReplayCommand.Scale(CycleOutcome.Unverified));
        Assert.Equal(
            (DecideReplayCommand.Passed, DecideReplayCommand.Passed,
                DecideReplayCommand.Passed, DecideReplayCommand.Passed),
            DecideReplayCommand.Scale(CycleOutcome.Confirmed));
        Assert.Equal(
            (DecideReplayCommand.NotEvaluated, DecideReplayCommand.NotEvaluated,
                DecideReplayCommand.NotEvaluated, DecideReplayCommand.NotEvaluated),
            DecideReplayCommand.Scale(CycleOutcome.NoWorldState));
    }

    [Fact]
    public async Task AReadableRecordingWithVitalsStillPrintsTheExactStop()
    {
        string path = Path.Combine(_dir, "stat.noscap");
        WriteCapture(path, Encoded("stat 100 200 50 80"));

        DecideReplayReport report = await DecideReplayCommand.InspectFileAsync(path, maxCycles: 8);
        string text = DecideReplayCommand.Format(report);

        Assert.True(report.Ok);
        Assert.NotEmpty(report.Cycles);
        Assert.Contains("stopped:", text, StringComparison.Ordinal);
        Assert.NotEmpty(report.CountsByReason);
        Assert.All(report.CountsByReason, e => Assert.False(string.IsNullOrWhiteSpace(e.Key)));
    }

    private void WriteCapture(string path, params byte[][] bodies)
    {
        var packets = new List<CapturedPacket>();
        uint seq = 1000;
        foreach (byte[] body in bodies)
        {
            packets.Add(new CapturedPacket(new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc), TcpPacket(seq, body)));
            seq += (uint)body.Length;
        }

        using var source = new InMemoryPacketSource(Server, ServerPort, packets);
        CaptureFile.Record(source, path);
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
            yield return text.Substring(i, Math.Min(size, text.Length));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        Assert.True(directory is not null, "Repository root not found.");
        return directory!.FullName;
    }
}
