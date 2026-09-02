using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using NosAi.Runtime.Configuration;
using NosAi.Runtime.Gate1;
using NosAi.Runtime.Gate2;
using NosAi.Runtime.Gate3;

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

    /// <summary>
    /// Gate 3 loop state. Null unless this process hosts the runtime: an attached
    /// console cannot see another process's loop, and inventing zeros would hide that.
    /// </summary>
    public Gate3LoopView? DescribeDecisions()
    {
        lock (_gate)
            return _host?.Decisions?.Describe();
    }

    public async Task HaltAsync()
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
            hosted.RequestHalt();
            _logger.Operator("Halt richiesto al runtime ospitato.");
            return;
        }

        if (attachedPort is int port)
        {
            using var content = new StringContent(NosAi.Runtime.Safety.ImmediateHalt.CommandName);
            try
            {
                await Http.PostAsync($"http://127.0.0.1:{port}/api/command", content).ConfigureAwait(false);
                _logger.Operator("Halt inviato al runtime collegato.");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.Error("Invio halt fallito.", ex);
            }
        }
    }

    /// <summary>Durable event-log health. Hosted reads the store; attached asks the runtime.</summary>
    public async Task<EventLogHealth> ReadEventLogAsync(CancellationToken token = default)
    {
        lock (_gate)
        {
            if (_kind == SessionKind.Hosted)
                return EventLogDiagnostics.Inspect();
        }

        if (Kind == SessionKind.Attached && DashboardPort is int port)
        {
            try
            {
                var json = await Http.GetStringAsync($"http://127.0.0.1:{port}/api/event-log", token).ConfigureAwait(false);
                return ParseEventLog(json);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FormatException)
            {
                return EventLogHealth.Failed(EventLogDiagnostics.DefaultDatabasePath, false, $"event_log_unreachable:{ex.GetType().Name}");
            }
        }

        return EventLogHealth.Failed(EventLogDiagnostics.DefaultDatabasePath, false, "runtime_not_connected");
    }

    private static EventLogHealth ParseEventLog(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        bool readable = !root.TryGetProperty("readable", out var r) || r.GetBoolean();
        string path = root.TryGetProperty("databasePath", out var p) ? p.GetString() ?? EventLogDiagnostics.DefaultDatabasePath : EventLogDiagnostics.DefaultDatabasePath;
        if (!readable)
        {
            string reason = root.TryGetProperty("failureReason", out var f) ? f.GetString() ?? "event_log_unreadable" : "event_log_unreadable";
            bool exists = root.TryGetProperty("exists", out var e) && e.GetBoolean();
            return EventLogHealth.Failed(path, exists, reason);
        }

        var gaps = new List<EventLogGapReport>();
        if (root.TryGetProperty("gaps", out var gapNode) && gapNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var g in gapNode.EnumerateArray())
            {
                gaps.Add(new EventLogGapReport(
                    g.TryGetProperty("afterSequence", out var a) ? a.GetInt64() : 0,
                    g.TryGetProperty("lostCount", out var l) ? l.GetInt64() : 0,
                    g.TryGetProperty("reason", out var reason) ? reason.GetString() ?? "" : "",
                    g.TryGetProperty("detectedUtc", out var d) && d.TryGetDateTime(out var dt) ? dt : default));
            }
        }

        return new EventLogHealth(
            path,
            root.TryGetProperty("exists", out var ex) && ex.GetBoolean(),
            root.TryGetProperty("eventCount", out var c) ? c.GetInt64() : 0,
            root.TryGetProperty("gapCount", out var gc) ? gc.GetInt64() : gaps.Count,
            root.TryGetProperty("lostEventCount", out var lost) ? lost.GetInt64() : 0,
            root.TryGetProperty("firstSequence", out var fs) && fs.ValueKind == JsonValueKind.Number ? fs.GetInt64() : null,
            root.TryGetProperty("lastSequence", out var ls) && ls.ValueKind == JsonValueKind.Number ? ls.GetInt64() : null,
            root.TryGetProperty("firstEventUtc", out var fe) && fe.TryGetDateTime(out var fet) ? fet : null,
            root.TryGetProperty("lastEventUtc", out var le) && le.TryGetDateTime(out var let) ? let : null,
            gaps,
            Array.Empty<EventLogTailEntry>(),
            null);
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
