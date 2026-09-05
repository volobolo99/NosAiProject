using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Temporal;
using NosAi.LiveIntegration;
using NosAi.Runtime.Gate1;
using NosAi.Runtime.Observability;
using NosAi.Runtime.Perception;

namespace NosAi.Runtime.WorldModel.Fusion;

/// <summary>
/// Runtime wiring from the existing Gate 1 observation snapshot into AP-01's
/// Unified World Model (docs/agents/AGENT_COMMAND_REGISTRY.md AP-01/A4:
/// "runtime wiring from existing observation snapshots into the World Model;
/// no contract redesign"). This type owns no new fusion logic of its own: it
/// only calls, in order, the two pure stages A2 and A3 already built --
/// <see cref="GameplayObservationProjector.Project"/> then
/// <see cref="WorldModelTemporalEnricher.Enrich"/> -- against whatever
/// <see cref="Gate1CanonicalSnapshot"/> the caller supplies, and remembers
/// the result across ticks so the next cycle has a "previous" to enrich
/// against.
/// </summary>
/// <remarks>
/// <para>
/// <b>The player-id gap.</b> Building a <see cref="Player"/> fact requires an
/// <see cref="EntityId"/> for the controlled character, but nothing in the
/// current chain from <c>NetworkWorldFeed</c> through
/// <see cref="Gate1BootstrapHost.Capture"/> exposes the player's own live
/// entity id publicly -- it is read internally but never surfaced on
/// <see cref="Gate1CanonicalSnapshot"/> or <see cref="GameplayObservation"/>.
/// Rather than reach into and refactor that chain (explicitly out of scope
/// for this task), every snapshot this loop produces uses the explicit
/// <see cref="UnknownPlayerSentinelId"/> sentinel, the same treatment
/// <see cref="GameplayObservationProjector.UnknownMapSentinelId"/> already
/// gives an unobserved map. Exposing the real id is follow-up work for
/// whichever agent next touches <c>Gate1ObservationChannel</c>/
/// <c>NetworkGameplayProvider</c>/<c>Gate1BootstrapHost</c>.
/// </para>
/// <para>
/// <b>No lock around <see cref="Current"/>.</b> Reads and writes go through
/// <see cref="Volatile"/>, the same treatment
/// <see cref="NosAi.Runtime.WorldModel.WorldModel.Current"/> already gives a
/// single reference field -- adequate because this loop's own pump calls
/// <see cref="RunOnce"/> strictly sequentially; it does not protect a caller
/// that invokes <see cref="RunOnce"/> concurrently from two threads, which
/// nothing in this runtime does.
/// </para>
/// <para>
/// <b>AP-02/A4: optional vision-side vitals.</b> The constructor's
/// <c>visualSource</c> parameter is additive and defaults to <c>null</c>, so
/// every existing caller that does not pass it keeps the exact network-only
/// behavior this type shipped with in AP-01/A4 -- no regression. When
/// supplied, each <see cref="RunOnce"/> call reads one
/// <see cref="VisualObservation"/> after the network-only <c>enriched</c>
/// snapshot is computed, and resolves HP/MP between the two channels via
/// <see cref="VisualObservationFusion.FuseVitals"/> before publishing. A
/// vision source that throws is treated as a missed vision cycle, never as a
/// reason to fail the network cycle: the exception is logged and fusion runs
/// against <see cref="VisualObservation.Unobserved(string, DateTime?)"/>
/// instead, which -- carrying only UNKNOWN fields -- resolves to the network
/// reading unchanged. The network channel stays autonomous either way.
/// </para>
/// </remarks>
public sealed class WorldModelFusionLoop : IAsyncDisposable
{
    /// <summary>
    /// Placeholder <see cref="EntityId"/> used for the controlled character
    /// until the observation chain exposes the real one (see class remarks).
    /// Not fabricated data: it is a fixed, documented sentinel, the same kind
    /// <see cref="WorldModelSnapshot.Unknown"/> already uses for its own
    /// placeholder player.
    /// </summary>
    public const string UnknownPlayerSentinelId = "unknown-player";

