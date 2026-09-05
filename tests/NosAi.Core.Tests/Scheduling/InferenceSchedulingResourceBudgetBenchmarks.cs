using System.Diagnostics;
using NosAi.Core.Scheduling;
using Xunit;
using Xunit.Abstractions;
using NosAi.Core.Hardware;

namespace NosAi.Core.Tests.Scheduling;

/// <summary>
/// AP-00 / A5 required deliverable: a reproducible resource-budget benchmark
/// for A3's scheduling module (docs/agents/phases/AP-00/A5_CLAUDE_tests_docs.md
/// REQUIRE list: "record reproducible benchmark commands and acceptance
/// criteria"; docs/ROADMAP_ESECUTIVA.md AP-00 DoD: "benchmark archiviati").
///
/// These are MEASUREMENTS, not correctness assertions. Every threshold below
/// is deliberately generous and exists only to catch a catastrophic
/// regression (e.g. an accidental blocking call reintroduced into a hot
/// path), never to enforce a tight numeric SLO on a shared/unknown-spec CI
/// runner. See <c>tests/NosAi.Core.Tests/TransportLoopTests.cs</c>
/// (<c>OneHundredLoopbackHandshakesStayUnderTheTwentyFiveMillisecondBudget</c>)
/// in this same repository for a worked example of what NOT to do: a tight,
/// machine-speed-dependent latency budget that already fails intermittently
/// on this very CI environment (see this task's own build/test evidence).
/// This file's bounds are chosen to be at least one, usually two, orders of
/// magnitude looser than any measurement observed while writing it.
///
/// HOW TO RE-RUN THIS BENCHMARK:
///   export PATH="/root/.dotnet:$PATH"; export DOTNET_CLI_TELEMETRY_OPTOUT=1; export DOTNET_NOLOGO=1
///   dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release \
///     --filter "FullyQualifiedName~InferenceSchedulingResourceBudgetBenchmarks" \
///     --logger "console;verbosity=detailed"
/// The measured numbers are printed via <see cref="ITestOutputHelper"/> (visible
/// with -v detailed / dotnet test's own captured output) so a human can read
/// the actual throughput/latency figures for this machine without needing to
/// change any assertion to see them.
/// </summary>
public sealed class InferenceSchedulingResourceBudgetBenchmarks
{
    private readonly ITestOutputHelper _output;

