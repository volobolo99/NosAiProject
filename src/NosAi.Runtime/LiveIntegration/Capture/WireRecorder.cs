using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using NosAi.Runtime.Configuration;

namespace NosAi.LiveIntegration.Capture;

/// <summary>What a recording run produced, and why it is not usable when it is not.</summary>
/// <remarks>
/// A file that exists is not evidence. A run that captured nothing writes a
/// well-formed but empty recording, and calling that a success would hand the
/// operator a second source with nothing in it.
/// </remarks>
public readonly record struct RecordingOutcome(long Packets, string Path, string? FailureReason)
{
    public bool Ok => FailureReason is null;
}

/// <summary>
/// Records the game's wire to a <c>.noscap</c>, so a memory reading can be
/// checked against a second source observed at the same moment.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the archived recordings cannot corroborate a live read.
/// Entity ids are per-session and vitals are per-instant, so comparing memory
/// now against a capture taken on another day produces a guaranteed mismatch
/// that says nothing about either source. ADR-0014 asks for two independent
/// observations of the same fact; "the same fact" includes when.
/// </para>
/// <para>
/// Sniff only. <see cref="WinDivertPacketSource"/> opens with
/// <c>FlagSniff | FlagRecvOnly</c>, so packets are copied and never dropped,
/// altered or injected. Recording is observation, and stays on the observation
/// side of ADR-0014.
/// </para>
/// <para>
/// <see cref="AwaitClientFlag"/> is the mode T-14 needs: recording that begins
/// <b>before</b> the client connects. The command takes no endpoint; it waits
/// for a client process, then for that process's first game connection, and
/// records from the moment the connection was detected. The packets exchanged
/// before that moment are gone and stay gone: the command says so when it
/// starts recording, never later, because a capture that claims to hold the
/// session from the launch would hand the decoder bytes it does not have.
/// </para>
/// </remarks>
public static class WireRecorder
{
    public const string Flag = "--record-wire";

    /// <summary>Where the replay commands look, so a recording lands where they will find it.</summary>
    public const string DefaultDirectory = "data";

    /// <summary>The mode that waits for the client instead of receiving an endpoint.</summary>
    public const string AwaitClientFlag = "--await-client";

    public const string EndpointMissingReason = "record_endpoint_missing";
    public const string EndpointMalformedPrefix = "record_endpoint_malformed";
    public const string HostNotAnIpPrefix = "record_host_not_an_ip";
    public const string PortImplausiblePrefix = "record_port_implausible";
    public const string DriverUnavailablePrefix = "record_driver_unavailable";
    public const string PathUnwritablePrefix = "record_path_unwritable";
    public const string NoPacketsReason = "record_no_packets";

    /// <summary>The client process never appeared within the wait.</summary>
    public const string ClientNeverAppearedReason = "record_client_process_never_appeared";

    /// <summary>The client process appeared but no game connection was detected within the wait.</summary>
    public const string GameSessionNeverAppearedReason = "record_game_session_never_appeared";

    /// <summary>An explicit endpoint and the await-client mode were both requested.</summary>
    public const string EndpointAndAwaitConflictReason = "record_endpoint_and_await_conflict";

    /// <summary>How often the wait loop asks again whether the client appeared.</summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>How long the wait mode looks for the client process before refusing.</summary>
    public static readonly TimeSpan DefaultClientProcessWait = TimeSpan.FromMinutes(20);

