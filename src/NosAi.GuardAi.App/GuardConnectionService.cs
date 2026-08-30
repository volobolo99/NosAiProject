using System.Security.Cryptography;
using System.Text.Json;
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

    /// <summary>The link failed or was refused. <see cref="GuardStatus.Detail"/> says why.</summary>
    Failed
}

/// <summary>
/// One classified field as the runtime published it.
/// </summary>
/// <remarks>
/// <see cref="Value"/> is null whenever <see cref="Source"/> is UNKNOWN. The app
/// never substitutes a zero, a dash or an empty string for an unobserved reading:
/// on an operator's phone those are indistinguishable from a real measurement.
/// </remarks>
public sealed record ClassifiedField(string Name, string? Value, string Source)
{
    public string Display => Source == "UNKNOWN" ? "UNKNOWN" : $"{Value} [{Source}]";
}

/// <summary>An immutable view of the link for the UI to render.</summary>
public sealed record GuardStatus(
    GuardLinkState State,
    string? Detail,
    IReadOnlyList<ClassifiedField> Client,
    string? RuntimeStatus,
    string? Endpoint,
    DateTimeOffset? ObservedAt)
{
    public static GuardStatus Idle { get; } =
        new(GuardLinkState.Idle, null, Array.Empty<ClassifiedField>(), null, null, null);

    public static GuardStatus Busy(GuardLinkState state, string detail) =>
        new(state, detail, Array.Empty<ClassifiedField>(), null, null, null);

    public static GuardStatus Failure(string detail) =>
        new(GuardLinkState.Failed, detail, Array.Empty<ClassifiedField>(), null, null, null);
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

    private readonly RSA _deviceKey;
    private readonly string? _runtimePublicKeyPem;
    private GuardAiClient? _client;
    private CancellationTokenSource? _loop;

    public GuardConnectionService(RSA deviceKey, string? runtimePublicKeyPem)
    {
        _deviceKey = deviceKey;
        _runtimePublicKeyPem = runtimePublicKeyPem;
    }

    /// <summary>Raised on every status change, including failures.</summary>
    public event Action<GuardStatus>? StatusChanged;

    public async Task ConnectAsync(GuardTransport transport, CancellationToken cancellationToken = default)
    {
        await StopAsync().ConfigureAwait(false);

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
                Publish(GuardStatus.Failure($"ricerca fallita ({ex.GetType().Name})"));
                return;
            }

            if (found is null)
            {
                Publish(GuardStatus.Failure(
                    "nessun runtime trovato sulla rete. Verificare che il PC sia sullo stesso Wi-Fi e che il runtime sia avviato."));
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
            Publish(GuardStatus.Failure(Explain(ex, transport)));
            return;
        }
        catch (OperationCanceledException)
        {
            await client.DisposeAsync().ConfigureAwait(false);
            Publish(GuardStatus.Idle);
            return;
        }

        Publish(Describe(session.TelemetryJson, endpoint));
        _loop = new CancellationTokenSource();
        _ = BeatAsync(endpoint, _loop.Token);
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
            Publish(GuardStatus.Failure(ex.Reason == "peer_disconnected"
                ? "sessione chiusa dal runtime."
                : $"sessione interrotta ({ex.Reason})."));
            await StopAsync().ConfigureAwait(false);
        }
    }

    private static GuardStatus Describe(string telemetryJson, string endpoint)
    {
        using var document = JsonDocument.Parse(telemetryJson);
        var root = document.RootElement;
        var client = root.GetProperty("client");

        var fields = new List<ClassifiedField>
        {
            Field(client, "processName", "Processo"),
            Field(client, "processId", "PID"),
            Field(client, "windowTitle", "Finestra"),
            Field(client, "processResponding", "Risponde"),
            Field(client, "windowVisible", "Visibile"),
            Field(client, "gameplayBaseline", "Gameplay"),
        };

        return new GuardStatus(
            GuardLinkState.Connected,
            client.TryGetProperty("status", out var clientStatus) ? clientStatus.GetString() : null,
            fields,
            root.TryGetProperty("runtimeStatus", out var status) ? status.GetString() : null,
            endpoint,
            DateTimeOffset.Now);
    }

    private static ClassifiedField Field(JsonElement client, string property, string label)
    {
        if (!client.TryGetProperty(property, out var field))
            return new ClassifiedField(label, null, "UNKNOWN");

        var source = field.TryGetProperty("source", out var s) ? s.GetString() ?? "UNKNOWN" : "UNKNOWN";
        var value = field.TryGetProperty("value", out var v) && v.ValueKind is not JsonValueKind.Null
            ? v.ToString()
            : null;

        // A value without a source, or a source of UNKNOWN, is not a reading.
        return value is null ? new ClassifiedField(label, null, "UNKNOWN") : new ClassifiedField(label, value, source);
    }

    public async Task StopAsync()
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
        _deviceKey.Dispose();
    }
}