    public InferenceSchedulingResourceBudgetBenchmarks(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Throughput benchmark: with a fixed, modest resource budget and bounded
    /// per-tier queues (numbers chosen to resemble
    /// docs/HARDWARE_PROFILE_ASUS_NITRO_V16.md's 16 GB RAM / 8 GB-class VRAM
    /// laptop target, scaled down for a millisecond/megabyte-costed synthetic
    /// job mix), measures how many Tier0-3 <see cref="InferenceJob"/>
    /// admissions <see cref="InferenceBudgetScheduler.TryAdmit"/> can process
    /// per second, and what fraction are accepted vs. explicitly rejected
    /// once the budget/queues fill up. Single-threaded by design (this
    /// benchmark measures steady-state admission throughput, not the
    /// concurrency correctness covered separately by
    /// <see cref="InferenceBudgetSchedulerConcurrencyTests"/>).
    /// </summary>
    [Fact]
    public void AdmissionThroughput_UnderAFixedModestBudget()
    {
        var clock = new ManualMonotonicClock(1_000_000);
        var ledger = new ResourceBudgetLedger(new ResourceCost(CpuMillis: 500, GpuMillis: 500, RamBytes: 512, VramBytes: 512));
        var queue = new BoundedTierQueue(new Dictionary<InferenceTier, int>
        {
            [InferenceTier.Tier0DeterministicRules] = 64,
            [InferenceTier.Tier1LightweightLocalMl] = 32,
            [InferenceTier.Tier2GpuAcceleratedVision] = 16,
            [InferenceTier.Tier3ExpensiveLocalReasoning] = 4
        });
        var scheduler = new InferenceBudgetScheduler(queue, ledger, clock);

        const int totalJobs = 20_000;
        int accepted = 0, acceptedDegraded = 0, rejected = 0;

        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < totalJobs; i++)
        {
            InferenceTier tier = (InferenceTier)(i % 4);
            var cost = new ResourceCost(CpuMillis: 1, GpuMillis: tier >= InferenceTier.Tier2GpuAcceleratedVision ? 1 : 0, RamBytes: 1, VramBytes: tier >= InferenceTier.Tier2GpuAcceleratedVision ? 1 : 0);
            var job = new InferenceJob(
                $"job-{i}", tier, JobPriority.Normal,
                DeadlineUnixMillis: clock.UnixMillis + 60_000, EstimatedDurationMs: 1,
                EstimatedCost: cost, MinimumConfidence: 0.1, Fallback: FallbackStrategy.Reject());

            AdmissionResult result = scheduler.TryAdmit(job);
            switch (result.Outcome)
            {
                case AdmissionOutcome.Accepted: accepted++; break;
                case AdmissionOutcome.AcceptedDegraded: acceptedDegraded++; break;
                default: rejected++; break;
            }

            // Simulate steady-state churn: every 5th job "completes" and frees
            // its reservation, so the ledger does not simply fill up once and
            // reject everything thereafter -- this measures ongoing admission
            // throughput under realistic turnover, not a one-shot fill.
            if (result.Accepted && i % 5 == 0)
                scheduler.Complete(result.Job);
        }
        stopwatch.Stop();

        double jobsPerSecond = totalJobs / stopwatch.Elapsed.TotalSeconds;

        _output.WriteLine($"[AP-00/A5 benchmark] InferenceBudgetScheduler.TryAdmit: {totalJobs} jobs in {stopwatch.Elapsed.TotalMilliseconds:0.###} ms " +
                           $"=> {jobsPerSecond:N0} admissions/sec (accepted={accepted}, acceptedDegraded={acceptedDegraded}, rejected={rejected}).");

        // Generous sanity floor only: a correctly-implemented, O(1)-per-call
        // admission path should clear many tens of thousands of jobs/sec even
        // on a slow/shared CI core. This is not a performance SLO -- it only
        // catches a gross regression (e.g. an accidental O(n) scan or a
        // blocking wait creeping into the hot path).
        Assert.True(jobsPerSecond > 1_000,
            $"Admission throughput ({jobsPerSecond:N0} jobs/sec) fell far below the generous 1,000/sec sanity floor -- " +
            "investigate for an accidental blocking call or O(n) scan in the admission hot path.");
        Assert.Equal(totalJobs, accepted + acceptedDegraded + rejected);
    }

