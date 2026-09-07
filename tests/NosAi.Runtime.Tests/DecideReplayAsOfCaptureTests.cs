using System.Net;
using System.Text;
using NosAi.LiveIntegration;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate1;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.Observability;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// <c>--decide-replay --as-of-capture</c>: the freshness rule judged against the
/// recording's own timestamps instead of the system clock, and the report that
/// says which staleness it is looking at.
/// </summary>
public sealed class DecideReplayAsOfCaptureTests : IDisposable
{
    private const string CombatRecording = "nostale_combat.noscap";

    private static readonly IPAddress Server = IPAddress.Parse("79.110.84.175");
    private const int ServerPort = 4002;
    private static readonly GameEndpoint Endpoint = new("79.110.84.175", 4002);
    private static readonly DateTime Start = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "nosai-decide-replay-asof-" + Guid.NewGuid().ToString("N"));

    public DecideReplayAsOfCaptureTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    // ------------------------------------------------------------ the clock

    [Fact]
    public void CaptureClock_answers_with_the_first_packet_instant_before_any_packet()
    {
        var first = new DateTimeOffset(Start, TimeSpan.Zero);
        var clock = new CaptureClock(first);

        // Before any packet the clock answers with the first packet's instant,
        // not with the system clock.
        Assert.Equal(first, clock.GetUtcNow());
    }

    [Fact]
    public void CaptureClock_advances_with_each_packet_and_never_backwards()
    {
        var clock = new CaptureClock(new DateTimeOffset(Start, TimeSpan.Zero));

        clock.Advance(new DateTimeOffset(Start.AddSeconds(10), TimeSpan.Zero));
        Assert.Equal(Start.AddSeconds(10), clock.GetUtcNow().UtcDateTime);

        // An out-of-order timestamp must not move it backwards.
        clock.Advance(new DateTimeOffset(Start.AddSeconds(5), TimeSpan.Zero));
        Assert.Equal(Start.AddSeconds(10), clock.GetUtcNow().UtcDateTime);

        clock.Advance(new DateTimeOffset(Start.AddSeconds(15), TimeSpan.Zero));
        Assert.Equal(Start.AddSeconds(15), clock.GetUtcNow().UtcDateTime);
    }

    // ------------------------------------------------------------ freshness

    [Fact]
    public void Two_stats_within_the_vitals_age_are_fresh_as_of_capture_and_stale_against_the_system_clock()
    {
        // As-of-capture: the clock follows the wire, so a one-second gap keeps
        // the remembered reading fresh.
        var captureClock = new CaptureClock(new DateTimeOffset(Start, TimeSpan.Zero));
        (ScriptedSource captureWire, NetworkGameplayProvider captureProvider) = Chain(captureClock);
        captureWire.Send("stat 7305 7305 1420 1420 0 1184", Start);
        captureProvider.Observe();
        captureClock.Advance(new DateTimeOffset(Start.AddSeconds(1), TimeSpan.Zero));
        captureWire.Send("mv 3 3194 121 110 5", Start.AddSeconds(1));

        GameplayObservation fresh = captureProvider.Observe();
        Assert.True(fresh.HasVitals);
        Assert.Equal(DataSourceKind.Cached, fresh.Hp.Source);

        // System clock: the same gap is judged two days later and is stale.
        var systemClock = new StubClock(new DateTimeOffset(Start.AddDays(2), TimeSpan.Zero));
        (ScriptedSource systemWire, NetworkGameplayProvider systemProvider) = Chain(systemClock);
        systemWire.Send("stat 7305 7305 1420 1420 0 1184", Start);
        systemProvider.Observe();
        systemWire.Send("mv 3 3194 121 110 5", Start.AddSeconds(1));

        GameplayObservation stale = systemProvider.Observe();
        Assert.False(stale.HasVitals);
        Assert.Equal("player_vitals_stale", stale.UnusableReason);
    }

    [Fact]
    public void Two_stats_beyond_the_vitals_age_are_stale_in_both_modes()
    {
        // As-of-capture: the wire itself went quiet longer than MaxVitalsAge (5 s).
        var captureClock = new CaptureClock(new DateTimeOffset(Start, TimeSpan.Zero));
        (ScriptedSource captureWire, NetworkGameplayProvider captureProvider) = Chain(captureClock);
        captureWire.Send("stat 7305 7305 1420 1420 0 1184", Start);
        captureProvider.Observe();
        captureClock.Advance(new DateTimeOffset(Start.AddSeconds(6), TimeSpan.Zero));
        captureWire.Send("mv 3 3194 121 110 5", Start.AddSeconds(6));

        GameplayObservation captureStale = captureProvider.Observe();
        Assert.False(captureStale.HasVitals);
        Assert.Equal("player_vitals_stale", captureStale.UnusableReason);

        // System clock: the same gap is stale too, for the same structural reason.
        var systemClock = new StubClock(new DateTimeOffset(Start.AddDays(2), TimeSpan.Zero));
        (ScriptedSource systemWire, NetworkGameplayProvider systemProvider) = Chain(systemClock);
        systemWire.Send("stat 7305 7305 1420 1420 0 1184", Start);
        systemProvider.Observe();
        systemWire.Send("mv 3 3194 121 110 5", Start.AddSeconds(6));

        GameplayObservation systemStale = systemProvider.Observe();
        Assert.False(systemStale.HasVitals);
        Assert.Equal("player_vitals_stale", systemStale.UnusableReason);
    }

    // --------------------------------------------------------- rejections

    [Fact]
    public async Task As_of_capture_without_a_file_is_rejected_by_name()
    {
        string path = Path.Combine(_dir, "absent.noscap");
        DecideReplayReport report = await DecideReplayCommand.InspectFileAsync(path, asOfCapture: true);

        Assert.False(report.Ok);
        Assert.Equal("as_of_capture_requires_a_recording_file", report.FailureReason);
    }

    [Fact]
    public async Task Existing_rejections_are_unchanged()
    {
        // Absent, without the flag, keeps the existing reason.
        DecideReplayReport absent = await DecideReplayCommand.InspectFileAsync(Path.Combine(_dir, "absent.noscap"));
        Assert.False(absent.Ok);
        Assert.Equal("recording_not_found", absent.FailureReason);

        // Junk still reports unreadable.
        string junk = Path.Combine(_dir, "junk.noscap");
        File.WriteAllText(junk, "not a noscap");
        DecideReplayReport junkReport = await DecideReplayCommand.InspectFileAsync(junk);
        Assert.False(junkReport.Ok);
        Assert.StartsWith("recording_unreadable:", junkReport.FailureReason, StringComparison.Ordinal);

        // Empty stays a successful diagnosis, with and without the flag.
        string empty = Path.Combine(_dir, "empty.noscap");
        using (var source = new InMemoryPacketSource(Server, ServerPort, Array.Empty<CapturedPacket>()))
            CaptureFile.Record(source, empty);

        Assert.True((await DecideReplayCommand.InspectFileAsync(empty)).Ok);
        Assert.True((await DecideReplayCommand.InspectFileAsync(empty, asOfCapture: true)).Ok);
    }

    // ------------------------------------------------------- real recording

    [RecordedCaptureFact(CombatRecording)]
    public async Task Without_as_of_capture_the_result_is_unchanged_and_says_why()
    {
        string path = RecordedCaptureFactAttribute.Resolve(CombatRecording)!;
        DecideReplayReport report = await DecideReplayCommand.InspectFileAsync(path, asOfCapture: false);

        Assert.True(report.Ok);
        // Unchanged from before this change: nothing is ever planned, and the
        // structural staleness that stops the remaining cycles is present.
        Assert.All(report.Cycles, row => Assert.NotEqual(DecideReplayCommand.Passed, row.Plan));
        Assert.Contains(report.CountsByReason, e => e.Key.Contains("player_vitals_stale", StringComparison.Ordinal));

        // And the report now says why: the staleness is structural (old capture).
        string text = DecideReplayCommand.Format(report);
        Assert.Contains("structural", text, StringComparison.Ordinal);
        Assert.Contains("seconds old", text, StringComparison.Ordinal);
    }

    [RecordedCaptureFact(CombatRecording)]
    public async Task As_of_capture_reaches_the_planner()
    {
        string path = RecordedCaptureFactAttribute.Resolve(CombatRecording)!;
        DecideReplayReport report = await DecideReplayCommand.InspectFileAsync(path, asOfCapture: true);

        Assert.True(report.Ok);
        // At least one cycle passes NoWorldState: the replay reaches planning.
        Assert.Contains(report.Cycles, row => row.Outcome != CycleOutcome.NoWorldState);
    }

    [RecordedCaptureFact(CombatRecording)]
    public async Task As_of_capture_keeps_every_reading_cached_and_acting_disabled()
    {
        string path = RecordedCaptureFactAttribute.Resolve(CombatRecording)!;
        using IPacketSource packets = CaptureFile.Open(path);
        var endpoint = new GameEndpoint(packets.ServerAddress.ToString(), packets.ServerPort);

        // The report rows carry the stage, not the per-reading provenance, so the
        // invariant "every reading stays CACHED" is pinned by driving the channel
        // with the capture clock directly. The clock value does not matter here:
        // it is the provenance under test, and the clock never changes it.
        using Gate1ObservationChannel channel = Gate1ObservationChannel.FromPackets(
            packets, endpoint, DataSourceKind.Cached, clock: new CaptureClock(DateTimeOffset.UnixEpoch));
        Assert.NotNull(channel.Provider);

        await using var loop = new Gate3DecisionLoop(
            new GameplayProviderWorldStateSource(channel.Provider!),
            new Gate3ExecutionOrchestrator(),
            new NullRuntimeLogger());

        bool sawReading = false;
        for (var i = 0; i < 20; i++)
        {
            Gate3LoopCycle cycle = await loop.RunOnceAsync();
            if (cycle.Hp.HasValue)
            {
                sawReading = true;
                Assert.Equal(DataSourceKind.Cached, cycle.Hp.Source);
            }
        }

        Assert.True(sawReading);
        Assert.False(loop.ActingEnabled);
    }

    // ------------------------------------------------------------- helpers

    private static (ScriptedSource Wire, NetworkGameplayProvider Provider) Chain(TimeProvider clock)
    {
        var wire = new ScriptedSource(DataSourceKind.Cached);
        var feed = new NetworkWorldFeed(new GameTrafficObserver(
            wire, new ScopedGameTrafficFilter(Endpoint), new NosTaleWorldProtocolDecoder()));
        return (wire, new NetworkGameplayProvider(feed, clock));
    }

    /// <summary>A channel the test feeds one printable packet at a time.</summary>
    private sealed class ScriptedSource : INetworkObservationSource
    {
        private readonly Queue<ObservedPacket> _packets = new();
        public DataSourceKind Source { get; }

        public ScriptedSource(DataSourceKind source) => Source = source;

        public void Send(string line, DateTime capturedUtc) => _packets.Enqueue(new ObservedPacket(
            capturedUtc, NetworkDirection.Inbound, Endpoint.Host, Endpoint.Port,
            Encoding.ASCII.GetBytes(line), Source));

        public bool TryObserve(out ObservedPacket packet)
        {
            if (_packets.Count == 0) { packet = null!; return false; }
            packet = _packets.Dequeue();
            return true;
        }
    }

    /// <summary>A fixed clock standing in for the system clock on a later day.</summary>
    private sealed class StubClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NullRuntimeLogger : IRuntimeLogger
    {
        public void Info(string message, IReadOnlyDictionary<string, object?>? properties = null) { }
        public void Warning(string message, IReadOnlyDictionary<string, object?>? properties = null) { }
        public void Error(string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? properties = null) { }
    }
}
