// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// Gate 2 — Motore integrato: osservazione → world model → bus → persistenza → delta
// ============================================================================

using System;
using System.Threading.Tasks;

namespace NosAi.Runtime.Gate2;

/// <summary>
/// Composition root of the Gate 2 module, wiring the canonical pipeline:
/// observation batch → world model fold → bounded event bus → WAL persistence
/// (events, session, trajectory) → per-consumer delta sync.
/// </summary>
/// <remarks>
/// Follows the house convention of <c>Gate5IntegratedEngine</c>: the module
/// composes itself and is certified by its own suite; cross-gate orchestration
/// belongs to the master host, not here. The engine observes and records — it
/// never authorizes or executes actions.
/// </remarks>
public sealed class Gate2IntegratedEngine : IAsyncDisposable
{
    private readonly Gate2RuntimeEngine _core;
    private readonly Gate2SessionStore _sessionStore;
    private readonly DeltaSyncTracker _deltaSync;
    private readonly long _sessionRowId;
    private bool _disposed;

    public Gate2RuntimeEngine Core => _core;
    public Gate2SessionStore SessionStore => _sessionStore;
    public DeltaSyncTracker DeltaSync => _deltaSync;
    public long SessionRowId => _sessionRowId;
    public WorldStateSnapshot CurrentState => _core.CurrentState;

    public Gate2IntegratedEngine(string dbPath = "data/nosai_telemetry.db",
        string sessionId = "GATE2_ACTIVE_SESSION", int deltaHistoryCapacity = 32)
    {
        _core = new Gate2RuntimeEngine(dbPath, sessionId);
        try
        {
            _sessionStore = new Gate2SessionStore(new SqliteStoragePolicy(dbPath, batchIntervalMs: 50, maxBatchSize: 200));
            _deltaSync = new DeltaSyncTracker(deltaHistoryCapacity);
            _sessionRowId = _sessionStore.OpenSession(sessionId, DateTime.UtcNow);
        }
        catch
        {
            // Partial construction must not leak the already-open core stores.
            _core.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    /// <summary>
    /// Folds one observation batch into the canonical state and fans it out:
    /// audit event on the bounded bus (via the core engine), trajectory row in the
    /// WAL store, and a new baseline for delta consumers. Returns the new snapshot.
    /// </summary>
    public WorldStateSnapshot ObserveFrame(ObservationBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var snapshot = _core.UpdateWorldState(state => WorldModelReducer.Fold(state, batch));
        _sessionStore.EnqueueFrame(_sessionRowId, snapshot);
        _deltaSync.TrackFrame(snapshot);
        return snapshot;
    }

    /// <summary>Bounded world context for VRAM-constrained decision providers.</summary>
    public SlimmedWorldContext SlimCurrentContext(int maxEntities = 8) =>
        WorldContextSlimmer.Slim(_core.CurrentState, maxEntities);

    public void RegisterDeltaConsumer(string consumerId) => _deltaSync.RegisterConsumer(consumerId);

    public SyncUpdate ProduceUpdate(string consumerId) => _deltaSync.ProduceUpdate(consumerId);

    public void AcknowledgeUpdate(string consumerId, ulong frameIndex) => _deltaSync.Acknowledge(consumerId, frameIndex);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        // Seal the session first so the recorded frame count reflects every row the
        // store managed to persist, then release the stores.
        try { _sessionStore.CloseSession(_sessionRowId, DateTime.UtcNow); }
        catch { /* closing telemetry must not mask the disposal path */ }
        await _core.DisposeAsync().ConfigureAwait(false);
        await _sessionStore.DisposeAsync().ConfigureAwait(false);
    }
}