    /// <summary>How long the wait mode looks for a game connection after the process appears.</summary>
    public static readonly TimeSpan DefaultGameSessionWait = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Parses <c>ip:port</c>.
    /// </summary>
    /// <remarks>
    /// An IPv4 literal, not a host name: the WinDivert filter is written against
    /// <c>ip.SrcAddr</c> / <c>ip.DstAddr</c>, so a name that resolved to several
    /// addresses would capture one of them and silently miss the rest. The same
    /// rule already governs <c>--observe-game</c>.
    /// </remarks>
    public static bool TryParseEndpoint(
        string? text, out IPAddress address, out int port, out string? failureReason)
    {
        address = IPAddress.None;
        port = 0;

        if (string.IsNullOrWhiteSpace(text))
        {
            failureReason = EndpointMissingReason;
            return false;
        }

        string trimmed = text.Trim();
        int separator = trimmed.LastIndexOf(':');
        if (separator <= 0 || separator == trimmed.Length - 1)
        {
            failureReason = string.Create(CultureInfo.InvariantCulture, $"{EndpointMalformedPrefix}:{trimmed}");
            return false;
        }

        string host = trimmed[..separator];
        string portText = trimmed[(separator + 1)..];

        if (!IPAddress.TryParse(host, out IPAddress? parsed)
            || parsed.AddressFamily != AddressFamily.InterNetwork)
        {
            failureReason = string.Create(CultureInfo.InvariantCulture, $"{HostNotAnIpPrefix}:{host}");
            return false;
        }

        if (!int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedPort)
            || parsedPort < 1
            || parsedPort > 65535)
        {
            failureReason = string.Create(CultureInfo.InvariantCulture, $"{PortImplausiblePrefix}:{portText}");
            return false;
        }

