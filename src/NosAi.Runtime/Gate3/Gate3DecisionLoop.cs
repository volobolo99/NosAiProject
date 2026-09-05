using System.Collections.Immutable;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Observability;
using NosAi.Core.Cognitive;

namespace NosAi.Runtime.Gate3;

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
        running = Running.ToWire(), cyclesRun = CyclesRun.ToWire(), lastOutcome = LastOutcome.ToWire(),
        lastAction = LastAction.ToWire(), lastSummary = LastSummary.ToWire(), lastHp = LastHp.ToWire(),
        lastMaxHp = LastMaxHp.ToWire(), lastObservationAgeSeconds = LastObservationAgeSeconds.ToWire(),
        actingEnabled = ActingEnabled.ToWire(), outcomeCounts = OutcomeCounts.ToDictionary(x => x.Key, x => x.Value)
    };

    public static Gate3LoopView NotConfigured()
    {
        const string reason = "decision_loop_not_configured";
        return new(ClassifiedValue<bool>.Derived(false), ClassifiedValue<long>.Unknown(reason),
            ClassifiedValue<string>.Unknown(reason), ClassifiedValue<string>.Unknown(reason),
            ClassifiedValue<string>.Unknown(reason), ClassifiedValue<int>.Unknown(reason),
            ClassifiedValue<int>.Unknown(reason), ClassifiedValue<double>.Unknown(reason),
            ClassifiedValue<bool>.Derived(false), ImmutableArray<KeyValuePair<string, long>>.Empty);
    }
}

