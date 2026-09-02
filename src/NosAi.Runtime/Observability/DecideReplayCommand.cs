using System.Globalization;
using System.Text;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Autonomy;
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
public sealed record DecideReplayReport(
    string Path,
    IReadOnlyList<DecideReplayCycleRow> Cycles,
    IReadOnlyList<KeyValuePair<string, int>> CountsByReason,
    IReadOnlyList<KeyValuePair<CycleOutcome, int>> CountsByOutcome,
    bool ActingEnabled,
    string? FailureReason)
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
        DecideReplayReport report = await InspectFileAsync(path, maxCycles).ConfigureAwait(false);
        Console.Write(Format(report));
        if (!report.Ok)
        {
            Console.Error.WriteLine($"Recording not readable: {path}");
            Console.Error.WriteLine("Usage: --decide-replay <file.noscap> [--decide-cycles N]");
            return ExitUnreadable;
        }

        return 0;
    }

    /// <summary>Reads a path. Zero cycles printed is a successful diagnosis, not a fault.</summary>
    public static async Task<DecideReplayReport> InspectFileAsync(string path, int maxCycles = DefaultMaxCycles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            return Failed(path, "recording_not_found");

        try
        {
            using IPacketSource packets = CaptureFile.Open(path);
            return await InspectAsync(path, packets, maxCycles).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return Failed(path, $"recording_unreadable:{ex.GetType().Name}");
        }
    }

    /// <summary>Drives the existing loop over an already-open source.</summary>
    public static async Task<DecideReplayReport> InspectAsync(
        string path, IPacketSource packets, int maxCycles = DefaultMaxCycles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(packets);
        if (maxCycles < 1)
            maxCycles = 1;

        var endpoint = new GameEndpoint(packets.ServerAddress.ToString(), packets.ServerPort);
        using Gate1ObservationChannel channel =
            Gate1ObservationChannel.FromPackets(packets, endpoint, DataSourceKind.Cached);
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

        return new DecideReplayReport(
            path,
            rows,
            byReason.OrderByDescending(e => e.Value).ThenBy(e => e.Key, StringComparer.Ordinal).ToList(),
            byOutcome.OrderByDescending(e => e.Value).ThenBy(e => e.Key).ToList(),
            loop.ActingEnabled,
            FailureReason: null);
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
        FailureReason: reason);

    /// <summary>The loop logs transitions; this command already prints every cycle.</summary>
    private sealed class DiscardingRuntimeLogger : IRuntimeLogger
    {
        public void Info(string message, IReadOnlyDictionary<string, object?>? properties = null) { }
        public void Warning(string message, IReadOnlyDictionary<string, object?>? properties = null) { }
        public void Error(string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? properties = null) { }
    }
}
