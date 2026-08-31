// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// LowLevel — The authorised boundary for input injection
// ============================================================================
//
// The Safety Gate used to live inside NosTaleGameAdapter alone: anyone holding
// RuntimeComponents.InputBackend or .Humanizer injected real input with
// LiveInputEnabled false, bypassing the authorisation entirely. The gate now
// sits at the boundary, where it cannot be stepped around (ADR-0003, "the
// runtime is authoritative for safety, authorization and privileged
// execution").

using System;
using System.Threading;
using NosAi.Runtime.Safety;

namespace NosAi.Runtime.LowLevel;

/// <summary>Why an injection attempt was refused.</summary>
public sealed record InputRefusal(string Reason, DateTime AtUtc);

/// <summary>
/// Wraps a real backend and refuses every injection unless the policy allows it.
/// </summary>
/// <remarks>
/// <para>
/// Fail closed by construction: the decision is taken here, on every call, from
/// the live policy — not cached, not passed in by the caller. A refusal returns
/// false and is counted, so a runtime that silently does nothing is still
/// diagnosable from <see cref="RefusedCount"/> and <see cref="LastRefusal"/>.
/// </para>
/// <para>
/// Read-only queries (<see cref="TryGetCursorPosition"/>) are always allowed:
/// observing the desktop is not actuating it.
/// </para>
/// </remarks>
public sealed class GatedInputBackend : IInputBackend
{
    private readonly IInputBackend _inner;
    private readonly Func<RuntimeSafetyPolicy> _policySource;
    private long _refusedCount;
    private long _allowedCount;
    private InputRefusal? _lastRefusal;

    /// <summary>Injections refused because the policy did not allow them.</summary>
    public long RefusedCount => Interlocked.Read(ref _refusedCount);

    /// <summary>Injections the policy allowed through to the real backend.</summary>
    public long AllowedCount => Interlocked.Read(ref _allowedCount);

    public InputRefusal? LastRefusal => _lastRefusal;

    /// <summary>True only when the policy currently permits live injection.</summary>
    public bool IsLive => _inner.IsLive && _policySource().LiveInputEnabled;

    public GatedInputBackend(IInputBackend inner, RuntimeSafetyPolicy policy)
        : this(inner, () => policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
    }

    /// <summary>
    /// Takes the policy through a delegate so a runtime that flips the switch at
    /// run time is obeyed immediately, with no stale copy to re-inject through.
    /// </summary>
    public GatedInputBackend(IInputBackend inner, Func<RuntimeSafetyPolicy> policySource)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _policySource = policySource ?? throw new ArgumentNullException(nameof(policySource));
    }

    private bool Allowed()
    {
        RuntimeSafetyPolicy policy = _policySource()
            ?? throw new InvalidOperationException("The safety policy source returned null; refusing to inject input.");
        if (policy.LiveInputEnabled)
        {
            Interlocked.Increment(ref _allowedCount);
            return true;
        }
        Interlocked.Increment(ref _refusedCount);
        _lastRefusal = new InputRefusal("live_input_disabled_by_policy", DateTime.UtcNow);
        return false;
    }

    // Observation, not actuation: never gated.
    public bool TryGetCursorPosition(out int x, out int y) => _inner.TryGetCursorPosition(out x, out y);

    public bool MoveRelative(int dx, int dy) => Allowed() && _inner.MoveRelative(dx, dy);

    public bool MoveAbsolute(int x, int y) => Allowed() && _inner.MoveAbsolute(x, y);

    public bool Click(MouseButton button, int delayBetweenDownUpMs = 45)
        => Allowed() && _inner.Click(button, delayBetweenDownUpMs);

    public bool KeyPress(ushort virtualKey, int pressDurationMs = 80, ReadOnlySpan<ushort> modifiers = default)
        => Allowed() && _inner.KeyPress(virtualKey, pressDurationMs, modifiers);

    public bool ScrollWheel(int detents) => Allowed() && _inner.ScrollWheel(detents);
}

/// <summary>
/// Backend that records requests without touching the OS. It is the honest
/// stand-in for tests and dry runs: nothing reaches the desktop, and
/// <see cref="IsLive"/> says so rather than implying a real actuation happened.
/// </summary>
public sealed class RecordingInputBackend : IInputBackend
{
    private readonly List<string> _events = new();
    private int _cursorX;
    private int _cursorY;

    public bool IsLive => false;

    public IReadOnlyList<string> Events => _events;

    public RecordingInputBackend(int cursorX = 0, int cursorY = 0)
    {
        _cursorX = cursorX;
        _cursorY = cursorY;
    }

    public bool TryGetCursorPosition(out int x, out int y)
    {
        x = _cursorX;
        y = _cursorY;
        return true;
    }

    public bool MoveRelative(int dx, int dy)
    {
        _cursorX += dx;
        _cursorY += dy;
        _events.Add($"move-relative:{dx},{dy}");
        return true;
    }

    public bool MoveAbsolute(int x, int y)
    {
        _cursorX = x;
        _cursorY = y;
        _events.Add($"move-absolute:{x},{y}");
        return true;
    }

    public bool Click(MouseButton button, int delayBetweenDownUpMs = 45)
    {
        _events.Add($"click:{button}");
        return true;
    }

    public bool KeyPress(ushort virtualKey, int pressDurationMs = 80, ReadOnlySpan<ushort> modifiers = default)
    {
        string prefix = modifiers.Length == 0 ? string.Empty : string.Join("+", modifiers.ToArray()) + "+";
        _events.Add($"key:{prefix}{virtualKey}");
        return true;
    }

    public bool ScrollWheel(int detents)
    {
        _events.Add($"scroll:{detents}");
        return true;
    }
}