        address = parsed;
        port = parsedPort;
        failureReason = null;
        return true;
    }

    /// <summary>A name that sorts by when it was taken, under the directory the readers scan.</summary>
    public static string DefaultPath(DateTime nowUtc) => Path.Combine(
        DefaultDirectory,
        string.Create(CultureInfo.InvariantCulture, $"nostale_{nowUtc.ToUniversalTime():yyyyMMdd_HHmmss}Z.noscap"));

    /// <summary>
    /// Drains a source into a recording and says whether the result is usable.
    /// </summary>
    /// <remarks>
    /// Separated from <see cref="Run"/> so the outcome rules are exercised
    /// against a scripted source with no driver and no game, the way the rest of
    /// the capture engine already is.
    /// </remarks>
    public static RecordingOutcome RecordFrom(
        IPacketSource source, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        long written;

        // Cancelling has to close the source, not merely set a flag.
        // WinDivertPacketSource.TryRead ignores its timeout argument: WinDivertRecv
        // blocks until a packet matches the filter, so CaptureFile.Record reaches
        // its token check only between packets and a quiet endpoint never gets
        // there. Closing the handle is what makes a pending read return, so
        // cancellation disposes the source. Dispose is idempotent, so the caller's
        // own using still runs.
        using (cancellationToken.Register(source.Dispose))
        {
            try
            {
                written = CaptureFile.Record(source, path, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return new RecordingOutcome(0, path, string.Create(CultureInfo.InvariantCulture,
                    $"{PathUnwritablePrefix}:{ex.GetType().Name}"));
            }
        }

        // Zero packets is a well-formed file with a header and nothing else. It
        // would be accepted by every reader and corroborate nothing, so it is
        // refused here rather than discovered three commands later — and removed,
        // because the refusal protects this command's exit code while the file
        // would sit in the directory the replay commands scan.
        if (written == 0)
        {
            Discard(path);
            return new RecordingOutcome(0, path, NoPacketsReason);
        }

        return new RecordingOutcome(written, path, null);
    }

    /// <summary>Removes a recording this call created and then refused.</summary>
    /// <remarks>
    /// Best effort: failing to delete a file that was already refused is not worth
    /// turning into a second, different failure.
    /// </remarks>
    private static void Discard(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Opens the driver on one endpoint and records until stopped.</summary>
    /// <param name="endpoint">The game server as <c>ip:port</c>.</param>
    /// <param name="path">Where to write, or null for <see cref="DefaultPath"/>.</param>
    /// <param name="seconds">Stop after this many seconds, or 0 to run until Ctrl+C.</param>
    public static int Run(string? endpoint, string? path = null, int seconds = 0)
    {
        if (!TryParseEndpoint(endpoint, out IPAddress address, out int port, out string? endpointFailure))
        {
            Console.WriteLine($"[REFUSED] {endpointFailure}");
            PrintUsage();
            return 2;
        }

        string target = string.IsNullOrWhiteSpace(path) ? DefaultPath(DateTime.UtcNow) : path!;

        WinDivertPacketSource? source = WinDivertPacketSource.TryOpen(address, port, out string? driverFailure);
        if (source is null)
        {
            Console.WriteLine($"[REFUSED] {DriverUnavailablePrefix}:{driverFailure}");
            return 1;
        }

        using (source)
        using (var stopping = new CancellationTokenSource())
        {
            if (seconds > 0)
                stopping.CancelAfter(TimeSpan.FromSeconds(seconds));

            // The first interrupt cancels rather than letting the runtime kill the
            // process: Record only flushes when its loop exits, so a hard Ctrl+C
            // would cost the operator the recording they just sat through.
            //
            // A second interrupt must still end the process. Suppressing every one
            // of them trades a lost recording for an operator who cannot get out,
            // which is the worse bargain — and it was reachable here, because a
            // filter that matches nothing leaves the read blocked in the driver.
            var interrupts = 0;
            ConsoleCancelEventHandler onCancel = (_, e) =>
            {
                if (Interlocked.Increment(ref interrupts) > 1)
                    return;

                e.Cancel = true;
                stopping.Cancel();
            };
            Console.CancelKeyPress += onCancel;

            try
            {
                Console.WriteLine("=== recording the wire (sniff only; nothing is altered) ===");
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"server: {address}:{port}"));
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"file:   {Path.GetFullPath(target)}"));
                Console.WriteLine(seconds > 0
                    ? string.Create(CultureInfo.InvariantCulture, $"stop:   after {seconds}s, or Ctrl+C")
                    : "stop:   Ctrl+C");
                Console.WriteLine();
                Console.WriteLine("Play the session you want to certify. Read the memory probes against");
                Console.WriteLine("this file, not an archived one: a second source has to be observed at");
                Console.WriteLine("the same moment as the reading it corroborates.");
                Console.WriteLine();

                RecordingOutcome outcome = RecordFrom(source, target, stopping.Token);
                if (!outcome.Ok)
                {
                    Console.WriteLine($"[REFUSED] {outcome.FailureReason}");
                    return 1;
                }

                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{outcome.Packets} packets -> {Path.GetFullPath(outcome.Path)}"));
                return 0;
            }
            finally
            {
                Console.CancelKeyPress -= onCancel;
            }
        }
    }

    /// <summary>
    /// The mode decision for one <c>--record-wire</c> invocation. An explicit
    /// endpoint and <see cref="AwaitClientFlag"/> together are contradictory:
    /// the wait mode exists precisely because the endpoint does not exist yet,
    /// so choosing one of the two silently would hide the other half of the
    /// request. <c>null</c> means the invocation is coherent.
    /// </summary>
    public static string? AwaitClientConflictReason(string? endpoint, bool awaitClient)
        => awaitClient && !string.IsNullOrWhiteSpace(endpoint) ? EndpointAndAwaitConflictReason : null;

    /// <summary>Dispatch for <c>--record-wire</c>: the endpoint mode, the wait mode, or the refusal between them.</summary>
    /// <param name="endpoint">The game server as <c>ip:port</c>, or null in the wait mode.</param>
    /// <param name="path">Where to write, or null for <see cref="DefaultPath"/>.</param>
    /// <param name="seconds">Record for this many seconds once attached, or 0 to run until Ctrl+C.</param>
    /// <param name="awaitClient">Whether <see cref="AwaitClientFlag"/> was passed.</param>
    public static int Run(string? endpoint, string? path, int seconds, bool awaitClient)
    {
        string? conflict = AwaitClientConflictReason(endpoint, awaitClient);
        if (conflict is not null)
        {
            Console.WriteLine($"[REFUSED] {conflict}: pass either <ip>:<port> or {AwaitClientFlag}, not both");
            PrintUsage();
            return 2;
        }

        if (awaitClient)
        {
            // Under the wait mode a positional token that parses as an IPv4
            // endpoint is the endpoint that was put in the file's place, which is
            // the same contradiction the wrong way round. Refusing beats letting
            // it ride along as an impossible file name until the attach.
            if (LooksLikeEndpointArgument(path))
            {
                Console.WriteLine($"[REFUSED] {EndpointAndAwaitConflictReason}: pass either <ip>:<port> or {AwaitClientFlag}, not both");
                PrintUsage();
                return 2;
            }

            return RunAwaitClient(path, seconds);
        }

        return Run(endpoint, path, seconds);
    }

    /// <summary>
    /// The wait mode as a command: waits for the client, then records from the
    /// moment its game connection is detected, and prints the honest report.
    /// </summary>
    /// <remarks>
    /// Command surface only. The loop itself lives in <see cref="RunAwaitAndRecord"/>,
    /// which is what the tests drive with a scripted process finder and observer.
    /// </remarks>
    public static int RunAwaitClient(string? path = null, int seconds = 0)
    {
        string target = string.IsNullOrWhiteSpace(path) ? DefaultPath(DateTime.UtcNow) : path!;
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        using var stopping = new CancellationTokenSource();

        // Same bargain as Run: the first interrupt cancels, a second one ends the
        // process. During the wait a cancel stops the search without a file;
        // during the recording it closes the driver through RecordFrom.
        var interrupts = 0;
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            if (Interlocked.Increment(ref interrupts) > 1)
                return;

            e.Cancel = true;
            stopping.Cancel();
        };
        Console.CancelKeyPress += onCancel;

        try
        {
            return RunAwaitAndRecord(
                path: target,
                recordingSeconds: seconds,
                clientProcessNames: new Gate1HostOptions().ClientProcessName,
                pollInterval: DefaultPollInterval,
                clientProcessTimeout: DefaultClientProcessWait,
                gameSessionTimeout: DefaultGameSessionWait,
                findClientProcessIds: FindClientProcesses,
                observe: ClientNetworkObserver.Observe,
                recordOnEndpoint: RecordOnEndpoint,
                output: Console.Out,
                cancellationToken: stopping.Token);
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    /// <summary>
    /// Waits for a client process and then for its first game connection, and
    /// records on that connection.
    /// </summary>
    /// <param name="path">Where the recording is written.</param>
    /// <param name="recordingSeconds">Record for this many seconds once attached, or 0 until cancelled.</param>
    /// <param name="clientProcessNames">
    /// Comma-separated executable names to look for. Defaults to
    /// <see cref="Gate1HostOptions.ClientProcessName"/>, so this code never writes
    /// the names itself.
    /// </param>
    /// <param name="pollInterval">How often the loop asks again. Explicit, bounded: no busy spin.</param>
    /// <param name="clientProcessTimeout">Ceiling for the process to appear. When it expires the run refuses.</param>
    /// <param name="gameSessionTimeout">Ceiling for a game connection once the process appeared.</param>
    /// <param name="findClientProcessIds">Process discovery. Defaults to the OS process table.</param>
    /// <param name="observe">Connection observation. Defaults to <see cref="ClientNetworkObserver.Observe"/>.</param>
    /// <param name="recordOnEndpoint">The recording phase. Defaults to opening the driver on the endpoint.</param>
    /// <param name="output">Where the report is written. Defaults to the console.</param>
    /// <param name="cancellationToken">Stops the wait (and, once attached, the recording).</param>
    /// <returns>0 on success, 1 when the run is refused, 0 also when cancelled before anything was written.</returns>
    /// <remarks>
    /// The seams exist so the loop is testable without a driver, a client or an
    /// OS process table: every default preserves the production behavior.
    /// </remarks>
    public static int RunAwaitAndRecord(
        string path,
        int recordingSeconds,
        string? clientProcessNames = null,
        TimeSpan? pollInterval = null,
        TimeSpan? clientProcessTimeout = null,
        TimeSpan? gameSessionTimeout = null,
        Func<string, IReadOnlyList<int>>? findClientProcessIds = null,
        Func<int, ClientNetworkObservation>? observe = null,
        Func<IPEndPoint, string, CancellationToken, RecordingOutcome>? recordOnEndpoint = null,
        TextWriter? output = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (recordingSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(recordingSeconds), "recordingSeconds must not be negative.");

        clientProcessNames ??= new Gate1HostOptions().ClientProcessName;
        TimeSpan interval = pollInterval ?? DefaultPollInterval;
        TimeSpan processCeiling = clientProcessTimeout ?? DefaultClientProcessWait;
        TimeSpan sessionCeiling = gameSessionTimeout ?? DefaultGameSessionWait;
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pollInterval), "pollInterval must be positive.");
        if (processCeiling <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(clientProcessTimeout), "clientProcessTimeout must be positive.");
        if (sessionCeiling <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(gameSessionTimeout), "gameSessionTimeout must be positive.");

        findClientProcessIds ??= FindClientProcesses;
        observe ??= ClientNetworkObserver.Observe;
        recordOnEndpoint ??= RecordOnEndpoint;
        output ??= Console.Out;

        string target = string.IsNullOrWhiteSpace(path) ? DefaultPath(DateTime.UtcNow) : path;
        string namesText = string.IsNullOrWhiteSpace(clientProcessNames)
            ? "(nessun nome)" : clientProcessNames;

        output.WriteLine("=== waiting for the NosTale client, then recording from its first game connection ===");
        output.WriteLine($"processes: {namesText}");
        output.WriteLine($"will wait up to {FormatDuration(processCeiling)} for the process, then up to "
                         + $"{FormatDuration(sessionCeiling)} for a game connection; Ctrl+C stops.");

        var waited = Stopwatch.StartNew();

        // Phase 1: the process. Absence for the whole ceiling is the first of the
        // two refusals: the operator has not opened the client, and telling them
        // to check the game would send them to the wrong place.
        int? processId = null;
        var processSearch = Stopwatch.StartNew();
        while (processSearch.Elapsed < processCeiling)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                output.WriteLine("stopped before the client process appeared; no file was written.");
                return 0;
            }

            IReadOnlyList<int> found;
            try
            {
                found = findClientProcessIds(clientProcessNames);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // A process table that cannot be read this round is not "the
                // client is absent". Ask again on the next interval.
                found = Array.Empty<int>();
            }

            int candidate = found.FirstOrDefault(id => id > 0);
            if (candidate > 0)
            {
                processId = candidate;
                break;
            }

            Thread.Sleep(interval);
        }

        if (processId is null)
        {
            output.WriteLine($"[REFUSED] {ClientNeverAppearedReason}: no client process ('{namesText}') appeared "
                             + $"within {FormatDuration(processCeiling)}; no file was written.");
            return 1;
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"client process found: pid={processId}"));

        // Phase 2: the game connection. The process existing without a remote
        // session is the second refusal, distinct from the first: it sends the
        // operator to the game's own login, not to the launcher.
        string? lastObservationFailure = null;
        IPEndPoint? endpoint = null;
        var sessionSearch = Stopwatch.StartNew();
        while (sessionSearch.Elapsed < sessionCeiling)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                output.WriteLine("stopped before a game connection was detected; no file was written.");
                return 0;
            }

            ClientNetworkObservation observation = observe(processId.Value);
            if (!observation.Observed)
            {
                // A probe that cannot look is not "no connection yet". Remember
                // its reason: if we time out while unable to look, that reason is
                // what the operator needs, not a blame on the game.
                lastObservationFailure = observation.FailureReason ?? "observation_unavailable";
            }
            else
            {
                lastObservationFailure = null;
                if (observation.Primary is { } session)
                {
                    endpoint = session.Remote;
                    break;
                }
            }

            Thread.Sleep(interval);
        }

        if (endpoint is null)
        {
            string refusal = lastObservationFailure ?? GameSessionNeverAppearedReason;
            string detail = lastObservationFailure is null
                ? $"the client process appeared but no game connection was detected within {FormatDuration(sessionCeiling)}; no file was written."
                : $"no game connection could be read within {FormatDuration(sessionCeiling)}; no file was written.";
            output.WriteLine($"[REFUSED] {refusal}: {detail}");
            return 1;
        }

        // The attach is the moment the capture can start. Everything the client
        // exchanged before it — the login handshake included, if it happened
        // before the detection — is not in the file, and this is said here, when
        // recording begins, not at the end where it could pass for a footnote.
        TimeSpan waitedBeforeAttach = waited.Elapsed;
        output.WriteLine("=== recording the wire (sniff only; nothing is altered) ===");
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"server: {endpoint.Address}:{endpoint.Port}"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"file:   {Path.GetFullPath(target)}"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"attached after waiting {waitedBeforeAttach.TotalSeconds:F1} s"));
        output.WriteLine("note: this recording starts from the connection just detected. Packets the client");
        output.WriteLine("exchanged BEFORE the attach are NOT captured; the file holds the wire from the");
        output.WriteLine("detected connection onward, not from the client's launch.");
        output.WriteLine(recordingSeconds > 0
            ? string.Create(CultureInfo.InvariantCulture, $"stop:   after {recordingSeconds}s, or Ctrl+C")
            : "stop:   Ctrl+C");
        output.WriteLine();

        RecordingOutcome outcome;
        using (var recordCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            if (recordingSeconds > 0)
                recordCts.CancelAfter(TimeSpan.FromSeconds(recordingSeconds));

            outcome = recordOnEndpoint(endpoint, target, recordCts.Token);
        }

        if (!outcome.Ok)
        {
            output.WriteLine($"[REFUSED] {outcome.FailureReason}");
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"waited before attach: {waitedBeforeAttach.TotalSeconds:F1} s"));
            return 1;
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{outcome.Packets} packets -> {Path.GetFullPath(outcome.Path)}"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"waited before attach: {waitedBeforeAttach.TotalSeconds:F1} s"));
        return 0;
    }

    /// <summary>Process discovery by the declared client names, in OS style.</summary>
    /// <remarks>
    /// The names come from the caller (defaults to <see cref="Gate1HostOptions.ClientProcessName"/>),
    /// so this method never holds a client process name of its own.
    /// </remarks>
    private static IReadOnlyList<int> FindClientProcesses(string clientProcessNames)
    {
        var ids = new List<int>();
        foreach (string rawName in clientProcessNames.Split(','))
        {
            string name = rawName.Trim();
            if (name.Length == 0)
                continue;

            try
            {
                foreach (Process process in Process.GetProcessesByName(name))
                {
                    using (process)
                        ids.Add(process.Id);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // A process table that cannot be read is a transient "not found"
                // for this round, not a statement about the client.
            }
        }

        return ids.Distinct().ToArray();
    }

    /// <summary>The driver open + drain path both run modes share. No second capture mechanism.</summary>
    private static RecordingOutcome RecordOnEndpoint(IPEndPoint endpoint, string path, CancellationToken cancellationToken)
    {
        WinDivertPacketSource? source = WinDivertPacketSource.TryOpen(
            endpoint.Address, endpoint.Port, out string? driverFailure);
        if (source is null)
        {
            return new RecordingOutcome(0, path, string.Create(CultureInfo.InvariantCulture,
                $"{DriverUnavailablePrefix}:{driverFailure}"));
        }

        using (source)
            return RecordFrom(source, path, cancellationToken);
    }

    private static bool LooksLikeEndpointArgument(string? text)
        => TryParseEndpoint(text, out _, out _, out _);

    private static void PrintUsage()
    {
        Console.WriteLine($"Usage: {Flag} <ip>:<port> [file.noscap] [--watch N]");
        Console.WriteLine($"       {Flag} {AwaitClientFlag} [file.noscap] [--watch N]  (waits for the client, then records from its connection)");
    }

    /// <summary>Describes a wait budget readably: minutes, seconds, or milliseconds.</summary>
    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalSeconds < 1
            ? string.Create(CultureInfo.InvariantCulture, $"{duration.TotalMilliseconds:F0} ms")
            : duration.TotalMinutes >= 1
                ? string.Create(CultureInfo.InvariantCulture, $"{duration.TotalMinutes:F0} min")
                : string.Create(CultureInfo.InvariantCulture, $"{duration.TotalSeconds:F0} s");
}
