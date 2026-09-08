using System.Diagnostics;
using System.Net;

namespace NosAi.LiveIntegration;

/// <summary>
/// Stage of a client-arrival watch, from "the client process is not running yet"
/// up to "a single remote game session has been identified".
/// </summary>
public enum ClientArrivalStage
{
    /// <summary>No game client process is running yet; the watch keeps waiting.</summary>
    WaitingForProcess = 0,

    /// <summary>Exactly one client process runs, but it exposes no remote TCP session.</summary>
    ProcessWithoutSession = 1,

    /// <summary>More than one client process or more than one remote session exists; none is selected.</summary>
    SessionAmbiguous = 2,

    /// <summary>Exactly one client process exposes exactly one remote game session.</summary>
    SessionIdentified = 3,

    /// <summary>The client process could not be enumerated or the TCP connection table could not be read.</summary>
    ObservationFailed = 4
}

/// <summary>
/// Outcome of a single client-arrival poll: where the watch stands, which process
/// (if any) it looked at, which remote endpoint (if any) it identified and why.
/// </summary>
/// <param name="Stage">Current stage of the arrival watch.</param>
/// <param name="ProcessId">Id of the single observed client process; 0 when no process could be singled out.</param>
/// <param name="Endpoint">Remote endpoint of the single game session; null until the session is identified.</param>
/// <param name="RemoteSessionCount">Number of remote TCP sessions observed for the single client process.</param>
/// <param name="Reason">Machine-readable reason describing why the watch did not identify a session; null when it did.</param>
/// <param name="ObservedUtc">Moment, in UTC, at which the observation was taken.</param>
public sealed record ClientArrivalStatus(
    ClientArrivalStage Stage,
    int ProcessId,
    IPEndPoint? Endpoint,
    int RemoteSessionCount,
    string? Reason,
    DateTime ObservedUtc)
{
    /// <summary>
    /// True when a single remote game session has been identified, which by
    /// construction also means <see cref="Endpoint"/> is not null.
    /// </summary>
    public bool Identified => Stage == ClientArrivalStage.SessionIdentified && Endpoint is not null;
}

/// <summary>
/// Waits for the game client process to appear and then watches the TCP connection
/// table until exactly one remote game session can be identified, without ever
/// picking a session at random when several are present.
/// </summary>
/// <remarks>
/// Ambiguity is never resolved by guessing. When several client processes are open
/// together, or when the single client process exposes more than one remote session,
/// the watch keeps waiting and reports the reason instead of choosing. Selecting one
/// session out of many would present a hypothesis as an observation, and that is the
/// same discipline <see cref="ClientNetworkObserver"/> already applies to
/// <c>Primary</c>, which is only populated when the remote sessions are exactly one.
/// </remarks>
public sealed class ClientArrivalWatcher
{
    /// <summary>
    /// Process names probed, in order, when looking for the game client:
    /// <c>NostaleClientX</c>, <c>NostaleClient</c>, <c>NosTale</c>.
    /// </summary>
    public static readonly string[] DefaultProcessNames = { "NostaleClientX", "NostaleClient", "NosTale" };

    /// <summary>
    /// Default delay between two consecutive polls while waiting for the client.
    /// </summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(250);

    private readonly Func<IReadOnlyList<int>> _processLookup;
    private readonly Func<int, ClientNetworkObservation> _networkLookup;
    private readonly Func<DateTime> _clock;

