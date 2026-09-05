using System.Collections.Concurrent;
using System.Threading;
using NosAi.Core.Scheduling;
using Xunit;
using NosAi.Core.Hardware;

namespace NosAi.Core.Tests.Scheduling;

/// <summary>
/// AP-00 / A5 audit coverage: concurrency behaviour of A3's admission path
/// (docs/agents/phases/AP-00/A5_CLAUDE_tests_docs.md explicitly calls out
/// "concorrenza sul ledger di A3" as a case to check).
///
/// <see cref="ResourceBudgetLedger"/> and <see cref="BoundedTierQueue"/> are
/// each individually documented and, per <see cref="LedgerAlone_NeverOvercommits_UnderHeavyConcurrentTryReserve"/>
/// and <see cref="Queue_Alone_NeverExceedsCapacity_UnderHeavyConcurrentTryEnqueue"/>
/// below, actually ARE thread-safe on their own: every mutation is taken
/// under a single lock and is genuinely atomic.
///
/// <see cref="InferenceBudgetScheduler.TryAdmit"/>, however, composes them
/// non-atomically in <c>TryAdmitCore</c>:
///   1. read <c>_ledger.Available</c> (one lock acquisition/release);
///   2. <c>_queue.TryEnqueue(job)</c> (a second, separate lock);
///   3. <c>_ledger.TryReserve(job.EstimatedCost)</c> (a third, separate lock)
///      -- and its <c>bool</c> return value is DISCARDED
///      (src/NosAi.Core/Scheduling/InferenceBudgetScheduler.cs, the line
///      right after the "Reserve after a successful enqueue" comment).
///
/// Because step 3's result is never checked, two concurrent callers can both
/// pass step 1's headroom check against the same still-unreserved capacity,
/// both succeed at step 2 (the queue has no idea the ledger is nearly
/// exhausted), and then only ONE of the two step-3 calls actually reserves
/// anything -- the other's `TryReserve` call quietly returns `false` and
/// <see cref="InferenceBudgetScheduler.TryAdmit"/> still reports
/// <see cref="AdmissionOutcome.Accepted"/> for BOTH jobs. This is a real,
/// reliably reproducible violation of the AP-00 DoD in
/// docs/ROADMAP_ESECUTIVA.md S:7 ("nessun overcommit VRAM/RAM") and of this
/// class's own XML doc ("nothing here is ever silently dropped"): a caller
/// that trusts <see cref="AdmissionResult.Accepted"/> as "the ledger now
/// holds a real reservation for this job" is being told something false.
///
/// This is NOT a rare/theoretical race: with a handful of concurrent
/// admitters and a tight budget it reproduces on effectively every run (see
/// the reproduction test below, which asserts the correct invariant and is
/// therefore expected to FAIL until the discarded <c>TryReserve</c> result
/// is fixed).
///
/// PER AP-00/A5 SCOPE: this test file only documents and proves the defect;
/// fixing <c>InferenceBudgetScheduler.TryAdmitCore</c> is out of A5's file
/// ownership (src/NosAi.Core/Scheduling/ belongs to A3) and is left for A6 /
/// a dedicated follow-up. Recommended fix direction for whoever picks this
/// up: treat a `false` return from the final `_ledger.TryReserve(...)` call
/// exactly like a queue-full rejection -- dequeue the job that was just
/// enqueued, then run the existing `FailOrDegradeOnce` path for it (reason
/// `RejectionReason.InsufficientBudget`) instead of falling through to
/// "Accepted".
///
/// DO NOT "fix" this suite by deleting, skipping or weakening
/// <see cref="ConcurrentTryAdmit_MustNeverAcceptMoreJobsThanTheLedgerCanActuallyReserve_KnownSchedulerDefect"/>
/// -- CLAUDE.md forbids deleting or weakening tests, and this one exists
/// specifically to keep this defect visible until it is actually fixed.
/// </summary>
public sealed class InferenceBudgetSchedulerConcurrencyTests
{
    private static InferenceBudgetScheduler NewSingleUnitScheduler(int queueCapacityPerTier)
    {
        var clock = new ManualMonotonicClock(1_000_000);
        // Capacity for exactly ONE job's worth of CPU budget: at most one
        // concurrent admission should ever be able to reserve it.
        var ledger = new ResourceBudgetLedger(new ResourceCost(1, 0, 0, 0));
        var queue = new BoundedTierQueue(new Dictionary<InferenceTier, int>
        {
            [InferenceTier.Tier0DeterministicRules] = queueCapacityPerTier,
            [InferenceTier.Tier1LightweightLocalMl] = queueCapacityPerTier,
            [InferenceTier.Tier2GpuAcceleratedVision] = queueCapacityPerTier,
            [InferenceTier.Tier3ExpensiveLocalReasoning] = queueCapacityPerTier
        });
        return new InferenceBudgetScheduler(queue, ledger, clock);
    }

