using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NosAi.LiveIntegration;
using NosAi.Runtime.Configuration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Hardware;
using NosAi.Runtime.Observability;
using NosAi.Runtime.Orchestration;
using NosAi.Runtime.WorldModel;

namespace NosAi.Runtime.Gate1;

public sealed class Gate1BootstrapHost : IAsyncDisposable
{
    private readonly Gate1HostOptions _options;
    private readonly IRuntimeLogger _logger;
    private readonly SessionAuth _auth;
    private readonly RuntimeIdentity _runtimeIdentity;
    private readonly GuardAiNetworkChannel _channel;
    private readonly RealClientConnector _client;
    private readonly LiveHardwareTelemetry _hardware;
    private readonly Gate1RuntimeSnapshotProvider _snapshot;
    private readonly Gate1OperatorServer? _dashboard;
    private DiscoveryResponder? _discovery;
    private readonly string _correlationId = Guid.NewGuid().ToString("N");
    private RuntimeHealthStatus _health = RuntimeHealthStatus.Bootstrapping;
    private RSA? _devKey;
    private bool _disposed;

    public RuntimeHealthStatus Health => _health;
    public int GuardPort => _channel.LocalPort;

    /// <summary>The port the operator dashboard is actually listening on, or null when it is disabled or failed to bind.</summary>
    public int? DashboardPort => _dashboard?.BoundPort;

    /// <summary>Whether the LAN discovery responder is answering probes.</summary>
    public bool DiscoveryListening => _discovery?.IsListening == true;

    /// <summary>Why discovery is not answering; null while it is.</summary>
    public string? DiscoveryFailureReason { get; private set; } = "discovery_not_started";

    /// <summary>
    /// Structured reason the operator dashboard is not serving: <c>dashboard_disabled</c>
    /// when it was never requested, otherwise the bind failure. Null while it is up.
    /// </summary>
    public string? DashboardFailureReason { get; private set; } = "dashboard_not_started";
    public Gate1RuntimeSnapshotProvider SnapshotProvider => _snapshot;
    public RealClientConnector Client => _client;
    public GuardAiNetworkChannel Channel => _channel;

    public Gate1BootstrapHost(Gate1HostOptions options, IRuntimeLogger? logger = null, IHardwareProbe? probe = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _logger = logger ?? new ConsoleRuntimeLogger();
        _runtimeIdentity = RuntimeIdentity.LoadOrCreate();
        _logger.Info("Runtime identity loaded.", new Dictionary<string, object?>
        {
            ["publicKeyPath"] = RuntimeIdentity.PublicPathFor(RuntimeIdentity.DefaultPath)
        });
        _auth = CreateAuth(_options, _logger, _runtimeIdentity, out _devKey);
        _channel = new GuardAiNetworkChannel(
            _options.GuardPort, _auth,
            // Loopback alone would make the Wi-Fi transport impossible: the phone
            // dials this machine's LAN address, not its loopback.
            _options.GuardLoopbackOnly ? System.Net.IPAddress.Loopback : System.Net.IPAddress.Any);
        _client = new RealClientConnector(_channel, _options.ClientProcessName);
        var safeProbe = new SafeHardwareProbe(probe ?? CreateDefaultProbe());
        _hardware = new LiveHardwareTelemetry(safeProbe);
        var runtime = RuntimeComposition.CreateSafe();
        var world = new NosAi.Runtime.WorldModel.WorldModel();
        _snapshot = new Gate1RuntimeSnapshotProvider(
            runtime,
            world,
            _channel,
            _hardware,
            _client,
            () => _health,
            _correlationId);
        _channel.SetSnapshotSource(_snapshot.Capture);
        _dashboard = _options.StartDashboard
            ? new Gate1OperatorServer(_options.DashboardPort, _snapshot.Capture, HandleOperatorCommand)
            : null;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _health = RuntimeHealthStatus.Bootstrapping;
        _logger.Info("Gate 1 bootstrap starting.", new Dictionary<string, object?>
        {
            ["correlationId"] = _correlationId,
            ["guardPort"] = _options.GuardPort,
            ["dashboardPort"] = _options.DashboardPort
        });

        var attached = _client.VerifyAndAttachClient();
        _logger.Info(attached ? "NosTale client attached." : "NosTale client not attached; snapshot remains explicit.", new Dictionary<string, object?>
        {
            ["attached"] = attached,
            ["failure"] = attached ? null : _client.CaptureBaselineSnapshot().FailureReason
        });

        await _client.StartRealNetworkTransportAsync(cancellationToken).ConfigureAwait(false);
        StartDashboard();
        StartDiscovery();
        _health = attached ? RuntimeHealthStatus.Healthy : RuntimeHealthStatus.Degraded;
        _logger.Info("Gate 1 runtime is listening.", new Dictionary<string, object?>
        {
            ["health"] = _health.ToString(),
            ["guardPort"] = GuardPort,
            ["dashboard"] = DashboardPort is int port ? $"http://127.0.0.1:{port}/" : "unavailable",
            ["dashboardFailure"] = DashboardFailureReason,
            ["discovery"] = DiscoveryListening ? $"udp/{DiscoveryProtocol.Port}" : "unavailable",
            ["discoveryFailure"] = DiscoveryFailureReason
        });
    }