public sealed class Gate3DecisionLoop : IAsyncDisposable
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(500);

    private readonly IWorldStateSource _source;
    private readonly Gate3ExecutionOrchestrator _orchestrator;
    private readonly TimeSpan _interval;
    private readonly IRuntimeLogger _logger;
    private readonly TimeProvider _clock;
    private readonly ICognitiveObservabilitySink? _cognitive;
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
        TimeProvider? clock = null,
        ICognitiveObservabilitySink? cognitive = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _interval = interval ?? DefaultInterval;
        if (_interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        _clock = clock ?? TimeProvider.System;
        _cognitive = cognitive;
    }

    public Gate3ExecutionOrchestrator Orchestrator => _orchestrator;
    public bool IsRunning => _pump is { IsCompleted: false };
    public bool ActingEnabled => _orchestrator.CanExecute;
    public Gate3LoopCycle? Last { get { lock (_gate) return _last; } }
    public event Action<Gate3LoopCycle>? CycleCompleted;

    public void Start(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_pump is not null) return;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _logger.Info("Gate 3 decision loop started.", new Dictionary<string, object?>
        {
            ["intervalMs"] = (long)_interval.TotalMilliseconds,
            ["acting"] = _orchestrator.CanExecute,
            ["maxObservationAgeMs"] = (long)_orchestrator.MaxObservationAge.TotalMilliseconds
        });
        _pump = PumpAsync(_cancellation.Token);
    }

    public async Task<Gate3LoopCycle> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        string cycleId = Guid.NewGuid().ToString("N");
        DateTime now = _clock.GetUtcNow().UtcDateTime;
        await PublishNode(cycleId, CognitiveNodeKind.Sensors, CognitiveNodeStatus.Running, "Acquisizione osservazione", "Lettura World State reale", 0.0, now, cancellationToken);

        Gate3WorldState state;
        try
        {
            state = await _source.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            state = Gate3WorldState.Unobserved($"world_state_source_failed:{ex.GetType().Name}");
        }

        now = _clock.GetUtcNow().UtcDateTime;
        bool observed = state.HasVitals;
        double observationConfidence = observed ? 1.0 : 0.0;
        await PublishNode(cycleId, CognitiveNodeKind.Sensors,
            observed ? CognitiveNodeStatus.Completed : CognitiveNodeStatus.Unknown,
            observed ? "Osservazione acquisita" : "World State UNKNOWN", observed ? "vitals_present" : "vitals_unobserved",
            observationConfidence, now, cancellationToken);
        await PublishNode(cycleId, CognitiveNodeKind.TemporalFusion, CognitiveNodeStatus.Completed,
            "Fusione temporale", state.AgeAt(now)?.ToString() ?? "observation_age_unknown", observationConfidence, now, cancellationToken);
        await PublishNode(cycleId, CognitiveNodeKind.BeliefState, observed ? CognitiveNodeStatus.Completed : CognitiveNodeStatus.Unknown,
            observed ? "Belief State aggiornato" : "Belief State UNKNOWN", observed ? "classified_world_state" : "no_world_state", observationConfidence, now, cancellationToken);
        await PublishNode(cycleId, CognitiveNodeKind.WorldModel, observed ? CognitiveNodeStatus.Completed : CognitiveNodeStatus.Unknown,
            observed ? "World Model aggiornato" : "World Model UNKNOWN", observed ? "gate3_world_state" : "unobserved", observationConfidence, now, cancellationToken);
        await PublishNode(cycleId, CognitiveNodeKind.Memory, CognitiveNodeStatus.Unknown,
            "Memoria non richiesta dal ciclo Gate3", "no_memory_provider_bound", 0.0, now, cancellationToken);
        await PublishNode(cycleId, CognitiveNodeKind.Attention, CognitiveNodeStatus.Completed,
            "Attenzione focalizzata", observed ? "vitals_and_world_state" : "no_observable_state", observationConfidence, now, cancellationToken);
        await PublishNode(cycleId, CognitiveNodeKind.Prediction, CognitiveNodeStatus.Unknown,
            "Predizione esplicita non disponibile", "orchestrator_does_not_expose_prediction_trace", 0.0, now, cancellationToken);
        await PublishNode(cycleId, CognitiveNodeKind.Goal, CognitiveNodeStatus.Completed,
            "Obiettivo valutato", "goal_state_owned_by_orchestrator", observed ? 1.0 : 0.0, now, cancellationToken);
        await PublishNode(cycleId, CognitiveNodeKind.UtilityRisk, CognitiveNodeStatus.Completed,
            "Utility/Risk valutati", "orchestrator_ranking", observed ? 1.0 : 0.0, now, cancellationToken);
        await PublishNode(cycleId, CognitiveNodeKind.Planner, CognitiveNodeStatus.Running,
            "Pianificazione candidati", "Gate3ExecutionOrchestrator", observed ? 1.0 : 0.0, now, cancellationToken);

        Gate3CycleResult result = await _orchestrator.ExecuteCycleAsync(state, cancellationToken).ConfigureAwait(false);
        now = _clock.GetUtcNow().UtcDateTime;
        bool selected = result.SelectedAction != ActionType.None;
        var terminalStatus = result.Outcome switch
        {
            CycleOutcome.Confirmed => CognitiveNodeStatus.Completed,
            CycleOutcome.Unverified => CognitiveNodeStatus.Unknown,
            CycleOutcome.Failed => CognitiveNodeStatus.Failed,
            CycleOutcome.ExecutionDisabled => CognitiveNodeStatus.Rejected,
            CycleOutcome.NoCandidate => CognitiveNodeStatus.Completed,
            _ => CognitiveNodeStatus.Unknown
        };
        await PublishNode(cycleId, CognitiveNodeKind.Planner, selected ? CognitiveNodeStatus.Completed : terminalStatus,
            selected ? "Piano selezionato" : "Nessun piano applicabile", result.Summary, selected ? 1.0 : 0.0, now, cancellationToken);
        await PublishNode(cycleId, CognitiveNodeKind.CandidatePlan, selected ? CognitiveNodeStatus.Completed : terminalStatus,
            selected ? result.SelectedAction.ToString() : "Candidate Plan UNKNOWN", result.Summary, selected ? 1.0 : 0.0, now, cancellationToken);
        await PublishNode(cycleId, CognitiveNodeKind.Guard,
            result.Outcome == CycleOutcome.ExecutionDisabled ? CognitiveNodeStatus.Rejected : CognitiveNodeStatus.Completed,
            "Guard valutato", result.Outcome.ToString(), selected ? 1.0 : 0.0, now, cancellationToken);
        await PublishNode(cycleId, CognitiveNodeKind.Safety,
            result.Outcome == CycleOutcome.ExecutionDisabled ? CognitiveNodeStatus.Rejected : CognitiveNodeStatus.Completed,
            "Safety valutata", result.Outcome.ToString(), selected ? 1.0 : 0.0, now, cancellationToken);
        await PublishNode(cycleId, CognitiveNodeKind.Execute,
            result.Outcome == CycleOutcome.ExecutionDisabled ? CognitiveNodeStatus.Rejected : terminalStatus,
            result.Outcome == CycleOutcome.ExecutionDisabled ? "Esecuzione bloccata" : "Esecuzione valutata", result.Summary,
            result.Outcome == CycleOutcome.Confirmed ? 1.0 : 0.0, now, cancellationToken);
        await PublishNode(cycleId, CognitiveNodeKind.Verify, terminalStatus,
            result.Outcome == CycleOutcome.Confirmed ? "Azione verificata" : "Verifica non confermata", result.Summary,
            result.Outcome == CycleOutcome.Confirmed ? 1.0 : 0.0, now, cancellationToken);
        await PublishNode(cycleId, CognitiveNodeKind.Reobserve, observed ? CognitiveNodeStatus.Completed : CognitiveNodeStatus.Unknown,
            observed ? "Ri-osservazione disponibile" : "Ri-osservazione UNKNOWN", observed ? "world_state" : "unobserved", observationConfidence, now, cancellationToken);

        var cycle = new Gate3LoopCycle(now, result.Outcome, result.Summary, result.SelectedAction,
            state.Hp, state.MaxHp, state.Mp, state.HasTarget, state.AgeAt(now),
            result.Outcome is CycleOutcome.Confirmed or CycleOutcome.Unverified or CycleOutcome.Failed);
        lock (_gate)
        {
            _last = cycle;
            _cycles++;
            _outcomes[result.Outcome] = _outcomes.GetValueOrDefault(result.Outcome) + 1;
        }
        CycleCompleted?.Invoke(cycle);
        return cycle;
    }

    private async ValueTask PublishNode(string cycleId, CognitiveNodeKind node, CognitiveNodeStatus status,
        string summary, string? evidence, double confidence, DateTime occurredAt, CancellationToken token)
    {
        if (_cognitive is null) return;
        await _cognitive.PublishAsync(new CognitiveTraceEvent(
            Guid.NewGuid().ToString("N"), cycleId, node, status, "gate3.node", summary, evidence,
            Math.Clamp(confidence, 0d, 1d), new DateTimeOffset(occurredAt, TimeSpan.Zero), 0), token).ConfigureAwait(false);
    }

    public Gate3LoopView Describe()
    {
        Gate3LoopCycle? last; long cycles; ImmutableArray<KeyValuePair<string, long>> counts;
        lock (_gate)
        {
            last = _last; cycles = _cycles;
            counts = _outcomes.OrderBy(x => x.Key).Select(x => new KeyValuePair<string, long>(x.Key.ToString(), x.Value)).ToImmutableArray();
        }
        if (last is null)
            return new(ClassifiedValue<bool>.Derived(IsRunning), ClassifiedValue<long>.Derived(cycles),
                ClassifiedValue<string>.Unknown("no_cycle_run_yet"), ClassifiedValue<string>.Unknown("no_cycle_run_yet"),
                ClassifiedValue<string>.Unknown("no_cycle_run_yet"), ClassifiedValue<int>.Unknown("no_cycle_run_yet"),
                ClassifiedValue<int>.Unknown("no_cycle_run_yet"), ClassifiedValue<double>.Unknown("no_cycle_run_yet"),
                ClassifiedValue<bool>.Derived(ActingEnabled), counts);
        return new(ClassifiedValue<bool>.Derived(IsRunning), ClassifiedValue<long>.Derived(cycles),
            ClassifiedValue<string>.Derived(last.Outcome.ToString(), last.AtUtc), ClassifiedValue<string>.Derived(last.SelectedAction.ToString(), last.AtUtc),
            ClassifiedValue<string>.Derived(last.Summary, last.AtUtc), last.Hp, last.MaxHp,
            last.ObservationAge is { } age ? ClassifiedValue<double>.Derived(Math.Round(age.TotalSeconds, 2), last.AtUtc) : ClassifiedValue<double>.Unknown("observation_age_unknown"),
            ClassifiedValue<bool>.Derived(ActingEnabled), counts);
    }

    private async Task PumpAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(_interval);
        try { while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false)) await RunOnceAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.Error("Gate 3 decision loop stopped on an unhandled fault.", ex); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_cancellation is not null) await _cancellation.CancelAsync().ConfigureAwait(false);
        if (_pump is not null) { try { await _pump.ConfigureAwait(false); } catch (OperationCanceledException) { } }
        _cancellation?.Dispose();
    }
}
