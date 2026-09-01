using System.Collections.Immutable;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Observability;

namespace NosAi.Runtime.Gate3;

/// <summary>One pass of the decision loop, as the operator sees it.</summary>
/// <param name="AtUtc">When the cycle ran.</param>
/// <param name="ObservationAge">
/// How old the reading it planned from was, or null when nothing was read. This
/// is the number the old all-LIVE rule could not report, and the one that
/// separates "the wire went quiet" from "the wire is lying" (ADR-0016).
/// </param>
/// <param name="WouldHaveActed">
/// Whether this cycle reached the point of applying an action. False while the
/// policy keeps live input off, which is the state a first real run is in.
/// </param>
public sealed record Gate3LoopCycle(
    DateTime AtUtc,
    CycleOutcome Outcome,
    string Summary,
    ActionType SelectedAction,
    ClassifiedValue<int> Hp,
    ClassifiedValue<int> MaxHp,
    ClassifiedValue<int> Mp,
    ClassifiedValue<bool> HasTarget,
    TimeSpan? ObservationAge,
    bool WouldHaveActed);

/// <summary>
/// The operator-facing state of the decision loop. Unknown fields carry a
/// reason; none of them is zeroed to look calm.
/// </summary>
public sealed record Gate3LoopView(
    ClassifiedValue<bool> Running,
    ClassifiedValue<long> CyclesRun,
    ClassifiedValue<string> LastOutcome,
    ClassifiedValue<string> LastAction,
    ClassifiedValue<string> LastSummary,
    ClassifiedValue<int> LastHp,
    ClassifiedValue<int> LastMaxHp,
    ClassifiedValue<double> LastObservationAgeSeconds,
    ClassifiedValue<bool> ActingEnabled,
    ImmutableArray<KeyValuePair<string, long>> OutcomeCounts)
{
    public object ToWire() => new
    {
        running = Running.ToWire(),
        cyclesRun = CyclesRun.ToWire(),
        lastOutcome = LastOutcome.ToWire(),
        lastAction = LastAction.ToWire(),
        lastSummary = LastSummary.ToWire(),
        lastHp = LastHp.ToWire(),
        lastMaxHp = LastMaxHp.ToWire(),
        lastObservationAgeSeconds = LastObservationAgeSeconds.ToWire(),
        actingEnabled = ActingEnabled.ToWire(),
        outcomeCounts = OutcomeCounts.ToDictionary(entry => entry.Key, entry => entry.Value)
    };

    /// <summary>The loop was never asked for.</summary>
    public static Gate3LoopView NotConfigured()
    {
        const string reason = "decision_loop_not_configured";
        return new(
            ClassifiedValue<bool>.Derived(false),
            ClassifiedValue<long>.Unknown(reason),
            ClassifiedValue<string>.Unknown(reason),
            ClassifiedValue<string>.Unknown(reason),
            ClassifiedValue<string>.Unknown(reason),
            ClassifiedValue<int>.Unknown(reason),
            ClassifiedValue<int>.Unknown(reason),
            ClassifiedValue<double>.Unknown(reason),
            ClassifiedValue<bool>.Derived(false),
            ImmutableArray<KeyValuePair<string, long>>.Empty);
    }
}