    /// <summary>Default tick interval, matching <see cref="NosAi.Runtime.Gate3.Gate3DecisionLoop.DefaultInterval"/>.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Default freshness window passed to <see cref="WorldModelTemporalEnricher.Enrich"/>.
    /// </summary>
    /// <remarks>
    /// Matches <c>NosAi.LiveIntegration.NetworkGameplayProvider.DefaultMaxVitalsAge</c>:
    /// the position this loop decays arrives on the same wire cadence as the
    /// vitals that constant already governs, so there is no reason for the two
    /// windows to disagree about how long a reading stays trustworthy.
    /// </remarks>
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Default maximum gap passed to <see cref="WorldModelTemporalEnricher.Enrich"/>
    /// for velocity estimation.
    /// </summary>
    /// <remarks>
    /// Equal to <see cref="DefaultMaxAge"/> by deliberate choice: a position
    /// pair spread further apart than the freshness window itself is already
    /// stale enough that the "current" velocity it would imply is not a
    /// reliable estimate either, so both bounds move together rather than one
    /// silently outliving the other.
    /// </remarks>
    public static readonly TimeSpan DefaultMaxObservationGap = TimeSpan.FromSeconds(5);

    /// <summary>Reason carried by the synthetic <see cref="VisualObservation"/> fused in for a missed vision cycle (the source threw).</summary>
    public const string VisualSourceThrewReason = "visual_source_threw";

    private readonly Func<Gate1CanonicalSnapshot> _source;
    private readonly IRuntimeLogger _logger;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _maxAge;
    private readonly TimeSpan _maxObservationGap;
    private readonly TimeProvider _clock;
    private readonly EntityId _playerId;
    private readonly Func<VisualObservation>? _visualSource;

    private WorldModelSnapshot _current = WorldModelSnapshot.Unknown("no_prior_fusion_cycle");
    private long _version;
    private CancellationTokenSource? _cancellation;
    private Task? _pump;
    private bool _disposed;

