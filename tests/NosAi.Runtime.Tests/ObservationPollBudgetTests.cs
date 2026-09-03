using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// One poll of the observation source has to end, whatever the wire does.
/// </summary>
/// <remarks>
/// <para>
/// It did not. <c>TryObserve</c> pumped until a frame was ready or the read
/// returned false, and neither is guaranteed: a scoped filter admits traffic the
/// framer discards, and reassembly waits for a segment that may not come. Packets
/// arriving without completing a frame kept the loop alive with nothing to show.
/// </para>
/// <para>
/// The operator API is where that surfaced. Every JSON route takes a snapshot,
/// every snapshot observes gameplay, and gameplay ends up here — so
/// <c>/api/gate1</c>, <c>/api/state</c>, <c>/api/telemetry</c> and
/// <c>/api/health</c> all hung while the dashboard, which takes no snapshot,
/// answered normally. Two independent HTTP clients timed out against a live
/// runtime before this was written.
/// </para>
/// </remarks>
public sealed class ObservationPollBudgetTests
{
    private static readonly IPAddress Server = IPAddress.Parse("10.20.30.40");
    private const int ServerPort = 4002;
    private const string Client = "10.0.0.9";
    private const ushort ClientPort = 51000;

    /// <summary>
    /// A well-formed packet from a conversation this source is not watching.
    /// </summary>
    /// <remarks>
    /// The parser rejects it, so it never reaches the framer and never completes
    /// a frame — which is exactly the traffic that used to keep the loop running.
    /// </remarks>
    private static byte[] Stranger()
    {
        var raw = new byte[20 + 20 + 4];
        raw[0] = 0x45;
        BinaryPrimitives.WriteUInt16BigEndian(raw.AsSpan(2, 2), (ushort)raw.Length);
        raw[9] = 6;
        IPAddress.Parse("8.8.8.8").GetAddressBytes().CopyTo(raw, 12);
        IPAddress.Parse(Client).GetAddressBytes().CopyTo(raw, 16);
        BinaryPrimitives.WriteUInt16BigEndian(raw.AsSpan(20, 2), 53);
        BinaryPrimitives.WriteUInt16BigEndian(raw.AsSpan(22, 2), ClientPort);
        raw[32] = 5 << 4;
        return raw;
    }

    /// <summary>A source that always has another packet, and never ends.</summary>
    /// <remarks>
    /// A live wire behaves like this: it does not run out, and it owes nobody a
    /// frame. <see cref="InMemoryPacketSource"/> cannot stand in here because it
    /// is finite, and running out is the one thing that used to stop the loop.
    /// </remarks>
    private sealed class EndlessSource : IPacketSource
    {
        public long Reads;

        public IPAddress ServerAddress => Server;
        public int ServerPort => ObservationPollBudgetTests.ServerPort;

        public bool TryRead(TimeSpan timeout, out CapturedPacket packet)
        {
            Reads++;
            packet = new CapturedPacket(DateTime.UtcNow, Stranger());
            return true;
        }

        public void Dispose()
        {
        }
    }

    [Fact]
    public void APollThatNeverCompletesAFrameGivesUpInsteadOfRunningForever()
    {
        var wire = new EndlessSource();
        using var source = new ReassembledObservationSource(
            wire,
            NosTaleWorldFramer.Factory(DataSourceKind.Live),
            DataSourceKind.Live,
            TimeSpan.FromMilliseconds(150));

        var clock = Stopwatch.StartNew();
        bool observed = source.TryObserve(out _);
        clock.Stop();

        Assert.False(observed);

        // Generous on purpose: the assertion is that it terminates at all. Before
        // the budget check this test would not have failed, it would have hung,
        // and a hung suite says nothing about what is wrong.
        Assert.True(
            clock.Elapsed < TimeSpan.FromSeconds(10),
            $"one poll took {clock.Elapsed}, so the budget is not bounding it");

        // It really did pump: the loop ran and gave up, rather than never starting.
        Assert.True(wire.Reads > 0, "the source was never read, so nothing was bounded");
    }

    [Fact]
    public void TheBudgetIsSpentPumpingRatherThanReturnedImmediately()
    {
        // A poll that gave up before trying would also terminate, and would be
        // useless. The budget is a ceiling on work done, not a reason to skip it.
        var wire = new EndlessSource();
        using var source = new ReassembledObservationSource(
            wire,
            NosTaleWorldFramer.Factory(DataSourceKind.Live),
            DataSourceKind.Live,
            TimeSpan.FromMilliseconds(120));

        source.TryObserve(out _);

        Assert.True(wire.Reads > 1, $"only {wire.Reads} read(s) before giving up");
    }
}
