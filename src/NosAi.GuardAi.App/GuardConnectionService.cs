using System.Security.Cryptography;
using System.Text.Json;
using NosAi.GuardClient;

namespace NosAi.GuardAi.App;

/// <summary>What the UI is allowed to say about the link, and nothing more.</summary>
public enum GuardLinkState
{
    /// <summary>No session has been attempted. Not "offline": nothing is known yet.</summary>
    Idle,
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
    string? Capabilities,
    IReadOnlyList<ClassifiedField> Client,
    string? RuntimeStatus,
    DateTimeOffset? ObservedAt)
{
    public static GuardStatus Idle { get; } =
        new(GuardLinkState.Idle, null, null, Array.Empty<ClassifiedField>(), null, null);

    public static GuardStatus Failure(string reason) =>
        new(GuardLinkState.Failed, reason, null, Array.Empty<ClassifiedField>(), null, null);
}

/// <summary>
/// Owns the Guard AI session and turns snapshots into something the UI can show.
/// </summary>
/// <remarks>
/// <para>
/// The heartbeat loop is the whole point: the runtime terminates a session that
/// goes quiet for <see cref="GuardAiClient.HeartbeatTimeout"/>, so the app must
/// keep beating while it is on screen, and must show the session as lost the
/// moment it stops.
/// </para>
/// <para>
/// The device key is generated per install in this Gate 1 build and its public
/// half must be enrolled on the PC. That is a deliberate limitation, not a
/// finished key lifecycle: see the README for what is still missing.
/// </para>
/// </remarks>
public sealed class GuardConnectionService : IAsyncDisposable
{
    // Half the server deadline, so one lost beat does not drop the session.
    private static readonly TimeSpan BeatInterval = TimeSpan.FromMilliseconds(1000);

    private readonly RSA _deviceKey;
    private GuardAiClient? _client;
    private CancellationTokenSource? _loop;

    public GuardConnectionService(RSA deviceKey) => _deviceKey = deviceKey;

    /// <summary>Raised on every status change, including failures.</summary>
    public event Action<GuardStatus>? StatusChanged;

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        await StopAsync().ConfigureAwait(false);
        Publish(new GuardStatus(GuardLinkState.Connecting, $"{host}:{port}", null, Array.Empty<ClassifiedField>(), null, null));

        var client = new GuardAiClient(host, port, _deviceKey);
        GuardSession session;
        try
        {
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            session = await client.OpenSessionAsync(cancellationToken).ConfigureAwait(false);
            _client = client;
            Publish(Describe(session.Capabilities, session.TelemetryJson));
        }
        catch (GuardProtocolException ex)
        {
            await client.DisposeAsync().ConfigureAwait(false);
            Publish(GuardStatus.Failure(ex.Detail is null ? ex.Reason : $"{ex.Reason} ({ex.Detail})"));
            return;
        }
        catch (OperationCanceledException)
        {
            await client.DisposeAsync().ConfigureAwait(false);
            Publish(GuardStatus.Failure("cancelled"));
            return;
        }

        _loop = new CancellationTokenSource();
        // Capabilities are negotiated once at handshake; carry them into the loop so
        // every later status still reports what the runtime said it allows.
        _ = BeatAsync(session.Capabilities, _loop.Token);
    }

    private async Task BeatAsync(string? capabilities, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && _client is { } client)
            {
                await Task.Delay(BeatInterval, token).ConfigureAwait(false);
                var telemetry = await client.HeartbeatAsync(token).ConfigureAwait(false);
                Publish(Describe(capabilities, telemetry));
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
            Publish(GuardStatus.Failure(ex.Reason));
            await StopAsync().ConfigureAwait(false);
        }
    }

    private static GuardStatus Describe(string? capabilities, string telemetryJson)
    {
        using var document = JsonDocument.Parse(telemetryJson);
        var root = document.RootElement;
        var client = root.GetProperty("client");

        var fields = new List<ClassifiedField>
        {
            Field(client, "processName", "Process"),
            Field(client, "processId", "PID"),
            Field(client, "windowTitle", "Window"),
            Field(client, "processResponding", "Responding"),
            Field(client, "windowVisible", "Visible"),
            Field(client, "gameplayBaseline", "Gameplay"),
        };

        var runtimeStatus = root.TryGetProperty("runtimeStatus", out var status) ? status.GetString() : null;
        return new GuardStatus(
            GuardLinkState.Connected,
            client.TryGetProperty("status", out var clientStatus) ? clientStatus.GetString() : null,
            capabilities,
            fields,
            runtimeStatus,
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