    /// <summary>
    /// Latency benchmark for <see cref="Tier3AsyncExecutor.RunWithDeadlineAsync{T}"/>:
    /// measures p50/p95/p99 wall-clock latency for work that completes well
    /// within its deadline, printing the distribution rather than asserting a
    /// tight bound on it (see the type-level remarks on why: shared/CI
    /// runners are not a reliable latency-SLO environment). The one assertion
    /// this test does make is qualitative and generous: the executor must not
    /// add order-of-magnitude overhead on top of the simulated work itself.
    /// </summary>
    [Fact]
    public async Task Tier3AsyncExecutor_LatencyDistribution_ForWorkThatCompletesWithinItsDeadline()
    {
        var clock = new ManualMonotonicClock(1_000_000);
        var executor = new Tier3AsyncExecutor(clock);
        const int samples = 50;
        var latenciesMs = new List<double>(samples);

        for (int i = 0; i < samples; i++)
        {
            var job = new InferenceJob(
                $"reasoning-{i}", InferenceTier.Tier3ExpensiveLocalReasoning, JobPriority.Normal,
                DeadlineUnixMillis: clock.UnixMillis + 5_000, EstimatedDurationMs: 5,
                EstimatedCost: new ResourceCost(1, 1, 1, 1), MinimumConfidence: 0.5, Fallback: FallbackStrategy.Reject());

            var stopwatch = Stopwatch.StartNew();
            Tier3ExecutionResult<int> result = await executor.RunWithDeadlineAsync(job, async ct =>
            {
                await Task.Delay(1, ct);
                return 1;
            });
            stopwatch.Stop();

            Assert.Equal(Tier3ExecutionOutcome.Completed, result.Outcome);
            latenciesMs.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        latenciesMs.Sort();
        double p50 = Percentile(latenciesMs, 0.50);
        double p95 = Percentile(latenciesMs, 0.95);
        double p99 = Percentile(latenciesMs, 0.99);

        _output.WriteLine($"[AP-00/A5 benchmark] Tier3AsyncExecutor.RunWithDeadlineAsync latency over {samples} samples " +
                           $"(1ms simulated work, 5000ms deadline): p50={p50:0.###}ms p95={p95:0.###}ms p99={p99:0.###}ms " +
                           $"min={latenciesMs[0]:0.###}ms max={latenciesMs[^1]:0.###}ms.");

        // Deliberately generous, non-flaky bound: this only needs to catch a
        // regression where the deadline race machinery itself adds massive
        // overhead (e.g. seconds) on top of ~1ms of simulated work -- not to
        // pin an exact millisecond figure on a shared runner.
        Assert.True(p99 < 2_000,
            $"Tier3AsyncExecutor p99 latency ({p99:0.###}ms) for work well within its deadline was unexpectedly high " +
            "-- investigate for added overhead in the deadline-race machinery.");
    }

    /// <summary>
    /// Companion latency benchmark: measures how quickly
    /// <see cref="Tier3AsyncExecutor.RunWithDeadlineAsync{T}"/> actually
    /// returns control to the caller once a job's deadline is reached by
    /// work that never finishes on its own -- the core guarantee
    /// ("Tier 3 non può bloccare Safety/recovery",
    /// docs/ROADMAP_ESECUTIVA.md S:5) this class exists to provide. Prints
    /// the observed overshoot past the nominal deadline rather than asserting
    /// a tight bound on it.
    /// </summary>
    [Fact]
    public async Task Tier3AsyncExecutor_TimeoutReturnLatency_ForWorkThatNeverCompletesOnItsOwn()
    {
        var clock = new ManualMonotonicClock(1_000_000);
        var executor = new Tier3AsyncExecutor(clock);
        const long deadlineWindowMs = 50;
        var job = new InferenceJob(
            "hung-job", InferenceTier.Tier3ExpensiveLocalReasoning, JobPriority.Normal,
            DeadlineUnixMillis: clock.UnixMillis + deadlineWindowMs, EstimatedDurationMs: deadlineWindowMs,
            EstimatedCost: new ResourceCost(1, 1, 1, 1), MinimumConfidence: 0.5, Fallback: FallbackStrategy.Reject());

        var stopwatch = Stopwatch.StartNew();
        Tier3ExecutionResult<int> result = await executor.RunWithDeadlineAsync(job, async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return 1;
        });
        stopwatch.Stop();

        double overshootMs = stopwatch.Elapsed.TotalMilliseconds - deadlineWindowMs;

        _output.WriteLine($"[AP-00/A5 benchmark] Tier3AsyncExecutor timeout return latency: nominal deadline={deadlineWindowMs}ms, " +
                           $"actual return at {stopwatch.Elapsed.TotalMilliseconds:0.###}ms (overshoot={overshootMs:0.###}ms).");

        Assert.Equal(Tier3ExecutionOutcome.TimedOut, result.Outcome);
        // Generous bound: the caller must get control back within roughly the
        // deadline window plus scheduling slack, not after seconds of delay.
        Assert.True(stopwatch.Elapsed.TotalMilliseconds < deadlineWindowMs + 2_000,
            $"Caller was held for {stopwatch.Elapsed.TotalMilliseconds:0.###}ms against a {deadlineWindowMs}ms deadline -- " +
            "this is the exact guarantee Tier3AsyncExecutor exists to provide.");
    }

    private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
            return 0;
        double rank = percentile * (sortedValues.Count - 1);
        int lowerIndex = (int)Math.Floor(rank);
        int upperIndex = (int)Math.Ceiling(rank);
        if (lowerIndex == upperIndex)
            return sortedValues[lowerIndex];
        double fraction = rank - lowerIndex;
        return sortedValues[lowerIndex] + (sortedValues[upperIndex] - sortedValues[lowerIndex]) * fraction;
    }
}
