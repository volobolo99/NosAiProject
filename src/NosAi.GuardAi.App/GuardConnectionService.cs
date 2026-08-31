using NosAi.GuardClient;

namespace NosAi.GuardAi.App;

/// <summary>What the UI is allowed to say about the link, and nothing more.</summary>
public enum GuardLinkState
{
    /// <summary>No session has been attempted. Not "offline": nothing is known yet.</summary>
    Idle,

    /// <summary>Looking for the runtime on the network.</summary>
    Searching,

    Connecting,
    Connected,

    /// <summary>
    /// The session dropped for a reason that may pass, and the app is waiting to
    /// try again on its own.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Failed"/> on purpose: one asks the operator to do
    /// something, the other asks them to wait. Showing a refused device as
    /// "reconnecting" would hide the one failure that needs a person.
    /// </remarks>
    Reconnecting,

    /// <summary>The link failed or was refused. <see cref="GuardStatus.Detail"/> says why.</summary>
    Failed
}

/// <summary>An immutable view of the link for the UI to render.</summary>
/// <remarks>
/// Every list is empty unless a snapshot is actually in hand. A dropped session
/// must not leave the last good reading on screen: stale state that looks current
/// is the one thing this application must never show.
/// </remarks>
public sealed record GuardStatus(
    GuardLinkState State,
    string? Detail,
    IReadOnlyList<ClassifiedField> Client,
    IReadOnlyList<ClassifiedField> Safety,
    string? RuntimeStatus,
    string? Endpoint,
    DateTimeOffset? ObservedAt)
{
    private static readonly IReadOnlyList<ClassifiedField> None = Array.Empty<ClassifiedField>();

    public static GuardStatus Idle { get; } =
        new(GuardLinkState.Idle, null, None, None, null, null, null);

    public static GuardStatus Busy(GuardLinkState state, string detail) =>
        new(state, detail, None, None, null, null, null);

    public static GuardStatus Failure(string detail) =>
        new(GuardLinkState.Failed, detail, None, None, null, null, null);

    public static GuardStatus Waiting(string detail) =>
        new(GuardLinkState.Reconnecting, detail, None, None, null, null, null);
}

/// <summary>
/// Owns the Guard AI session end to end, so the screen carries a status and two
/// buttons and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Everything the operator would otherwise have to handle is resolved here: the
/// device key is loaded or created and never shown, the runtime's address is
/// discovered rather than typed, and enrollment is the pairing step done once
/// over USB. What surfaces is the state of the link.
/// </para>
/// <para>
/// The heartbeat loop is the whole point: the runtime terminates a session that
/// goes quiet for <see cref="GuardAiClient.HeartbeatTimeout"/>, so the app must
/// keep beating while it is on screen and must show the session as lost the
/// moment it stops.
/// </para>
/// </remarks>
public sealed class GuardConnectionService : IAsyncDisposable
{
    /// <summary>Half the server deadline, so one lost beat does not drop the session.</summary>
    private static readonly TimeSpan BeatInterval = TimeSpan.FromMilliseconds(1000);

    /// <summary>How long to wait for a runtime to answer a discovery probe.</summary>
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Loopback: over USB the reverse tunnel makes the PC answer here.</summary>
    private const string UsbHost = "127.0.0.1";

    private const int GuardPort = 17471;

    private readonly IDeviceSigner _deviceKey;
    private readonly string? _runtimePublicKeyPem;
    private readonly GuardReconnectPolicy _policy = new();
    private GuardAiClient? _client;
    private CancellationTokenSource? _loop;

    /// <summary>
    /// The transport the operator chose, so a reconnect repeats their choice.
    /// </summary>
    /// <remarks>
    /// Null while nothing is running, which is also what stops a stale loop from
    /// reconnecting after the operator pressed Disconnetti.
    /// </remarks>
    private GuardTransport? _active;

    /// <summary>Where the device key lives, for the operator to see (ADR-0010).</summary>
    public DeviceKeyCustody KeyCustody => _deviceKey.Custody;

