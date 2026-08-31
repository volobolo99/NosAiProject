using System.Diagnostics;
using System.IO;
using System.Net.Http;
using NosAi.Runtime.Configuration;
using NosAi.Runtime.Gate1;
using NosAi.Runtime.Observability;

namespace NosAi.ControlPanel;

public enum SessionKind
{
    Idle,
    Hosted,
    Attached
}

/// <summary>
/// Owns the Gate 1 host or attaches to one already listening. The panel never
/// executes game actions itself.
/// </summary>
public sealed class RuntimeSession : IAsyncDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMilliseconds(800) };

    private readonly UiLogger _logger;
    private readonly object _gate = new();
    private Gate1BootstrapHost? _host;
    private SessionKind _kind = SessionKind.Idle;

    public RuntimeSession(UiLogger logger) => _logger = logger;

    public SessionKind Kind
    {
        get { lock (_gate) return _kind; }
    }

    public bool IsLive => Kind != SessionKind.Idle;

    public int? DashboardPort { get; private set; }
    public int GuardPort { get; private set; }
    public string? Detail { get; private set; }
    public string? LastFailure { get; private set; }

    public void NoteFailure(string reason) => LastFailure = reason;

    public async Task<bool> ProbeExistingAsync(int dashboardPort, CancellationToken token = default)
    {
        try
        {
            using var response = await Http.GetAsync($"http://127.0.0.1:{dashboardPort}/api/health", token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return false;
        }
    }

    public async Task StartHostedAsync(Gate1HostOptions options)
    {
        await StopAsync().ConfigureAwait(false);
        var host = new Gate1BootstrapHost(options, _logger);
        await host.StartAsync().ConfigureAwait(false);
        lock (_gate)
        {
            _host = host;
            _kind = SessionKind.Hosted;
            DashboardPort = host.DashboardPort;
            GuardPort = host.GuardPort;
            Detail = host.DashboardPort is int port
                ? $"API operatore http://127.0.0.1:{port}/"
                : host.DashboardFailureReason ?? "API operatore non disponibile";
        }

        LastFailure = null;
        _logger.Operator($"Runtime avviato. Guard {host.GuardPort}.");
    }

    public void Attach(int dashboardPort, int guardPort)
    {
        lock (_gate)
        {
            _kind = SessionKind.Attached;
            DashboardPort = dashboardPort;
            GuardPort = guardPort;
            Detail = $"collegato a un runtime già in ascolto su http://127.0.0.1:{dashboardPort}/";
        }

        LastFailure = null;
        _logger.Operator($"Collegato al runtime esistente sulla porta {dashboardPort}.");
    }

    public SnapshotView Capture()
    {
        lock (_gate)
        {
            if (_kind == SessionKind.Hosted && _host is not null)
                return SnapshotView.From(_host.Capture());
        }

        return SnapshotView.Empty(Kind == SessionKind.Attached
            ? "lettura HTTP in corso"
            : "runtime non avviato");
    }

    public async Task<SnapshotView> CaptureAsync(CancellationToken token = default)
    {
        lock (_gate)
        {
            if (_kind == SessionKind.Hosted && _host is not null)
                return SnapshotView.From(_host.Capture());
        }

        if (Kind == SessionKind.Attached && DashboardPort is int port)
        {
            try
            {
                var json = await Http.GetStringAsync($"http://127.0.0.1:{port}/api/gate1", token).ConfigureAwait(false);
                return AttachedSnapshot.Parse(json);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FormatException)
            {
                LastFailure = $"runtime_unreachable: {ex.GetType().Name}";
                return SnapshotView.Empty(LastFailure);
            }
        }

        return SnapshotView.Empty("runtime non avviato");
    }

    public async Task EmergencyStopAsync()
    {
        Gate1BootstrapHost? hosted;
        int? attachedPort;
        lock (_gate)
        {
            hosted = _host;
            attachedPort = _kind == SessionKind.Attached ? DashboardPort : null;
        }

        if (hosted is not null)
        {
            hosted.RequestEmergencyStop();
            _logger.Operator("Arresto di emergenza richiesto al runtime ospitato.");
            return;
        }

        if (attachedPort is int port)
        {
            using var content = new StringContent("EMERGENCY_STOP");
            try
            {
                await Http.PostAsync($"http://127.0.0.1:{port}/api/command", content).ConfigureAwait(false);
                _logger.Operator("Arresto di emergenza inviato al runtime collegato.");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.Error("Invio arresto di emergenza fallito.", ex);
            }
        }
    }

    public async Task StopAsync()
    {
        Gate1BootstrapHost? hosted;
        lock (_gate)
        {
            hosted = _host;
            _host = null;
            _kind = SessionKind.Idle;
            DashboardPort = null;
            Detail = null;
        }

        if (hosted is not null)
        {
            await hosted.DisposeAsync().ConfigureAwait(false);
            _logger.Operator("Runtime fermato.");
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
