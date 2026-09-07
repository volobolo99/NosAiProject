using System.Globalization;
using System.Net;
using System.Text;
using NosAi.LiveIntegration;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate1;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.Perception.Network;

namespace NosAi.Runtime.Observability;

/// <summary>One printed cycle of <c>--decide-replay</c>.</summary>
/// <param name="Index">1-based index among cycles that ran.</param>
/// <param name="Outcome"><see cref="CycleOutcome"/> the orchestrator already chose.</param>
/// <param name="Summary">The orchestrator's exact stop text. Not rewritten.</param>
/// <param name="Action"><see cref="Gate3LoopCycle.SelectedAction"/>.</param>
/// <param name="Plan">Passed, Refused, or NotEvaluated.</param>
/// <param name="Safety">Passed, Refused, or NotEvaluated.</param>
/// <param name="Execution">Passed, Refused, or NotEvaluated.</param>
/// <param name="Verify">Passed, Refused, or NotEvaluated.</param>
public readonly record struct DecideReplayCycleRow(
    int Index,
    CycleOutcome Outcome,
    string Summary,
    ActionType Action,
    string Plan,
    string Safety,
    string Execution,
    string Verify);

/// <summary>The offline decision dump.</summary>
/// <param name="AsOfCapture">
/// Whether freshness was judged against the recording's own timestamps
/// (<c>--as-of-capture</c>) rather than the system clock.
/// </param>
/// <param name="LastVitalsAtUtc">
/// The wire time of the last distinct vitals reading, or null when the recording
/// carried none.
/// </param>
/// <param name="FreshnessJudgedAtUtc">
/// The "now" the freshness rule used: the system clock by default, the recording's
/// last timestamp under <c>--as-of-capture</c>.
/// </param>
public sealed record DecideReplayReport(
    string Path,
    IReadOnlyList<DecideReplayCycleRow> Cycles,
    IReadOnlyList<KeyValuePair<string, int>> CountsByReason,
    IReadOnlyList<KeyValuePair<CycleOutcome, int>> CountsByOutcome,
    bool ActingEnabled,
    string? FailureReason,
    bool AsOfCapture,
    DateTime? LastVitalsAtUtc,
    DateTime FreshnessJudgedAtUtc)
{
    public bool Ok => FailureReason is null;
}

/// <summary>
/// Prints the plan / safety / execution / verify scale of every decision cycle
/// over a recording, with the orchestrator's own stop text (CLI <c>--decide-replay</c>).
/// </summary>
/// <remarks>
/// <para>
/// It does not choose a threshold, a refusal, or an authorisation rule. Those
/// stay in <see cref="Gate3ExecutionOrchestrator"/>. This command only names
/// which of the four stages that existing outcome reached, and reprints
/// <see cref="Gate3LoopCycle.Summary"/> as the reason.
/// </para>
/// <para>
/// Idle exhaustion uses the same five empty polls
/// <see cref="Gate3ReplayProbe"/> already waits for; it is not a new bound.
/// </para>
/// </remarks>
public static class DecideReplayCommand
{
    /// <summary>The operator flag.</summary>
    public const string Flag = "--decide-replay";

    /// <summary>Sub-flag of <c>--decide-replay</c>: judge freshness on the recording's own timestamps.</summary>
    public const string AsOfCaptureFlag = "--as-of-capture";

    /// <summary>Same value as <see cref="Gate3ReplayProbe"/>'s private idle bound.</summary>
    public const int IdleCyclesBeforeExhausted = 5;

    /// <summary>Default cycle cap, same as the existing probe.</summary>
    public const int DefaultMaxCycles = 200;

    /// <summary>Missing or unreadable recording.</summary>
    public const int ExitUnreadable = 2;

    public const string Passed = "Passed";
    public const string Refused = "Refused";
    public const string NotEvaluated = "NotEvaluated";