    public GuardConnectionService(IDeviceSigner deviceKey, string? runtimePublicKeyPem)
    {
        _deviceKey = deviceKey;
        _runtimePublicKeyPem = runtimePublicKeyPem;
    }

    /// <summary>Raised on every status change, including failures.</summary>
    public event Action<GuardStatus>? StatusChanged;

    public async Task ConnectAsync(GuardTransport transport, CancellationToken cancellationToken = default)
    {
        await StopAsync().ConfigureAwait(false);
        _policy.OnSuccess();
        await OpenAsync(transport, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Opens one session. Used by the operator's Connetti and by a retry.</summary>
    private async Task OpenAsync(GuardTransport transport, CancellationToken cancellationToken)
    {
        _active = transport;

        string host;
        if (transport == GuardTransport.WiFi)
        {
            Publish(GuardStatus.Busy(GuardLinkState.Searching, "Ricerca del runtime sulla rete…"));
            DiscoveredRuntime? found;
            try
            {
                found = await RuntimeDiscovery.FindFirstAsync(DiscoveryTimeout, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await AfterFailureAsync($"ricerca fallita ({ex.GetType().Name})", "discovery_failed", transport, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            if (found is null)
            {
                // The runtime may simply not be up yet, so this is worth waiting on
                // rather than handing straight back to the operator.
                await AfterFailureAsync(
                    "nessun runtime trovato sulla rete. Verificare che il PC sia sullo stesso Wi-Fi e che il runtime sia avviato.",
                    "discovery_empty", transport, cancellationToken).ConfigureAwait(false);
                return;
            }

            host = found.Address;
        }
        else
        {
            host = UsbHost;
        }

        var endpoint = $"{host}:{GuardPort}";
        Publish(GuardStatus.Busy(GuardLinkState.Connecting, endpoint));

        if (string.IsNullOrWhiteSpace(_runtimePublicKeyPem))
        {
            // Terminal by nature: no amount of waiting produces a pairing.
            _active = null;
            Publish(GuardStatus.Failure(
                "chiave del runtime assente. Collegare il telefono via USB ed eseguire python -m nosai.phone.deploy."));
            return;
        }

        var client = new GuardAiClient(host, GuardPort, _deviceKey, _runtimePublicKeyPem);
        GuardSession session;
        try
        {
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            session = await client.OpenSessionAsync(cancellationToken).ConfigureAwait(false);
            _client = client;
        }
        catch (GuardProtocolException ex)
        {
            await client.DisposeAsync().ConfigureAwait(false);
            await AfterFailureAsync(Explain(ex, transport), ex.Reason, transport, cancellationToken).ConfigureAwait(false);
            return;
        }
        catch (OperationCanceledException)
        {
            await client.DisposeAsync().ConfigureAwait(false);
            _active = null;
            Publish(GuardStatus.Idle);
            return;
        }

        _policy.OnSuccess();
        Publish(Describe(session.TelemetryJson, endpoint));
        _loop = new CancellationTokenSource();
        _ = BeatAsync(endpoint, _loop.Token);
    }

    /// <summary>
    /// Reports a failure, and retries it when waiting could plausibly help.
    /// </summary>
    /// <remarks>
    /// The decision is <see cref="GuardReconnectPolicy"/>'s, not this method's: a
    /// refused device is reported once and left alone, a runtime that is not up
    /// yet is waited for. Retrying a refusal would turn the one message that needs
    /// a person into a scrolling one.
    /// </remarks>
    private async Task AfterFailureAsync(string detail, string? reason, GuardTransport transport, CancellationToken cancellationToken)
    {
        if (_policy.OnFailure(reason, out var delay) == ReconnectDecision.Stop)
        {
            _active = null;
            Publish(GuardStatus.Failure(detail));
            return;
        }

        Publish(GuardStatus.Waiting($"{detail} Nuovo tentativo fra {delay.TotalSeconds:F0} s (tentativo {_policy.Attempt})."));

        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _active = null;
            Publish(GuardStatus.Idle);
            return;
        }

        // The operator may have pressed Disconnetti while we waited.
        if (_active != transport)
            return;

        await OpenAsync(transport, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Turns a protocol failure into something an operator can act on.
    /// </summary>
    /// <remarks>
    /// A refused device and a dead tunnel both surface as "not connected" but need
    /// opposite responses, and over USB a refused connection almost always means
    /// the reverse tunnel is gone rather than the runtime being down.
    /// </remarks>
    private static string Explain(GuardProtocolException ex, GuardTransport transport) => ex.Reason switch
    {
        "authentication_refused" =>
            "dispositivo non riconosciuto dal runtime. Ripetere l'abbinamento via USB.",
        "runtime_proof_rejected" =>
            "runtime non riconosciuto. Ripetere l'abbinamento via USB.",
        "connect_failed" when transport == GuardTransport.Usb =>
            "runtime irraggiungibile via USB. Ricollegare il cavo e rieseguire l'abbinamento.",
        "connect_failed" =>
            "runtime irraggiungibile. Verificare che sia avviato sul PC.",
        "unsupported_contract_version" =>
            $"versione del contratto non supportata ({ex.Detail}). Aggiornare l'app o il runtime.",
        "peer_disconnected" => "sessione chiusa dal runtime.",
        _ => ex.Detail is null ? ex.Reason : $"{ex.Reason} ({ex.Detail})"
    };

    private async Task BeatAsync(string endpoint, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && _client is { } client)
            {
                await Task.Delay(BeatInterval, token).ConfigureAwait(false);
                Publish(Describe(await client.HeartbeatAsync(token).ConfigureAwait(false), endpoint));
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped on purpose.
        }
        catch (GuardProtocolException ex)
        {
            // A dropped session must be visible immediately. Leaving the last good
            // snapshot on screen would show the operator stale state as if it were
            // current, which is the one thing this app must never do.
            var transport = _active;
            var detail = ex.Reason == "peer_disconnected"
                ? "sessione chiusa dal runtime."
                : $"sessione interrotta ({ex.Reason}).";

            await StopKeepingTransportAsync().ConfigureAwait(false);

            if (transport is { } chosen)
                await AfterFailureAsync(detail, ex.Reason, chosen, CancellationToken.None).ConfigureAwait(false);
            else
                Publish(GuardStatus.Failure(detail));
        }
    }

    /// <summary>
    /// Turns a snapshot into what the screen shows.
    /// </summary>
    /// <remarks>
    /// The parsing lives in <see cref="GuardSnapshotView"/>, in the client library,
    /// so it can be tested without a device — the phone application has no test
    /// host of its own, and rendering rules that only run on hardware are rules
    /// nobody checks. The safety section in particular is <b>read</b> here: the
    /// screen used to assert that input and injection were off, which is a claim
    /// about a property only the runtime is authoritative for.
    /// </remarks>
    private static GuardStatus Describe(string telemetryJson, string endpoint)
    {
        var view = GuardSnapshotView.Parse(telemetryJson);
        return new GuardStatus(
            GuardLinkState.Connected,
            view.ClientStatus,
            view.Client,
            view.Safety,
            view.RuntimeStatus,
            endpoint,
            // The runtime's own capture time, not the phone's clock: a snapshot is
            // as fresh as when it was taken, not as when it arrived.
            view.CapturedAtUtc ?? DateTimeOffset.Now);
    }

    /// <summary>Stops the session and forgets the operator's transport choice.</summary>
    public async Task StopAsync()
    {
        _active = null;
        await StopKeepingTransportAsync().ConfigureAwait(false);
    }

    /// <summary>Stops the session but keeps the choice, so a retry can repeat it.</summary>
    private async Task StopKeepingTransportAsync()
    {
        _loop?.Cancel();
        _loop?.Dispose();
        _loop = null;
        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
            _client = null;
        }
    }

    private void Publish(GuardStatus status) => StatusChanged?.Invoke(status);

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        (_deviceKey as IDisposable)?.Dispose();
    }
}
