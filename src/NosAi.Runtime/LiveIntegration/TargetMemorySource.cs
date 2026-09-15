using System;

namespace NosAi.LiveIntegration;

public delegate bool TryAttachClientMemorySession(
    out ClientMemorySession? session, out string? failureReason, int processId);

/// <summary>
/// Owns the one <see cref="ClientMemorySession"/> a runtime keeps open, and
/// re-attaches it when the target process id changes. ADR-0021 § 1: this is
/// what lets <see cref="MemoryTargetGameplayProvider"/> read the client's own
/// target pointer instead of only inferring it from the wire.
/// </summary>
public sealed class TargetMemorySource : IDisposable
{
    private readonly TryAttachClientMemorySession _attach;
    private ClientMemorySession? _session;
    private string? _lastFailureReason;
    private bool _disposed;

    public TargetMemorySource(TryAttachClientMemorySession? attach = null)
    {
        _attach = attach ?? ClientMemorySession.TryAttach;
    }

    public TargetPointerReading? Read(int? clientProcessId)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TargetMemorySource));

        if (clientProcessId == null)
        {
            _lastFailureReason = "client_process_not_attached";
            return null;
        }

        if (_session != null && _session.ProcessId != clientProcessId.Value)
        {
            _session.Dispose();
            _session = null;
        }

        if (_session == null)
        {
            if (!_attach(out ClientMemorySession? session, out string? attachFailureReason, clientProcessId.Value))
            {
                _session = null;
                _lastFailureReason = attachFailureReason;
                return null;
            }

            _session = session;
        }

        if (_session!.TryReadTarget(out TargetPointerReading reading, out string? readFailureReason))
        {
            _lastFailureReason = null;
            return reading;
        }

        _lastFailureReason = readFailureReason;
        return null;
    }

    public string? FailureReason() => _lastFailureReason;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _session?.Dispose();
    }
}