    /// <summary>Console entry.</summary>
    public static async Task<int> RunAsync(string path, int maxCycles = DefaultMaxCycles)
    {
        // A sub-flag of an existing command, read here rather than in Program.cs:
        // it is accepted only by the path that opens a .noscap, so no live path
        // can ever receive it.
        bool asOfCapture = Environment.GetCommandLineArgs().Any(a =>
            string.Equals(a, AsOfCaptureFlag, StringComparison.OrdinalIgnoreCase));

        DecideReplayReport report = await InspectFileAsync(path, maxCycles, asOfCapture).ConfigureAwait(false);
        Console.Write(Format(report));
        if (!report.Ok)
        {
            Console.Error.WriteLine($"Recording not readable: {path}");
            Console.Error.WriteLine($"Usage: {Flag} <file.noscap> [--decide-cycles N] [{AsOfCaptureFlag}]");
            return ExitUnreadable;
        }

        return 0;
    }

    /// <summary>Reads a path. Zero cycles printed is a successful diagnosis, not a fault.</summary>
    public static async Task<DecideReplayReport> InspectFileAsync(
        string path, int maxCycles = DefaultMaxCycles, bool asOfCapture = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            // --as-of-capture only means anything over a real recording; without one
            // the flag has nothing to judge against, and saying so by name beats
            // reusing the plain "recording_not_found" that already has its own case.
            return Failed(path, asOfCapture
                ? "as_of_capture_requires_a_recording_file"
                : "recording_not_found");
        }