    /// <summary>
    /// Starts the LAN discovery responder so the phone can find this runtime
    /// without being given an address.
    /// </summary>
    /// <remarks>
    /// Like the dashboard and unlike the Guard channel, a failure to bind degrades
    /// the feature rather than the runtime: discovery is a convenience, and the
    /// phone can always be pointed at an address by hand. It answers probes only —
    /// it grants nothing and every authorisation still happens in the handshake.
    /// </remarks>
    private void StartDiscovery()
    {
        if (!_options.EnableDiscovery)
        {
            DiscoveryFailureReason = "discovery_disabled";
            return;
        }

        var responder = new DiscoveryResponder(GuardPort);
        if (responder.TryStart(out var failureReason))
        {
            _discovery = responder;
            DiscoveryFailureReason = null;
            return;
        }

        DiscoveryFailureReason = failureReason;
        _logger.Error(
            "LAN discovery could not bind; the runtime continues without it.",
            new InvalidOperationException(failureReason ?? "discovery_bind_failed"),
            new Dictionary<string, object?>
            {
                ["port"] = DiscoveryProtocol.Port,
                ["reason"] = failureReason,
                ["remedy"] = "Free the port or pass --no-discovery; the phone can still be given an address."
            });
    }

    /// <summary>
    /// The dashboard is an observability surface, not a safety gate. A port already
    /// held by another process used to throw out of <see cref="StartAsync"/> and kill
    /// the whole runtime, taking the Guard channel and the attached client with it.
    /// It now degrades to "no dashboard" and says why.
    /// </summary>
    private void StartDashboard()
    {
        if (_dashboard is null)
        {
            DashboardFailureReason = "dashboard_disabled";
            return;
        }

        if (_dashboard.TryStart(out var failureReason))
        {
            DashboardFailureReason = null;
            return;
        }

        DashboardFailureReason = failureReason;
        _logger.Error(
            "Operator dashboard could not bind; the runtime continues without it.",
            new InvalidOperationException(failureReason ?? "dashboard_bind_failed"),
            new Dictionary<string, object?>
            {
                ["requestedPort"] = _options.DashboardPort,
                ["reason"] = failureReason,
                ["remedy"] = "Free the port, pass --dashboard-port <n>, or pass --dashboard-port 0 for an ephemeral port."
            });
    }

    public Gate1CanonicalSnapshot Capture() => _snapshot.Capture();

    /// <summary>
    /// Operator request for an emergency stop. Same path as POST /api/command:
    /// the UI may ask, it does not enforce policy. Execution stays disabled in Gate 1.
    /// </summary>
    public void RequestEmergencyStop() => HandleOperatorCommand("EMERGENCY_STOP");

    private void HandleOperatorCommand(string command)
    {
        if (command.Contains("EMERGENCY_STOP", StringComparison.OrdinalIgnoreCase)
            || command.Contains("\"action\":\"stop\"", StringComparison.OrdinalIgnoreCase))
        {
            _health = RuntimeHealthStatus.Failed;
            _channel.TerminateSession("operator_emergency_stop");
            _logger.Warning("Operator emergency stop accepted; execution remains disabled in Gate 1.", new Dictionary<string, object?>
            {
                ["health"] = _health.ToString()
            });
        }
    }

