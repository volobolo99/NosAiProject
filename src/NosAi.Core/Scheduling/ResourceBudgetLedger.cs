namespace NosAi.Core.Scheduling;

/// <summary>
/// The minimal, hardware-agnostic seam this budget policy needs from "how
/// much CPU/GPU/RAM/VRAM is available right now" -- deliberately independent
/// of any concrete hardware-capability contract (in particular, independent
/// of whatever agent A1 defines under src/NosAi.Core/Hardware/, which did
/// not exist yet when this module was authored).
///
/// A6 integration note: whatever type A1's hardware profiler exposes for a
/// live capability snapshot should get a small adapter that implements this
/// interface (projecting detected CPU/GPU/RAM/VRAM headroom into a
/// <see cref="ResourceCost"/>), rather than this module taking a dependency
/// on that concrete type. That keeps AI-budget policy decoupled from
/// hardware-detection mechanics -- the policy only needs a number, not how
/// it was measured.
/// </summary>
public interface IResourceBudgetSource
{
    /// <summary>The current total resource capacity (not the remaining headroom -- see <see cref="ResourceBudgetLedger.Available"/> for headroom).</summary>
    ResourceCost CurrentCapacity { get; }
}

/// <summary>
/// A thread-safe capacity/commitment ledger: the concrete "available budget"
/// that <see cref="InferenceBudgetScheduler"/> checks a job's estimated cost
/// against before admitting it, and that a caller returns resources to on
/// completion. All bookkeeping is O(1); the lock is held only across that
/// bookkeeping, never across an inference call.
/// </summary>
public sealed class ResourceBudgetLedger
{
    private readonly object _gate = new();
    private readonly IResourceBudgetSource? _liveSource;
    private ResourceCost _capacity;
    private ResourceCost _committed;

    public ResourceBudgetLedger(ResourceCost initialCapacity, IResourceBudgetSource? liveSource = null)
    {
        if (!initialCapacity.IsNonNegative)
            throw new ArgumentOutOfRangeException(nameof(initialCapacity), "Capacity must be non-negative in every component.");
        _capacity = initialCapacity;
        _committed = ResourceCost.Zero;
        _liveSource = liveSource;
    }

    /// <summary>Remaining headroom (capacity minus committed). Can be negative under a live capacity shrink (e.g. thermal throttling) -- see <see cref="ResourceCost"/> remarks; that is a legitimate "over budget" state, not an error.</summary>
    public ResourceCost Available
    {
        get { lock (_gate) return _capacity.Subtract(_committed); }
    }

    public ResourceCost Capacity
    {
        get { lock (_gate) return _capacity; }
    }

    public ResourceCost Committed
    {
        get { lock (_gate) return _committed; }
    }

    /// <summary>
    /// Pulls a fresh capacity reading from the injected <see cref="IResourceBudgetSource"/>
    /// (e.g. a hardware-telemetry adapter reporting reduced headroom under
    /// thermal/power pressure per docs/HARDWARE_PROFILE_ASUS_NITRO_V16.md S:9).
    /// A no-op when no source was supplied.
    /// </summary>
    public void RefreshCapacityFromSource()
    {
        if (_liveSource is null)
            return;
        UpdateCapacity(_liveSource.CurrentCapacity);
    }

    /// <summary>Replaces total capacity outright (e.g. a hardware-detected or telemetry-driven capacity change). Existing commitments are preserved, which may push <see cref="Available"/> negative -- callers must re-check headroom, not assume it stays non-negative.</summary>
    public void UpdateCapacity(ResourceCost newCapacity)
    {
        if (!newCapacity.IsNonNegative)
            throw new ArgumentOutOfRangeException(nameof(newCapacity), "Capacity must be non-negative in every component.");
        lock (_gate) _capacity = newCapacity;
    }

    /// <summary>Atomically reserves <paramref name="cost"/> if, and only if, it fits within current headroom. Returns false (never throws, never blocks) when it does not fit -- the caller is expected to apply an explicit fallback, not retry in a loop.</summary>
    public bool TryReserve(ResourceCost cost)
    {
        if (!cost.IsNonNegative)
            throw new ArgumentOutOfRangeException(nameof(cost), "Cannot reserve a negative resource cost.");

        lock (_gate)
        {
            ResourceCost available = _capacity.Subtract(_committed);
            if (!cost.IsWithin(available))
                return false;
            _committed = _committed.Add(cost);
            return true;
        }
    }

    /// <summary>Releases a previously reserved cost. Throws on an unbalanced reserve/release pair (releasing more than is currently committed) rather than silently clamping to zero -- a double release is a caller bug and must surface, not be masked.</summary>
    public void Release(ResourceCost cost)
    {
        if (!cost.IsNonNegative)
            throw new ArgumentOutOfRangeException(nameof(cost), "Cannot release a negative resource cost.");

        lock (_gate)
        {
            ResourceCost next = _committed.Subtract(cost);
            if (!next.IsNonNegative)
                throw new InvalidOperationException(
                    $"Release({cost}) exceeds currently committed cost ({_committed}); reserve/release calls are unbalanced.");
            _committed = next;
        }
    }
}