        try
        {
            using IPacketSource packets = CaptureFile.Open(path);
            return await InspectAsync(path, packets, maxCycles, asOfCapture).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return Failed(path, $"recording_unreadable:{ex.GetType().Name}");
        }
    }

    /// <summary>Drives the existing loop over an already-open source.</summary>
    public static async Task<DecideReplayReport> InspectAsync(
        string path, IPacketSource packets, int maxCycles = DefaultMaxCycles, bool asOfCapture = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(packets);
        if (maxCycles < 1)
            maxCycles = 1;

        CaptureClock? captureClock = null;
        if (asOfCapture)
        {
            // The seed is the first packet's instant and every later packet advances
            // the clock as it is read, so the provider's "now" follows the wire.
            var wrapped = new CaptureClockPacketSource(packets);
            captureClock = wrapped.Clock;
            packets = wrapped;
        }

        var endpoint = new GameEndpoint(packets.ServerAddress.ToString(), packets.ServerPort);
        using Gate1ObservationChannel channel =
            Gate1ObservationChannel.FromPackets(packets, endpoint, DataSourceKind.Cached, clock: captureClock);
        if (channel.Provider is null)
            return Failed(path, channel.FailureReason ?? "observation_chain_uncomposed");

        await using var loop = new Gate3DecisionLoop(
            new GameplayProviderWorldStateSource(channel.Provider),
            new Gate3ExecutionOrchestrator(),
            new DiscardingRuntimeLogger());

        var rows = new List<DecideReplayCycleRow>();
        var byReason = new Dictionary<string, int>(StringComparer.Ordinal);
        var byOutcome = new Dictionary<CycleOutcome, int>();
        DateTime? lastReadingAt = null;
        var idleCycles = 0;

        for (var i = 0; i < maxCycles; i++)
        {
            Gate3LoopCycle cycle = await loop.RunOnceAsync().ConfigureAwait(false);
            (string plan, string safety, string execution, string verify) = Scale(cycle.Outcome);
            rows.Add(new DecideReplayCycleRow(
                i + 1, cycle.Outcome, cycle.Summary, cycle.SelectedAction,
                plan, safety, execution, verify));

            byReason[cycle.Summary] = byReason.GetValueOrDefault(cycle.Summary) + 1;
            byOutcome[cycle.Outcome] = byOutcome.GetValueOrDefault(cycle.Outcome) + 1;

            bool newReading = cycle.Hp.HasValue && cycle.Hp.ObservedAtUtc != lastReadingAt;
            if (newReading)
            {
                lastReadingAt = cycle.Hp.ObservedAtUtc;
                idleCycles = 0;
                continue;
            }

            if (++idleCycles >= IdleCyclesBeforeExhausted)
                break;
        }

        DateTime judgedAt = captureClock is not null
            ? captureClock.GetUtcNow().UtcDateTime
            : DateTime.UtcNow;

        return new DecideReplayReport(
            path,
            rows,
            byReason.OrderByDescending(e => e.Value).ThenBy(e => e.Key, StringComparer.Ordinal).ToList(),
            byOutcome.OrderByDescending(e => e.Value).ThenBy(e => e.Key).ToList(),
            loop.ActingEnabled,
            FailureReason: null,
            AsOfCapture: asOfCapture,
            LastVitalsAtUtc: lastReadingAt,
            FreshnessJudgedAtUtc: judgedAt);
    }

    /// <summary>The operator-facing block. Stable enough to assert against.</summary>
    public static string Format(DecideReplayReport report)
    {
        var text = new StringBuilder();
        text.AppendLine("=== Gate 3 over a recorded world channel ===");
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Recording: {report.Path}"));
        text.AppendLine("Every reading below is CACHED: these bytes were real when they were");
        text.AppendLine("captured and are not current now. Nothing can act on them, and with the");
        text.AppendLine("safe default policy nothing could act on a live one either.");
        text.AppendLine();

        if (report.FailureReason is { } failure)
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"unreadable: {failure}"));
            return text.ToString();
        }

        text.AppendLine(FreshnessLine(report));
        text.AppendLine();

        foreach (DecideReplayCycleRow row in report.Cycles)
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"cycle #{row.Index}  {row.Outcome}  {row.Action}"));
            text.AppendLine(FormatStage("plan", row.Plan, row.Summary));
            text.AppendLine(FormatStage("safety", row.Safety, row.Summary));
            text.AppendLine(FormatStage("execution", row.Execution, row.Summary));
            text.AppendLine(FormatStage("verify", row.Verify, row.Summary));
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  stopped: {row.Summary}"));
        }

        text.AppendLine();
        text.AppendLine("counts by reason:");
        if (report.CountsByReason.Count == 0)
            text.AppendLine("  (none)");
        foreach (KeyValuePair<string, int> entry in report.CountsByReason)
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {entry.Value}  {entry.Key}"));

        text.AppendLine("counts by outcome:");
        if (report.CountsByOutcome.Count == 0)
            text.AppendLine("  (none)");
        foreach (KeyValuePair<CycleOutcome, int> entry in report.CountsByOutcome)
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {entry.Value}  {entry.Key}"));

        text.AppendLine();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Acting enabled: {report.ActingEnabled}"));
        text.AppendLine("A replayed reading is never actionable: it is real and it is not recent.");
        return text.ToString();
    }

    /// <summary>
    /// The one line that says which staleness the reader is looking at. Under the
    /// system clock a <c>player_vitals_stale</c> is structural — every recording is
    /// old — and says nothing about this capture; under <c>--as-of-capture</c> the
    /// same words mean a real gap in the wire.
    /// </summary>
    private static string FreshnessLine(DecideReplayReport report)
    {
        if (report.AsOfCapture)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"Freshness: judged as-of-capture on the recording's own timestamps — a player_vitals_stale below is a real gap in the wire (two readings more than {NetworkGameplayProvider.DefaultMaxVitalsAge.TotalSeconds:F0} seconds apart), not the capture's age.");
        }

        TimeSpan age = report.FreshnessJudgedAtUtc - (report.LastVitalsAtUtc ?? report.FreshnessJudgedAtUtc);
        return string.Create(CultureInfo.InvariantCulture,
            $"Freshness: judged against the system clock — the capture is {age.TotalSeconds:F0} seconds old, so a player_vitals_stale below is structural (an old recording), not a gap in this capture.");
    }

    /// <summary>
    /// Which of the four named stages the existing <see cref="CycleOutcome"/>
    /// reached. The mapping restates <c>ExecuteCycleAsync</c>'s early returns;
    /// it does not add a refusal.
    /// </summary>
    public static (string Plan, string Safety, string Execution, string Verify) Scale(CycleOutcome outcome)
        => outcome switch
        {
            CycleOutcome.NoCandidate => (Refused, NotEvaluated, NotEvaluated, NotEvaluated),
            CycleOutcome.Blocked => (Passed, Refused, NotEvaluated, NotEvaluated),
            CycleOutcome.ExecutionDisabled => (Passed, Passed, Refused, NotEvaluated),
            CycleOutcome.Unverified => (Passed, Passed, Passed, Refused),
            CycleOutcome.Failed => (Passed, Passed, Passed, Refused),
            CycleOutcome.Confirmed => (Passed, Passed, Passed, Passed),
            _ => (NotEvaluated, NotEvaluated, NotEvaluated, NotEvaluated),
        };

    private static string FormatStage(string name, string status, string summary)
    {
        string reason = status == Refused ? "  " + summary : "";
        return string.Create(CultureInfo.InvariantCulture, $"  {(name + ":").PadRight(12)} {status}{reason}");
    }

    private static DecideReplayReport Failed(string path, string reason) => new(
        path,
        Array.Empty<DecideReplayCycleRow>(),
        Array.Empty<KeyValuePair<string, int>>(),
        Array.Empty<KeyValuePair<CycleOutcome, int>>(),
        ActingEnabled: false,
        FailureReason: reason,
        AsOfCapture: false,
        LastVitalsAtUtc: null,
        FreshnessJudgedAtUtc: DateTime.UtcNow);

    /// <summary>The loop logs transitions; this command already prints every cycle.</summary>
    private sealed class DiscardingRuntimeLogger : IRuntimeLogger
    {
        public void Info(string message, IReadOnlyDictionary<string, object?>? properties = null) { }
        public void Warning(string message, IReadOnlyDictionary<string, object?>? properties = null) { }
        public void Error(string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? properties = null) { }
    }

    /// <summary>
    /// Wraps a packet source so a <see cref="CaptureClock"/> advances with every
    /// packet's timestamp. The first packet is read eagerly to seed the clock, then
    /// handed back on the first <c>TryRead</c>, so the clock answers with the first
    /// packet's instant before anything has been read — never with the system clock.
    /// </summary>
    private sealed class CaptureClockPacketSource : IPacketSource
    {
        private readonly IPacketSource _inner;
        private CapturedPacket? _pending;

        public CaptureClockPacketSource(IPacketSource inner)
        {
            _inner = inner;
            if (_inner.TryRead(TimeSpan.Zero, out CapturedPacket first))
            {
                _pending = first;
                Clock = new CaptureClock(AsUtc(first.TimestampUtc));
            }
            else
            {
                // An empty recording has no wire instant to seed from. Nothing can be
                // stale before anything has been read, so any epoch works here.
                Clock = new CaptureClock(DateTimeOffset.UnixEpoch);
            }
        }

        public CaptureClock Clock { get; }
        public IPAddress ServerAddress => _inner.ServerAddress;
        public int ServerPort => _inner.ServerPort;

        public bool TryRead(TimeSpan timeout, out CapturedPacket packet)
        {
            if (_pending is { } first)
            {
                _pending = null;
                packet = first;
                return true;
            }

            if (_inner.TryRead(timeout, out packet))
            {
                Clock.Advance(AsUtc(packet.TimestampUtc));
                return true;
            }

            return false;
        }

        public void Dispose() => _inner.Dispose();

        private static DateTimeOffset AsUtc(DateTime wireTime) =>
            new(wireTime.ToUniversalTime(), TimeSpan.Zero);
    }
}
