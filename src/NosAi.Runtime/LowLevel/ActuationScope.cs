using System.Globalization;

namespace NosAi.Runtime.LowLevel;

/// <summary>What an actuation scope believes may currently be held down.</summary>
internal readonly record struct HeldInput(bool IsKey, ushort VirtualKey, MouseButton Button);

/// <summary>
/// One authorised act, from the commit request that authorised it to the release of
/// anything it pressed.
/// </summary>
/// <remarks>
/// <para>
/// <b>DOMAIN-17 made into a shape.</b> An input program has at most one irreversible
/// step and it is the last; a scope is that program. Cursor moves inside it are
/// reversible and are checked against the policy and against the scope being open; the
/// irreversible ones — click, key, wheel — are revalidated in full against the commit
/// point in the instant before they are emitted.
/// </para>
/// <para>
/// <b>The abort machine.</b> Every press the gate lets through is recorded here before
/// it is issued and struck off after it returns. So anything that fails part-way, or
/// is interrupted by a refusal, leaves a record; <see cref="Abort"/> and
/// <see cref="Dispose"/> release exactly what the record holds, in reverse order. The
/// release is attempted even when the policy has since been switched off, and that is
/// the one deliberate exception to the gate: it can only emit an <i>up</i> for
/// something already recorded as pressed, so it cannot press anything, and a key left
/// down outlives the process that pressed it while a refused release does not.
/// </para>
/// <para>
/// A scope that has aborted stays aborted. Reusing it would let a caller retry past a
/// refusal by holding on to the object that was refused.
/// </para>
/// </remarks>
public sealed class ActuationScope : IDisposable
{
    /// <summary>Reported by a call made after this scope was aborted.</summary>
    public const string AlreadyAbortedReason = "actuation_scope_aborted";

    private readonly GatedInputBackend _gate;
    private readonly List<HeldInput> _held = new(4);
    private readonly object _lock = new();
    private bool _aborted;
    private bool _closed;

    internal ActuationScope(GatedInputBackend gate, CommitRequest request, ActuationAuthority authority)
    {
        _gate = gate;
        Request = request;
        Authority = authority;
    }

    /// <summary>What was authorised, and against which geometry and scale.</summary>
    public CommitRequest Request { get; }

    /// <summary>
    /// Under whose authority this act is being emitted (ADR-0020 § 2).
    /// </summary>
    /// <remarks>
    /// Carried on the scope rather than passed along beside it, so the audit for an act
    /// reads it from the same object that knows what the act pressed. A scope that could
    /// not name its authority would be exactly the unattributable emission the record
    /// forbids.
    /// </remarks>
    public ActuationAuthority Authority { get; }

    /// <summary>True once this scope has refused and released.</summary>
    public bool IsAborted
    {
        get { lock (_lock) return _aborted; }
    }

    /// <summary>Why it aborted, or null.</summary>
    public string? AbortReason { get; private set; }

    /// <summary>Presses this scope believes are still down.</summary>
    public int HeldCount
    {
        get { lock (_lock) return _held.Count; }
    }

    /// <summary>Whether a call may still proceed.</summary>
    internal bool TryEnter(out string? refusalReason)
    {
        lock (_lock)
        {
            if (_aborted || _closed)
            {
                refusalReason = AbortReason ?? AlreadyAbortedReason;
                return false;
            }
        }

        refusalReason = null;
        return true;
    }

    internal void RecordKey(ushort virtualKey)
    {
        lock (_lock)
            _held.Add(new HeldInput(true, virtualKey, default));
    }

    internal void RecordButton(MouseButton button)
    {
        lock (_lock)
            _held.Add(new HeldInput(false, 0, button));
    }

    internal void Forget(HeldInput input)
    {
        lock (_lock)
        {
            int index = _held.LastIndexOf(input);
            if (index >= 0)
                _held.RemoveAt(index);
        }
    }

    /// <summary>
    /// Refuses the rest of the act and releases anything still held.
    /// </summary>
    /// <remarks>
    /// Called by the gate when a condition fails mid-program, and by the operator's
    /// suspend, which § 2.3 requires to stop everything immediately rather than at the
    /// end of some cycle.
    /// </remarks>
    public void Abort(string reason)
    {
        lock (_lock)
        {
            if (_aborted)
                return;

            _aborted = true;
            AbortReason = reason;
        }

        ReleaseEverything();
    }

    /// <summary>Closes the scope, releasing anything still recorded as pressed.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_closed)
                return;

            _closed = true;
        }

        ReleaseEverything();
        _gate.CloseScope(this);
    }

    private void ReleaseEverything()
    {
        List<HeldInput> pending;
        lock (_lock)
        {
            if (_held.Count == 0)
                return;

            pending = new List<HeldInput>(_held);
            _held.Clear();
        }

        // Reverse order, so modifiers come up after the key they were held around,
        // exactly as a completed press would have released them.
        for (int i = pending.Count - 1; i >= 0; i--)
            _gate.ReleaseHeld(pending[i]);
    }

    internal string Describe() => string.Create(CultureInfo.InvariantCulture,
        $"scope at {Request.ScreenX},{Request.ScreenY} holding {HeldCount} under {Authority.Describe()}");
}