    /// <summary>
    /// Creates a watcher. The lookup functions are seams so that tests can drive the
    /// watcher without a client running: by default <paramref name="processLookup"/>
    /// enumerates the real client processes and <paramref name="networkLookup"/> reads
    /// the real TCP connection table through <see cref="ClientNetworkObserver"/>.
    /// </summary>
    /// <param name="processLookup">Returns the ids of the running client processes; defaults to real process enumeration.</param>
    /// <param name="networkLookup">Observes the TCP connections of the given process id; defaults to <see cref="ClientNetworkObserver.Observe"/>.</param>
    /// <param name="pollInterval">Delay between consecutive polls; must be greater than zero. Defaults to <see cref="DefaultPollInterval"/>.</param>
    /// <param name="clock">Time source used only to stamp <c>ObservedUtc</c>; defaults to <see cref="DateTime.UtcNow"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pollInterval"/> is zero or negative.</exception>
    public ClientArrivalWatcher(
        Func<IReadOnlyList<int>>? processLookup = null,
        Func<int, ClientNetworkObservation>? networkLookup = null,
        TimeSpan? pollInterval = null,
        Func<DateTime>? clock = null)
    {
        _processLookup = processLookup ?? EnumerateClientProcesses;
        _networkLookup = networkLookup ?? ClientNetworkObserver.Observe;
        PollInterval = pollInterval ?? DefaultPollInterval;
        if (PollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval), PollInterval, "The poll interval must be greater than zero.");
        }

        _clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>
    /// Delay between two consecutive polls while waiting for the client.
    /// </summary>
    public TimeSpan PollInterval { get; }

    /// <summary>
    /// Takes a single observation of the running client processes and, when exactly
    /// one client process exists, of its TCP connection table.
    /// </summary>
    /// <returns>The observed arrival status; never throws because of the environment.</returns>
    public ClientArrivalStatus Poll()
    {
        IReadOnlyList<int> pids;
        try
        {
            pids = _processLookup();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            DateTime failedUtc = _clock().ToUniversalTime();
            return new ClientArrivalStatus(ClientArrivalStage.ObservationFailed, 0, null, 0, $"client_lookup_failed:{ex.GetType().Name}", failedUtc);
        }

        DateTime now = _clock().ToUniversalTime();

        if (pids.Count == 0)
        {
            return new ClientArrivalStatus(ClientArrivalStage.WaitingForProcess, 0, null, 0, "client_process_not_running", now);
        }

        if (pids.Count > 1)
        {
            return new ClientArrivalStatus(ClientArrivalStage.SessionAmbiguous, 0, null, 0, $"several_client_processes:{pids.Count}", now);
        }

        int pid = pids[0];
        ClientNetworkObservation observation;
        try
        {
            observation = _networkLookup(pid);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            return new ClientArrivalStatus(ClientArrivalStage.ObservationFailed, pid, null, 0, $"client_lookup_failed:{ex.GetType().Name}", now);
        }

        if (!observation.Observed)
        {
            return new ClientArrivalStatus(ClientArrivalStage.ObservationFailed, pid, null, 0, $"connection_table:{observation.FailureReason}", now);
        }

        int remote = observation.RemoteSessions.Count;

        if (observation.Primary is ClientTcpConnection primary)
        {
            return new ClientArrivalStatus(ClientArrivalStage.SessionIdentified, pid, primary.Remote, remote, null, now);
        }

        if (remote == 0)
        {
            return new ClientArrivalStatus(ClientArrivalStage.ProcessWithoutSession, pid, null, 0, "client_has_no_remote_session", now);
        }

        return new ClientArrivalStatus(ClientArrivalStage.SessionAmbiguous, pid, null, remote, $"several_remote_sessions:{remote}", now);
    }

    /// <summary>
    /// Polls until a single remote game session is identified, the timeout expires
    /// or the cancellation token is cancelled.
    /// </summary>
    /// <param name="timeout">How long to keep polling; must be greater than zero.</param>
    /// <param name="cancellationToken">Token that interrupts the wait without throwing.</param>
    /// <param name="onStatus">Callback invoked with the first status and afterwards only when the substantial status changes.</param>
    /// <returns>The last observed status; cancellation and timeout return it without throwing.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is zero or negative.</exception>
    public ClientArrivalStatus WaitForClient(
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        Action<ClientArrivalStatus>? onStatus = null)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "The timeout must be greater than zero.");
        }

        // Monotonic clock for the deadline; the injected clock only stamps ObservedUtc.
        var elapsed = Stopwatch.StartNew();
        ClientArrivalStatus status = Poll();
        onStatus?.Invoke(status);

        while (!status.Identified)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (elapsed.Elapsed >= timeout)
            {
                break;
            }

            cancellationToken.WaitHandle.WaitOne(PollInterval);
            ClientArrivalStatus next = Poll();
            if (IsSubstantiallyDifferent(status, next))
            {
                onStatus?.Invoke(next);
            }

            status = next;
        }

        return status;
    }

    /// <summary>
    /// True when anything that matters for the caller changed between two polls:
    /// the stage, the process id, the ordinal reason or the remote endpoint.
    /// <c>ObservedUtc</c> does not count as a change.
    /// </summary>
    private static bool IsSubstantiallyDifferent(ClientArrivalStatus current, ClientArrivalStatus next)
    {
        if (current.Stage != next.Stage)
        {
            return true;
        }

        if (current.ProcessId != next.ProcessId)
        {
            return true;
        }

        if (!string.Equals(current.Reason, next.Reason, StringComparison.Ordinal))
        {
            return true;
        }

        if (current.Endpoint is null)
        {
            return next.Endpoint is not null;
        }

        return next.Endpoint is not null && !current.Endpoint.Equals(next.Endpoint);
    }

    /// <summary>
    /// Enumerates the real client processes by probing every name in
    /// <see cref="DefaultProcessNames"/> with <see cref="Process.GetProcessesByName(string)"/>.
    /// </summary>
    /// <returns>The ids of the running client processes, sorted ascending; empty when not on Windows.</returns>
    private static IReadOnlyList<int> EnumerateClientProcesses()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<int>();
        }

        var processIds = new HashSet<int>();
        foreach (string name in DefaultProcessNames)
        {
            try
            {
                Process[] processes = Process.GetProcessesByName(name);
                try
                {
                    foreach (Process process in processes)
                    {
                        processIds.Add(process.Id);
                    }
                }
                finally
                {
                    // Every Process object holds a system handle and must be released.
                    foreach (Process process in processes)
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Skip this process name and keep probing the remaining ones.
            }
        }

        int[] sortedIds = new int[processIds.Count];
        processIds.CopyTo(sortedIds);
        Array.Sort(sortedIds);
        return sortedIds;
    }
}