/// <summary>
/// Runs the Gate 3 cycle against whatever the runtime is currently observing.
/// </summary>
/// <remarks>
/// <para>
/// <b>The gap this closes.</b> Gate 3 held a complete
/// <c>Observe → Plan → Simulate → Rank → Guard → Execute → Verify</c> pipeline,
/// a world-state adapter that reads the Gate 1 snapshot
/// (<see cref="Gate1SnapshotWorldStateSource"/>), and a suite that passed. Nothing
/// constructed any of it. <c>NosAi.Host</c> refuses every gate but 1, no view in
/// the Control Panel showed a decision, and the only caller of
/// <see cref="Gate3ExecutionOrchestrator"/> anywhere in <c>src/</c> was its own
/// certification suite. The network path had begun publishing the player's real HP
/// and there was still nothing in the runtime that would form an opinion about it.
/// </para>
/// <para>
/// <b>It decides; it does not act.</b> With
/// <see cref="Safety.RuntimeSafetyPolicy.SafeDefault"/> the orchestrator binds a
/// <c>DisabledActionEffector</c>, so a cycle runs the whole pipeline through the
/// Safety Gate and stops at <see cref="CycleOutcome.ExecutionDisabled"/> — the
/// plan is formed, authorised, and deliberately not applied. That is the honest
/// first run against a live account: every stage exercised on real observations,
/// nothing sent to the client. Enabling live input is a separate decision with its
/// own policy field, and the risk recorded in ADR-0014 is the operator's.
/// </para>
/// <para>
/// <b>A quiet loop is not a healthy one.</b> Outcomes are counted by kind rather
/// than reduced to a success rate, because the failures differ in what they mean:
/// <see cref="CycleOutcome.NoWorldState"/> says nothing is being observed,
/// <see cref="CycleOutcome.NoCandidate"/> says the character is fine and there was
/// nothing to do, and <see cref="CycleOutcome.RefusedStaleInput"/> says the
/// channel is falling behind. Collapsing those into one number would hide the only
/// distinctions worth watching.
/// </para>
/// </remarks>
public sealed class Gate3DecisionLoop : IAsyncDisposable
{
    /// <summary>
    /// How often a cycle runs when the caller does not say. Slower than the wire —
    /// <c>stat</c> arrived 62 times in 90 s of combat — because a decision that
    /// nothing can act on gains nothing from being taken more often than a person
    /// can read it.
    /// </summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(500);

    private readonly IWorldStateSource _source;
    private readonly Gate3ExecutionOrchestrator _orchestrator;
    private readonly TimeSpan _interval;
    private readonly IRuntimeLogger _logger;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly Dictionary<CycleOutcome, long> _outcomes = new();

    private CancellationTokenSource? _cancellation;
    private Task? _pump;
    private Gate3LoopCycle? _last;
    private long _cycles;
    private bool _disposed;

    public Gate3DecisionLoop(
        IWorldStateSource source,
        Gate3ExecutionOrchestrator orchestrator,
        IRuntimeLogger logger,
        TimeSpan? interval = null,
        TimeProvider? clock = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _interval = interval ?? DefaultInterval;
        if (_interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval), "The loop interval must be positive.");
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>Whether the pump is running.</summary>
    public bool IsRunning => _pump is { IsCompleted: false };

    /// <summary>Whether anything is bound that could apply an action.</summary>
    public bool ActingEnabled => _orchestrator.CanExecute;

    /// <summary>The most recent cycle, or null before the first one.</summary>
    public Gate3LoopCycle? Last
    {
        get { lock (_gate) return _last; }
    }

    /// <summary>Raised after each cycle, on the pump's thread.</summary>
    public event Action<Gate3LoopCycle>? CycleCompleted;

