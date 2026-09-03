using NosAi.Runtime.Contracts;

namespace NosAi.LiveIntegration;

/// <summary>
/// Reuses one read handle on the attached client to produce the map-world
/// observation the snapshot publishes. Observation only: the process is opened
/// for reading, the layout is resolved, and nothing is written, posted, or armed.
/// </summary>
/// <remarks>
/// <para>
/// The signature scan that <see cref="ClientMemorySession.TryAttach"/> runs is
/// paid once per process id, not once per snapshot. A new PID is a different
/// client and the previous handle is dropped; the same PID keeps the session
/// so a 1 Hz snapshot does not re-scan the image every time.
/// </para>
/// <para>
/// Map id and standing cell are read independently. A readable id with an
/// unreadable player is a map id and an UNKNOWN cell, not a guessed origin.
/// </para>
/// </remarks>
public sealed class ClientMapWorldSource : IDisposable
{
    private readonly Func<int?> _processId;
    private readonly object _gate = new();
    private ClientMemorySession? _session;
    private int _sessionPid;
    private bool _disposed;

    /// <param name="processId">
    /// The snapshot's attached client PID, or null when nothing is attached.
    /// Null is <see cref="MemoryMapWorldProvider.ProcessNotAttachedReason"/>,
    /// not an invitation to pick the first windowed process.
    /// </param>
    public ClientMapWorldSource(Func<int?> processId)
    {
        _processId = processId ?? throw new ArgumentNullException(nameof(processId));
    }

    /// <summary>
    /// One pass over map id and standing cell. A missing PID, a failed attach,
    /// or a failed field read is UNKNOWN with that field's own reason.
    /// </summary>
    public MapWorldObservation Read()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_processId() is not int pid || pid <= 0)
            {
                DropSession();
                return MapWorldObservation.Unknown(MemoryMapWorldProvider.ProcessNotAttachedReason);
            }

            if (!TrySession(pid, out ClientMemorySession? session, out string? attachFailure))
                return MapWorldObservation.Unknown(attachFailure ?? MemoryMapWorldProvider.SessionUnavailableReason);

            ClassifiedValue<int> mapId = session!.TryReadMapId(out int id, out string? mapFailure)
                ? ClassifiedValue<int>.Live(id)
                : ClassifiedValue<int>.Unknown(mapFailure ?? MemoryMapWorldProvider.MapIdUnreadable);

            ClassifiedValue<MapPoint> standing;
            if (session.TryReadPlayer(out PlayerObjectReading player, out string? posFailure))
            {
                standing = ClassifiedValue<MapPoint>.Live(new MapPoint(player.X, player.Y));
            }
            else
            {
                standing = ClassifiedValue<MapPoint>.Unknown(posFailure ?? MemoryMapWorldProvider.PlayerUnreadable);
            }

            return new MapWorldObservation(mapId, standing);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            DropSession();
        }
    }

    private bool TrySession(int pid, out ClientMemorySession? session, out string? failureReason)
    {
        if (_session is not null && _sessionPid == pid)
        {
            session = _session;
            failureReason = null;
            return true;
        }

        DropSession();
        if (!ClientMemorySession.TryAttach(out session, out failureReason, pid))
            return false;

        _session = session;
        _sessionPid = pid;
        return true;
    }

    private void DropSession()
    {
        _session?.Dispose();
        _session = null;
        _sessionPid = 0;
    }
}