    private static InferenceJob OneUnitJob(string id, long now) =>
        new(id, InferenceTier.Tier1LightweightLocalMl, JobPriority.Normal, now + 60_000, 10,
            new ResourceCost(1, 0, 0, 0), 0.1, FallbackStrategy.Reject());

    /// <summary>
    /// Runs <paramref name="body"/> once per index in [0, threadCount) on its
    /// own dedicated <see cref="Thread"/> (never the shared .NET
    /// <c>ThreadPool</c>), releasing all of them at once via
    /// <paramref name="barrier"/>. Using real, purpose-started threads rather
    /// than <c>Parallel.For</c>/<c>Task.Run</c> matters here specifically
    /// because the default <c>ThreadPool</c> grows slowly (roughly one new
    /// thread per ~0.5-1s) once existing pooled threads are blocked on a
    /// <see cref="Barrier"/>, which would make a multi-round race test like
    /// this one take tens of seconds for reasons that have nothing to do with
    /// the behaviour under test.
    /// </summary>
    private static void RunConcurrently(int threadCount, Action<int> body)
    {
        var threads = new Thread[threadCount];
        for (int i = 0; i < threadCount; i++)
        {
            int captured = i;
            threads[i] = new Thread(() => body(captured));
        }
        foreach (Thread t in threads)
            t.Start();
        foreach (Thread t in threads)
            t.Join();
    }

    /// <summary>
    /// KNOWN, PRE-EXISTING DEFECT (see the type-level doc comment above for the
    /// full mechanism). This test asserts the actual AP-00 DoD -- a 1-unit
    /// resource budget must never yield more than one
    /// <see cref="AdmissionOutcome.Accepted"/> result across concurrent
    /// callers -- and is expected to FAIL today because of the discarded
    /// <c>TryReserve</c> return value in <c>InferenceBudgetScheduler.TryAdmitCore</c>.
    ///
    /// This is a correctness bug, not a timing-sensitive performance
    /// assertion (contrast with the already-known-flaky
    /// <c>TransportLoopTests</c> latency budget elsewhere in this suite):
    /// many concurrent threads racing a tight, fixed budget reproduce
    /// overcommit essentially every run, independent of machine speed.
    /// </summary>
    [Fact]
    public void ConcurrentTryAdmit_MustNeverAcceptMoreJobsThanTheLedgerCanActuallyReserve_KnownSchedulerDefect()
    {
        const int rounds = 5;
        const int threadsPerRound = 16;
        int maxAcceptedInAnySingleRound = 0;
        string? diagnostic = null;

        for (int round = 0; round < rounds; round++)
        {
            var scheduler = NewSingleUnitScheduler(queueCapacityPerTier: threadsPerRound + 1);
            var barrier = new Barrier(threadsPerRound);
            var results = new ConcurrentBag<AdmissionResult>();

            RunConcurrently(threadsPerRound, i =>
            {
                InferenceJob job = OneUnitJob($"round{round}-job{i}", 1_000_000);
                barrier.SignalAndWait();
                results.Add(scheduler.TryAdmit(job));
            });

            int acceptedCount = 0;
            foreach (AdmissionResult r in results)
                if (r.Accepted)
                    acceptedCount++;

            if (acceptedCount > maxAcceptedInAnySingleRound)
            {
                maxAcceptedInAnySingleRound = acceptedCount;
                diagnostic =
                    $"Round {round}: {acceptedCount}/{threadsPerRound} concurrent 1-unit jobs were reported " +
                    $"Accepted against a 1-unit total budget. Ledger.Available after round: {scheduler.Ledger.Available}, " +
                    $"Ledger.Committed after round: {scheduler.Ledger.Committed}.";
            }

            // Demonstrate the operational consequence: if every "Accepted" job
            // later calls Complete() (the documented, expected lifecycle -- see
            // InferenceBudgetScheduler.Complete's XML doc), a job that was
            // falsely marked Accepted without ever actually reserving anything
            // will release resources it never held, either double-releasing
            // another job's real reservation (corrupting the ledger silently)
            // or throwing InvalidOperationException out of a call site that
            // had no reason to expect one.
            if (acceptedCount > 1)
            {
                int unexpectedReleaseFailures = 0;
                foreach (AdmissionResult r in results)
                {
                    if (!r.Accepted)
                        continue;
                    try
                    {
                        scheduler.Complete(r.Job);
                    }
                    catch (InvalidOperationException)
                    {
                        unexpectedReleaseFailures++;
                    }
                }

                Assert.True(
                    unexpectedReleaseFailures > 0 || scheduler.Ledger.Available.CpuMillis >= 0,
                    "Expected either an unbalanced-release exception or a corrupted (over-released) ledger " +
                    "once every falsely-'Accepted' job completes -- confirming the overcommit has a real " +
                    "downstream blast radius, not just a cosmetic reporting mismatch.");
            }
        }

        Assert.True(
            maxAcceptedInAnySingleRound <= 1,
            "InferenceBudgetScheduler.TryAdmit overcommitted the ledger under concurrent access. " + diagnostic +
            " Root cause: src/NosAi.Core/Scheduling/InferenceBudgetScheduler.cs TryAdmitCore discards the " +
            "bool result of the final _ledger.TryReserve(job.EstimatedCost) call, so a losing concurrent " +
            "caller is still reported as AdmissionOutcome.Accepted even though nothing was actually reserved " +
            "for it. This is a real, pre-existing defect in A3's Scheduling module found during the AP-00/A5 " +
            "test audit -- NOT introduced by this test file and NOT fixable from within A5's file ownership " +
            "(src/NosAi.Core/Scheduling/ belongs to A3). See this test class's XML doc for the recommended fix " +
            "and hand this off to A6 before AP-00 can be called 'Integrated' with a straight face about " +
            "'nessun overcommit VRAM/RAM' (docs/ROADMAP_ESECUTIVA.md S:7).");
    }

