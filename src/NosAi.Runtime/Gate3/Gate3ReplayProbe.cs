using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate1;
using NosAi.Runtime.Observability;
using NosAi.Runtime.Perception.Network;

namespace NosAi.Runtime.Gate3;

/// <summary>
/// Drives the decision loop from a recorded world channel, offline.
/// </summary>
/// <remarks>
/// <para>
/// <c>WinDivertProbe --world &lt;file&gt;</c> already shows what the recording
/// <i>says</i>: 62 <c>stat</c> readings, HP 7218..7305. This shows what the
/// runtime <i>decides</i> about it — the same bytes carried through framing,
/// decoding, the gameplay provider, the Gate 3 planner, the simulator, the
/// ranking and the Safety Gate, printing the outcome of every cycle.
/// </para>
/// <para>
/// It needs no driver, no elevation and no client running, which is what makes it
/// repeatable. What it is not is a live run: every reading is CACHED by
/// construction, exactly as <see cref="WorldChannelReplay"/> is, so nothing here
/// closes T-05's remaining half. It closes a different question, and one nothing
/// answered before — whether the decision path works at all on real game bytes.
/// </para>
/// </remarks>
public static class Gate3ReplayProbe
{
    /// <summary>
    /// Polls with nothing new before the recording is called spent. More than one,
    /// because reassembly can need a further packet before a frame completes.
    /// </summary>
    private const int IdleCyclesBeforeExhausted = 5;

    /// <param name="path">A <c>.noscap</c> recording.</param>
    /// <param name="maxCycles">
    /// How many decision cycles to run. Each one reads the provider once, so this
    /// is how far into the recording the run gets.
    /// </param>
    public static async Task<int> RunAsync(string path, int maxCycles = 200)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Console.Error.WriteLine($"Recording not found: {path}");
            Console.Error.WriteLine("Usage: --decide-replay <file.noscap> [--decide-cycles N]");
            return 2;
        }

        Console.WriteLine("=== Gate 3 over a recorded world channel ===");
        Console.WriteLine($"Recording: {path}");
        Console.WriteLine("Every reading below is CACHED: these bytes were real when they were");
        Console.WriteLine("captured and are not current now. Nothing can act on them, and with the");
        Console.WriteLine("safe default policy nothing could act on a live one either.");
        Console.WriteLine();

        using IPacketSource packets = CaptureFile.Open(path);
        // The endpoint filter has to match the recording, and the recording knows
        // it: taking it from the file rather than from a flag means the probe
        // cannot be pointed at the wrong conversation by a stale argument.
        var endpoint = new GameEndpoint(packets.ServerAddress.ToString(), packets.ServerPort);
        Console.WriteLine($"Endpoint: {endpoint.Host}:{endpoint.Port}");

        using Gate1ObservationChannel channel =
            Gate1ObservationChannel.FromPackets(packets, endpoint, DataSourceKind.Cached);
        if (channel.Provider is null)
        {
            Console.Error.WriteLine($"Observation chain did not compose: {channel.FailureReason}");
            return 1;
        }

        await using var loop = new Gate3DecisionLoop(
            new GameplayProviderWorldStateSource(channel.Provider),
            new Gate3ExecutionOrchestrator(),
            new ConsoleRuntimeLogger());

        var byOutcome = new Dictionary<CycleOutcome, int>();
        var byDecision = new Dictionary<CycleOutcome, int>();
        DateTime? lastReadingAt = null;
        var idleCycles = 0;
        var decisions = 0;
        var cyclesRun = 0;

        for (var i = 0; i < maxCycles; i++)
        {
            Gate3LoopCycle cycle = await loop.RunOnceAsync().ConfigureAwait(false);
            cyclesRun = i + 1;
            byOutcome[cycle.Outcome] = byOutcome.GetValueOrDefault(cycle.Outcome) + 1;

            // A file source hands over everything it holds as fast as it is polled,
            // so cycles are not evenly spaced through the recording the way they
            // would be on a live channel. What separates a cycle that saw something
            // from one that did not is the reading's own timestamp: a fresh decode
            // carries a new one, a retained reading carries the same one again.
            bool newReading = cycle.Hp.HasValue && cycle.Hp.ObservedAtUtc != lastReadingAt;
            if (newReading)
            {
                lastReadingAt = cycle.Hp.ObservedAtUtc;
                idleCycles = 0;
                decisions++;
                byDecision[cycle.Outcome] = byDecision.GetValueOrDefault(cycle.Outcome) + 1;

                Console.WriteLine(
                    $"  #{decisions,-3} {cycle.Outcome,-18} {cycle.SelectedAction,-14} " +
                    $"HP {cycle.Hp.Value}/{cycle.MaxHp.Value} MP {(cycle.Mp.HasValue ? cycle.Mp.Value.ToString() : "?")} " +
                    $"[{cycle.Hp.Source.ToWire()} @ {cycle.Hp.ObservedAtUtc:HH:mm:ss.fff}]");
                Console.WriteLine($"       {cycle.Summary}");
                continue;
            }

            // The recording is spent once polling stops yielding anything new.
            // Carrying on would only count the same refusal, which is a measure of
            // how long the loop ran and not of what the recording contained.
            if (++idleCycles >= IdleCyclesBeforeExhausted)
            {
                Console.WriteLine();
                Console.WriteLine($"Recording exhausted after {cyclesRun} cycles: "
                                  + $"{idleCycles} consecutive polls carried no new reading.");
                break;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Readings that produced a decision: {decisions}");
        foreach ((CycleOutcome outcome, int count) in byDecision.OrderByDescending(entry => entry.Value))
            Console.WriteLine($"  {outcome,-22} {count}");

        Console.WriteLine();
        Console.WriteLine($"All {cyclesRun} cycles, including polls with nothing new:");
        foreach ((CycleOutcome outcome, int count) in byOutcome.OrderByDescending(entry => entry.Value))
            Console.WriteLine($"  {outcome,-22} {count}");

        Console.WriteLine();
        Console.WriteLine($"Acting enabled: {loop.ActingEnabled}");
        Console.WriteLine("A replayed reading is never actionable: it is real and it is not recent.");

        // A run that never reached a plannable state is a failure of this probe,
        // not a quiet success. The combat recording carries 62 stat readings, so
        // producing none means the chain read nothing at all.
        if (decisions == 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("No cycle ever reached a plannable state. The chain read nothing from this");
            Console.Error.WriteLine("recording; compare with: WinDivertProbe.exe --world <file>");
            return 1;
        }

        return 0;
    }
}