    /// <param name="source">Reads a fresh <see cref="Gate1CanonicalSnapshot"/> on demand. In production this is <c>Gate1BootstrapHost.Capture</c>; tests supply a stub.</param>
    /// <param name="logger">Same logger type/style as <see cref="NosAi.Runtime.Gate3.Gate3DecisionLoop"/>.</param>
    /// <param name="interval">How often the pump ticks. Defaults to <see cref="DefaultInterval"/>. Must be positive.</param>
    /// <param name="maxAge">Passed through to <see cref="WorldModelTemporalEnricher.Enrich"/>. Defaults to <see cref="DefaultMaxAge"/>. Must be positive.</param>
    /// <param name="maxObservationGap">Passed through to <see cref="WorldModelTemporalEnricher.Enrich"/>. Defaults to <see cref="DefaultMaxObservationGap"/>. Must be positive.</param>
    /// <param name="clock">Time source for the pump's own ticks. Defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="playerId">Overrides <see cref="UnknownPlayerSentinelId"/>, for a future caller that does obtain the real id.</param>
    /// <param name="visualSource">
    /// Reads one <see cref="VisualObservation"/> on demand (in production, e.g.
    /// <c>NosAi.Runtime.Perception.ScreenVitalsCapture.Capture</c>). Optional and
    /// <c>null</c> by default: with no source, <see cref="RunOnce"/> behaves
    /// exactly as it did before this parameter existed -- network-only, no
    /// vitals fusion. See the class remarks, "AP-02/A4: optional vision-side
    /// vitals".
    /// </param>
    public WorldModelFusionLoop(
        Func<Gate1CanonicalSnapshot> source,
        IRuntimeLogger logger,
        TimeSpan? interval = null,
        TimeSpan? maxAge = null,
        TimeSpan? maxObservationGap = null,
        TimeProvider? clock = null,
        EntityId? playerId = null,
        Func<VisualObservation>? visualSource = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _interval = interval ?? DefaultInterval;
        if (_interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval), "A fusion loop interval must be positive.");
        _maxAge = maxAge ?? DefaultMaxAge;
        if (_maxAge <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maxAge), "A freshness window must be positive.");
        _maxObservationGap = maxObservationGap ?? DefaultMaxObservationGap;
        if (_maxObservationGap <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maxObservationGap), "A max observation gap must be positive.");
        _clock = clock ?? TimeProvider.System;
        _playerId = playerId ?? new EntityId(UnknownPlayerSentinelId);
        _visualSource = visualSource;
    }

    /// <summary>The most recently fused snapshot. Starts at <see cref="WorldModelSnapshot.Unknown"/> before the first tick.</summary>
    public WorldModelSnapshot Current => Volatile.Read(ref _current);

    /// <summary>Raised after every successful <see cref="RunOnce"/>, including the loop's own ticks.</summary>
    public event Action<WorldModelSnapshot>? SnapshotFused;

    public bool IsRunning => _pump is { IsCompleted: false };

    /// <summary>
    /// Runs one fusion cycle against an already-captured snapshot: projects
    /// its gameplay observation (or an honest <see cref="GameplayObservation.Unobserved"/>
    /// fallback when none is bound), enriches it against the last stored
    /// snapshot, stores and returns the result. Deliberately takes the
    /// snapshot as a parameter rather than capturing it itself, so it can be
    /// exercised in a unit test without constructing a real
    /// <see cref="Gate1BootstrapHost"/>.
    /// </summary>
    /// <param name="snapshot">A Gate 1 snapshot, e.g. from <c>Gate1BootstrapHost.Capture()</c>.</param>
    /// <param name="nowUtc">The instant this cycle runs at, used for confidence decay and the produced snapshot's own timestamp.</param>
    public WorldModelSnapshot RunOnce(Gate1CanonicalSnapshot snapshot, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Same fallback the existing Gate3WorldState chain uses
        // (Gate1SnapshotWorldStateSource.ReadAsync): prefer the snapshot's own
        // stated reason over a literal, so the two can never disagree about
        // why gameplay is unobserved.
        GameplayObservation gameplay;
        if (snapshot.Client.Gameplay is { } observed)
        {
            gameplay = observed;
        }
        else
        {
            gameplay = GameplayObservation.Unobserved(
                snapshot.Client.GameplayBaseline.FailureReason ?? "gameplay_provider_not_available",
                nowUtc);
        }

        long version = Interlocked.Increment(ref _version);
        WorldModelSnapshot previous = Volatile.Read(ref _current);
        WorldModelSnapshot projected = GameplayObservationProjector.Project(gameplay, _playerId, version, nowUtc);
        WorldModelSnapshot enriched = WorldModelTemporalEnricher.Enrich(previous, projected, nowUtc, _maxAge, _maxObservationGap);

        WorldModelSnapshot result = enriched;
        if (_visualSource is not null)
        {
            VisualObservation visual;
            try
            {
                visual = _visualSource();
            }
            catch (Exception ex)
            {
                // A missed vision cycle, never a reason to fail the network
                // cycle: fuse against an honestly-UNKNOWN observation, which
                // resolves back to the network reading unchanged (see class
                // remarks, "AP-02/A4: optional vision-side vitals").
                _logger.Error("World Model fusion loop's vision source threw; fusing network-only for this cycle.", ex);
                visual = VisualObservation.Unobserved(VisualSourceThrewReason, nowUtc);
            }

            result = VisualObservationFusion.FuseVitals(enriched, visual, nowUtc);
        }

        Volatile.Write(ref _current, result);
        SnapshotFused?.Invoke(result);
        return result;
    }

    /// <summary>Starts the periodic pump. A no-op if already running.</summary>
    public void Start(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_pump is not null) return;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _logger.Info("World Model fusion loop started.", new Dictionary<string, object?>
        {
            ["intervalMs"] = (long)_interval.TotalMilliseconds,
            ["maxAgeMs"] = (long)_maxAge.TotalMilliseconds,
            ["maxObservationGapMs"] = (long)_maxObservationGap.TotalMilliseconds
        });
        _pump = PumpAsync(_cancellation.Token);
    }

    private async Task PumpAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(_interval);
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                RunOnceFromSource();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.Error("World Model fusion loop stopped on an unhandled fault.", ex);
        }
    }

    private void RunOnceFromSource()
    {
        Gate1CanonicalSnapshot snapshot;
        try
        {
            snapshot = _source();
        }
        catch (Exception ex)
        {
            // A capture failure is a missed cycle, not a reason to fabricate a
            // snapshot or to stop the pump: the next tick tries again.
            _logger.Error("World Model fusion loop failed to capture a Gate 1 snapshot; skipping this cycle.", ex);
            return;
        }

        RunOnce(snapshot, _clock.GetUtcNow().UtcDateTime);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_cancellation is not null)
            await _cancellation.CancelAsync().ConfigureAwait(false);
        if (_pump is not null)
        {
            try { await _pump.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _cancellation?.Dispose();
    }
}