    private static IHardwareProbe CreateDefaultProbe()
    {
        if (OperatingSystem.IsWindows())
            return CreateWindowsProbe();
        return new FallbackHardwareProbe();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static IHardwareProbe CreateWindowsProbe() => new WindowsHardwareProbe();

    private static SessionAuth CreateAuth(Gate1HostOptions options, IRuntimeLogger logger, RuntimeIdentity identity, out RSA? devKey)
    {
        devKey = null;
        if (!string.IsNullOrWhiteSpace(options.TrustedGuardPublicKeyPem))
        {
            logger.Info("Trusting one Guard device key.", new Dictionary<string, object?>
            {
                ["source"] = options.TrustedGuardPublicKeySource ?? "explicit"
            });
            return new SessionAuth(options.TrustedGuardPublicKeyPem, identity);
        }

        if (options.DevEnrollment)
        {
            var rsa = RSA.Create(2048);
            devKey = rsa;
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NosAi", "Gate1");
            Directory.CreateDirectory(directory);
            var publicPath = Path.Combine(directory, "guard-dev-public.pem");
            var privatePath = Path.Combine(directory, "guard-dev-private.pem");
            File.WriteAllText(publicPath, rsa.ExportRSAPublicKeyPem());
            File.WriteAllText(privatePath, rsa.ExportRSAPrivateKeyPem());
            logger.Warning("Dev enrollment keypair written. This is not a production trust root.", new Dictionary<string, object?>
            {
                ["publicKeyPath"] = publicPath,
                ["privateKeyPath"] = privatePath
            });
            return new SessionAuth(rsa.ExportRSAPublicKeyPem(), identity);
        }

        logger.Warning(
            "No trusted Guard device key. The channel is listening, but every session will be refused. "
            + $"Pair a phone (python -m nosai.phone.deploy) or pass --guard-public-key-path; the default is {Gate1HostOptions.DefaultTrustedKeyPath}.",
            null);
        var ephemeral = RSA.Create(2048);
        try
        {
            return new SessionAuth(ephemeral.ExportRSAPublicKeyPem(), identity);
        }
        finally
        {
            ephemeral.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _health = RuntimeHealthStatus.Stopping;
        if (_discovery is not null)
            await _discovery.DisposeAsync().ConfigureAwait(false);
        if (_dashboard is not null)
            await _dashboard.DisposeAsync().ConfigureAwait(false);
        await _client.DisposeAsync().ConfigureAwait(false);
        _auth.Dispose();
        _runtimeIdentity.Dispose();
        _devKey?.Dispose();
        _health = RuntimeHealthStatus.Stopped;
    }
}

/// <summary>
/// Read-mostly operator surface for Gate 1: it serves the classified snapshot and
/// accepts the emergency-stop command. It is an observability surface, not a
/// safety gate, so a failure to bind must degrade the dashboard and never take
/// the runtime (Guard channel, client attachment) down with it.
/// </summary>
public sealed class Gate1OperatorServer : IAsyncDisposable
{
    /// <summary>Attempts used when resolving an ephemeral port (port 0).</summary>
    private const int EphemeralBindAttempts = 8;

    private readonly Func<Gate1CanonicalSnapshot> _snapshot;
    private readonly Action<string> _commandHandler;
    private readonly int _requestedPort;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    /// <summary>
    /// The port actually bound, or <c>null</c> while the server is not listening.
    /// It is never a guess: reporting the requested port before the bind succeeded
    /// is what previously made an unreachable dashboard look reachable.
    /// </summary>
    public int? BoundPort { get; private set; }

    /// <summary>Why the last start attempt failed; <c>null</c> while listening.</summary>
    public string? FailureReason { get; private set; }

    public bool IsListening => _listener?.IsListening == true;

    public Gate1OperatorServer(int port, Func<Gate1CanonicalSnapshot> snapshot, Action<string> commandHandler)
    {
        if (port is < 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), port, "Dashboard port must be between 0 and 65535.");
        _requestedPort = port;
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _commandHandler = commandHandler ?? throw new ArgumentNullException(nameof(commandHandler));
    }

    /// <summary>
    /// Binds the loopback listener. Returns false with a structured reason instead
    /// of throwing, so the caller can keep the runtime alive without the dashboard.
    /// </summary>
    public bool TryStart(out string? failureReason)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsListening)
        {
            failureReason = null;
            return true;
        }

        failureReason = _requestedPort == 0 ? BindEphemeral() : BindFixed(_requestedPort);
        FailureReason = failureReason;
        if (failureReason is not null)
        {
            BoundPort = null;
            return false;
        }