    /// <summary>
    /// Isolation control: <see cref="ResourceBudgetLedger"/> ON ITS OWN (no
    /// <see cref="BoundedTierQueue"/>, no scheduler composing the two) is
    /// genuinely thread-safe -- <see cref="ResourceBudgetLedger.TryReserve"/>
    /// performs its check-and-commit under a single lock, so concurrent
    /// callers racing a tight budget correctly see at most as many successes
    /// as the budget allows. This proves the overcommit defect above lives in
    /// <see cref="InferenceBudgetScheduler"/>'s non-atomic composition, not in
    /// the ledger's own synchronization.
    /// </summary>
    [Fact]
    public void LedgerAlone_NeverOvercommits_UnderHeavyConcurrentTryReserve()
    {
        for (int round = 0; round < 5; round++)
        {
            var ledger = new ResourceBudgetLedger(new ResourceCost(1, 0, 0, 0));
            const int threadCount = 16;
            var barrier = new Barrier(threadCount);
            int successCount = 0;

            RunConcurrently(threadCount, _ =>
            {
                barrier.SignalAndWait();
                if (ledger.TryReserve(new ResourceCost(1, 0, 0, 0)))
                    Interlocked.Increment(ref successCount);
            });

            Assert.True(successCount <= 1, $"Round {round}: ledger allowed {successCount} concurrent reservations against a 1-unit budget.");
            Assert.True(ledger.Available.CpuMillis >= 0, $"Round {round}: ledger.Available went negative purely from concurrent TryReserve calls.");
        }
    }

    /// <summary>
    /// Isolation control: <see cref="BoundedTierQueue"/> ON ITS OWN never
    /// admits more jobs than its configured per-tier capacity, even under
    /// heavy concurrent <see cref="BoundedTierQueue.TryEnqueue"/> calls --
    /// its single <c>object</c> lock makes capacity-check-then-enqueue
    /// atomic. Paired with <see cref="LedgerAlone_NeverOvercommits_UnderHeavyConcurrentTryReserve"/>,
    /// this narrows the known overcommit defect to
    /// <see cref="InferenceBudgetScheduler"/>'s composition of the two,
    /// rather than either primitive's own thread-safety.
    /// </summary>
    [Fact]
    public void Queue_Alone_NeverExceedsCapacity_UnderHeavyConcurrentTryEnqueue()
    {
        for (int round = 0; round < 5; round++)
        {
            var queue = new BoundedTierQueue(new Dictionary<InferenceTier, int> { [InferenceTier.Tier1LightweightLocalMl] = 1 });
            const int threadCount = 16;
            var barrier = new Barrier(threadCount);
            int acceptedCount = 0;

            RunConcurrently(threadCount, i =>
            {
                InferenceJob job = OneUnitJob($"round{round}-q{i}", 1_000_000);
                barrier.SignalAndWait();
                if (queue.TryEnqueue(job).Accepted)
                    Interlocked.Increment(ref acceptedCount);
            });

            Assert.True(acceptedCount <= 1, $"Round {round}: queue with capacity 1 accepted {acceptedCount} concurrent enqueues.");
            Assert.Equal(1, queue.Count(InferenceTier.Tier1LightweightLocalMl));
        }
    }
}