    /// <summary>Starts the pump. Calling it twice is a no-op, not a second pump.</summary>
    public void Start(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_pump is not null)
            return;

        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _logger.Info("Gate 3 decision loop started.", new Dictionary<string, object?>
        {
            ["intervalMs"] = (long)_interval.TotalMilliseconds,
            ["acting"] = _orchestrator.CanExecute,
            ["maxObservationAgeMs"] = (long)_orchestrator.MaxObservationAge.TotalMilliseconds
        });
        _pump = PumpAsync(_cancellation.Token);
    }

    /// <summary>
    /// Runs exactly one cycle.
    /// </summary>
    /// <remarks>
    /// Public because a single deliberate cycle is what an operator wants first,
    /// and because a test can then drive the loop without racing a timer.
    /// </remarks>
    public async Task<Gate3LoopCycle> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        Gate3WorldState state;
        try
        {
            state = await _source.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A source that throws is a source that read nothing. Reporting that as
            // an unknown world state keeps the loop alive and keeps the reason.
            state = Gate3WorldState.Unobserved($"world_state_source_failed:{ex.GetType().Name}");
        }

        DateTime now = _clock.GetUtcNow().UtcDateTime;
        Gate3CycleResult result = await _orchestrator.ExecuteCycleAsync(state, cancellationToken).ConfigureAwait(false);

        var cycle = new Gate3LoopCycle(
            AtUtc: now,
            Outcome: result.Outcome,
            Summary: result.Summary,
            SelectedAction: result.SelectedAction,
            Hp: state.Hp,
            MaxHp: state.MaxHp,
            Mp: state.Mp,
            HasTarget: state.HasTarget,
            ObservationAge: state.AgeAt(now),
            WouldHaveActed: result.Outcome is CycleOutcome.Confirmed or CycleOutcome.Unverified or CycleOutcome.Failed);

        bool outcomeChanged;
        lock (_gate)
        {
            outcomeChanged = _last is null || _last.Outcome != result.Outcome;
            _last = cycle;
            _cycles++;
            _outcomes[result.Outcome] = _outcomes.GetValueOrDefault(result.Outcome) + 1;
        }

        // Transitions only. At two cycles a second a line per cycle is a line
        // nobody reads, and the same refusal repeated says nothing the first one
        // did not -- but the moment it changes is exactly what an operator running
        // headless needs to see.
        if (outcomeChanged)
        {
            _logger.Info("Gate 3 decision.", new Dictionary<string, object?>
            {
                ["outcome"] = result.Outcome.ToString(),
                ["action"] = result.SelectedAction.ToString(),
                ["hp"] = state.Hp.HasValue ? state.Hp.Value : null,
                ["maxHp"] = state.MaxHp.HasValue ? state.MaxHp.Value : null,
                ["observationAgeMs"] = cycle.ObservationAge is { } age ? (long)age.TotalMilliseconds : null,
                ["summary"] = result.Summary
            });
        }

        CycleCompleted?.Invoke(cycle);
        return cycle;
    }

    /// <summary>What to show the operator.</summary>
    public Gate3LoopView Describe()
    {
        Gate3LoopCycle? last;
        long cycles;
        ImmutableArray<KeyValuePair<string, long>> counts;
        lock (_gate)
        {
            last = _last;
            cycles = _cycles;
            counts = _outcomes
                .OrderBy(entry => entry.Key)
                .Select(entry => new KeyValuePair<string, long>(entry.Key.ToString(), entry.Value))
                .ToImmutableArray();
        }

        ClassifiedValue<bool> running = ClassifiedValue<bool>.Derived(IsRunning);
        ClassifiedValue<bool> acting = ClassifiedValue<bool>.Derived(ActingEnabled);

        if (last is null)
        {
            const string reason = "no_cycle_run_yet";
            return new Gate3LoopView(
                running,
                ClassifiedValue<long>.Derived(cycles),
                ClassifiedValue<string>.Unknown(reason),
                ClassifiedValue<string>.Unknown(reason),
                ClassifiedValue<string>.Unknown(reason),
                ClassifiedValue<int>.Unknown(reason),
                ClassifiedValue<int>.Unknown(reason),
                ClassifiedValue<double>.Unknown(reason),
                acting,
                counts);
        }

        return new Gate3LoopView(
            running,
            ClassifiedValue<long>.Derived(cycles),
            ClassifiedValue<string>.Derived(last.Outcome.ToString(), last.AtUtc),
            ClassifiedValue<string>.Derived(last.SelectedAction.ToString(), last.AtUtc),
            ClassifiedValue<string>.Derived(last.Summary, last.AtUtc),
            // The vitals keep the classification the provider gave them; the loop
            // reports what it planned on, it does not reclassify it.
            last.Hp,
            last.MaxHp,
            last.ObservationAge is { } age
                ? ClassifiedValue<double>.Derived(Math.Round(age.TotalSeconds, 2), last.AtUtc)
                : ClassifiedValue<double>.Unknown(last.Hp.FailureReason ?? "nothing_observed"),
            acting,
            counts);
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                await RunOnceAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stopping is not a fault.
        }
        catch (Exception ex)
        {
            // The pump dying silently would leave a Control Panel showing a loop
            // that stopped thinking half an hour ago and still says "running".
            _logger.Error("Gate 3 decision loop stopped on an unhandled fault.", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_cancellation is not null)
            await _cancellation.CancelAsync().ConfigureAwait(false);

        if (_pump is not null)
        {
            try
            {
                await _pump.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cancellation?.Dispose();
    }
}