        _cts = new CancellationTokenSource();
        _ = LoopAsync(_cts.Token);
        return true;
    }

    /// <summary>Starts the dashboard or throws. Use <see cref="TryStart"/> when the caller must survive a busy port.</summary>
    public void Start()
    {
        if (!TryStart(out var reason))
            throw new InvalidOperationException($"Gate 1 operator dashboard could not start: {reason}");
    }

    /// <summary>
    /// An explicitly requested port is never silently swapped for another one: an
    /// operator who pinned a port would otherwise be told the dashboard is up at
    /// an address that serves nothing.
    /// </summary>
    private string? BindFixed(int port)
    {
        var listener = CreateListener(port);
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            listener.Close();
            return DescribeBindFailure(port, ex);
        }
        catch (ObjectDisposedException)
        {
            return $"dashboard_port_unavailable:{port}";
        }

        _listener = listener;
        BoundPort = port;
        return null;
    }

    /// <summary>
    /// Port 0 means "any free loopback port". HttpListener cannot bind 0 itself, so
    /// a socket picks a free port first; the gap between probe and bind is a race,
    /// hence the retries.
    /// </summary>
    private string? BindEphemeral()
    {
        string? lastFailure = null;
        for (var attempt = 0; attempt < EphemeralBindAttempts; attempt++)
        {
            int candidate;
            try
            {
                candidate = ReserveFreeLoopbackPort();
            }
            catch (SocketException ex)
            {
                return $"dashboard_port_probe_failed:{ex.SocketErrorCode}";
            }

            lastFailure = BindFixed(candidate);
            if (lastFailure is null)
                return null;
        }

        return lastFailure ?? "dashboard_ephemeral_port_unavailable";
    }

    private static int ReserveFreeLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    private static HttpListener CreateListener(int port)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        return listener;
    }

    /// <summary>
    /// Maps the Win32 error behind HttpListenerException onto a reason an operator
    /// can act on. Error 32 (sharing violation) is what a second process already
    /// holding the port looks like, and it used to surface only as a stack trace.
    /// </summary>
    private static string DescribeBindFailure(int port, HttpListenerException ex)
        => ex.ErrorCode switch
        {
            32 or 183 => $"dashboard_port_in_use:{port}",
            5 => $"dashboard_port_access_denied:{port}",
            _ => $"dashboard_bind_failed:{port}:{ex.ErrorCode}"
        };

    private async Task LoopAsync(CancellationToken token)
    {
        var listener = _listener;
        if (listener is null)
            return;

        while (!token.IsCancellationRequested && listener.IsListening)
        {
            try
            {
                var context = await listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => Handle(context), token);
            }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (OperationCanceledException) { break; }
        }
    }

    private void Handle(HttpListenerContext context)
    {
        try
        {
            // The response is materialised before a single byte is written, so a
            // snapshot that throws yields an honest 500 instead of a truncated 200
            // that the dashboard would parse as live data.
            var (status, contentType, body) = BuildResponse(context);
            context.Response.StatusCode = status;
            context.Response.ContentType = contentType;
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.ContentLength64 = body.Length;
            context.Response.OutputStream.Write(body, 0, body.Length);
        }
        catch (HttpListenerException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            try { context.Response.OutputStream.Close(); }
            catch (HttpListenerException) { }
            catch (ObjectDisposedException) { }
        }
    }

    private (int Status, string ContentType, byte[] Body) BuildResponse(HttpListenerContext context)
    {
        var path = context.Request.Url?.AbsolutePath ?? "/";
        var method = context.Request.HttpMethod;
        try
        {
            if (method == "GET" && (path == "/api/gate1" || path == "/api/telemetry" || path == "/api/state"))
                return Json(200, _snapshot().ToWire());

            if (method == "GET" && path == "/api/health")
            {
                var snapshot = _snapshot();
                return Json(200, new
                {
                    ok = snapshot.RuntimeStatus is RuntimeHealthStatus.Healthy or RuntimeHealthStatus.Degraded,
                    service = "gate1-operator",
                    runtimeStatus = snapshot.RuntimeStatus.ToString(),
                    contractVersion = snapshot.ContractVersion
                });
            }

            if (method == "POST" && path == "/api/command")
            {
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                _commandHandler(reader.ReadToEnd());
                return Json(202, new { status = "ACCEPTED", execution = "disabled_in_gate1" });
            }

            return (200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(Gate1DashboardHtml.Render()));
        }
        catch (Exception ex)
        {
            return Json(500, new { error = "operator_endpoint_failed", reason = $"{ex.GetType().Name}: {ex.Message}" });
        }
    }

    private static (int Status, string ContentType, byte[] Body) Json(int status, object value)
        => (status,
            "application/json; charset=utf-8",
            JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions { WriteIndented = true }));

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;
        _disposed = true;
        _cts?.Cancel();
        if (_listener is not null)
        {
            if (_listener.IsListening)
                _listener.Stop();
            _listener.Close();
            _listener = null;
        }
        _cts?.Dispose();
        BoundPort = null;
        return ValueTask.CompletedTask;
    }
}

internal static class Gate1DashboardHtml
{
    public static string Render() => """
<!DOCTYPE html>
<html lang="it">
<head>
  <meta charset="UTF-8">
  <title>NosAi Gate 1 — Operator Dashboard</title>
  <style>
    body { font-family: Segoe UI, sans-serif; background:#0f172a; color:#f8fafc; margin:0; padding:24px; }
    h1 { font-size:22px; margin:0 0 8px; }
    .muted { color:#94a3b8; font-size:13px; }
    .grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(240px,1fr)); gap:16px; margin-top:20px; }
    .card { background:#1e293b; border:1px solid #334155; border-radius:10px; padding:16px; }
    .card h2 { font-size:14px; color:#38bdf8; margin:0 0 12px; }
    .value { font-size:20px; font-weight:700; }
    .src { font-size:11px; letter-spacing:.6px; color:#7dd3fc; }
    .warn { color:#fbbf24; font-size:12px; }
    pre { background:#020617; padding:12px; border-radius:8px; overflow:auto; font-size:12px; }
    button { background:#ef4444; color:white; border:0; padding:10px 16px; border-radius:6px; cursor:pointer; font-weight:700; }
  </style>
</head>
<body>
  <h1>NosAi Gate 1 — Operator Dashboard</h1>
  <p class="muted">Values are shown only with their source classification. UNKNOWN is not replaced by zero or demo data.</p>
  <p>Runtime: <span class="value" id="runtime">…</span> · Guard: <span id="guard">…</span> · Client: <span id="client">…</span></p>
  <div class="grid">
    <div class="card"><h2>PC hardware</h2><div class="value" id="cpu">—</div><div class="src" id="cpu-src"></div></div>
    <div class="card"><h2>Process RAM</h2><div class="value" id="ram">—</div><div class="src" id="ram-src"></div></div>
    <div class="card"><h2>NosTale client</h2><div class="value" id="client-status">—</div><div class="src" id="client-src"></div></div>
    <div class="card"><h2>Guard AI session</h2><div class="value" id="guard-status">—</div><div class="src" id="guard-src"></div></div>
  </div>
  <p class="warn" id="warning"></p>
  <p><button onclick="fetch('/api/command',{method:'POST',body:'EMERGENCY_STOP'})">EMERGENCY STOP</button></p>
  <pre id="raw">Loading classified snapshot…</pre>
  <script>
    function field(obj, fallback) {
      if (!obj) return fallback;
      if (obj.source === 'UNKNOWN' || obj.value === null || obj.value === undefined) return 'UNKNOWN';
      return obj.value;
    }
    function src(obj) { return obj && obj.source ? obj.source : 'UNKNOWN'; }
    async function refresh() {
      try {
        const s = await (await fetch('/api/gate1')).json();
        document.getElementById('runtime').textContent = s.runtimeStatus;
        document.getElementById('cpu').textContent = field(s.hardware.cpu, 'UNKNOWN');
        document.getElementById('cpu-src').textContent = src(s.hardware.cpu);
        document.getElementById('ram').textContent = field(s.hardware.processWorkingSetMb, 'UNKNOWN');
        document.getElementById('ram-src').textContent = src(s.hardware.processWorkingSetMb);
        document.getElementById('client').textContent = s.client.status;
        document.getElementById('client-status').textContent = s.client.status;
        document.getElementById('client-src').textContent = src(s.client.attached);
        document.getElementById('guard-status').textContent = field(s.guard.authenticated, false) === true ? 'AUTHENTICATED' : (field(s.guard.connected, false) === true ? 'CONNECTED' : 'DISCONNECTED');
        document.getElementById('guard-src').textContent = src(s.guard.connected);
        document.getElementById('warning').textContent = s.warning || s.client.warning || '';
        document.getElementById('raw').textContent = JSON.stringify(s, null, 2);
      } catch (e) {
        document.getElementById('runtime').textContent = 'UNKNOWN';
        document.getElementById('warning').textContent = 'Dashboard cannot reach the Gate 1 snapshot.';
      }
    }
    refresh();
    setInterval(refresh, 2000);
  </script>
</body>
</html>
""";
}
