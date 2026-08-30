using System.Net;
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
    private readonly GuardAiNetworkChannel _channel;
    private readonly RealClientConnector _client;
    private readonly LiveHardwareTelemetry _hardware;
    private readonly Gate1RuntimeSnapshotProvider _snapshot;
    private readonly Gate1OperatorServer? _dashboard;
    private readonly string _correlationId = Guid.NewGuid().ToString("N");
    private RuntimeHealthStatus _health = RuntimeHealthStatus.Bootstrapping;
    private RSA? _devKey;
    private bool _disposed;

    public RuntimeHealthStatus Health => _health;
    public int GuardPort => _channel.LocalPort;
    public int? DashboardPort => _dashboard?.BoundPort;
    public Gate1RuntimeSnapshotProvider SnapshotProvider => _snapshot;
    public RealClientConnector Client => _client;
    public GuardAiNetworkChannel Channel => _channel;

    public Gate1BootstrapHost(Gate1HostOptions options, IRuntimeLogger? logger = null, IHardwareProbe? probe = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _logger = logger ?? new ConsoleRuntimeLogger();
        _auth = CreateAuth(_options, _logger, out _devKey);
        _channel = new GuardAiNetworkChannel(_options.GuardPort, _auth);
        _client = new RealClientConnector(_channel);
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
        _dashboard?.Start();
        _health = attached ? RuntimeHealthStatus.Healthy : RuntimeHealthStatus.Degraded;
        _logger.Info("Gate 1 runtime is listening.", new Dictionary<string, object?>
        {
            ["health"] = _health.ToString(),
            ["guardPort"] = GuardPort,
            ["dashboard"] = DashboardPort is int port ? $"http://127.0.0.1:{port}/" : "disabled"
        });
    }

    public Gate1CanonicalSnapshot Capture() => _snapshot.Capture();

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

    private static SessionAuth CreateAuth(Gate1HostOptions options, IRuntimeLogger logger, out RSA? devKey)
    {
        devKey = null;
        if (!string.IsNullOrWhiteSpace(options.TrustedGuardPublicKeyPem))
            return new SessionAuth(options.TrustedGuardPublicKeyPem);

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
            return new SessionAuth(rsa.ExportRSAPublicKeyPem());
        }

        logger.Warning("No trusted Guard public key configured. The channel is listening, but authentication will fail closed.", null);
        var ephemeral = RSA.Create(2048);
        try
        {
            return new SessionAuth(ephemeral.ExportRSAPublicKeyPem());
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
        if (_dashboard is not null)
            await _dashboard.DisposeAsync().ConfigureAwait(false);
        await _client.DisposeAsync().ConfigureAwait(false);
        _auth.Dispose();
        _devKey?.Dispose();
        _health = RuntimeHealthStatus.Stopped;
    }
}

public sealed class Gate1OperatorServer : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly Func<Gate1CanonicalSnapshot> _snapshot;
    private readonly Action<string> _commandHandler;
    private CancellationTokenSource? _cts;
    private readonly int _requestedPort;

    public int BoundPort { get; private set; }

    public Gate1OperatorServer(int port, Func<Gate1CanonicalSnapshot> snapshot, Action<string> commandHandler)
    {
        _requestedPort = port;
        _snapshot = snapshot;
        _commandHandler = commandHandler;
        _listener = new HttpListener();
        var bindPort = port == 0 ? 8765 : port;
        BoundPort = bindPort;
        _listener.Prefixes.Add($"http://127.0.0.1:{bindPort}/");
    }

    public void Start()
    {
        _listener.Start();
        BoundPort = _requestedPort == 0 ? BoundPort : _requestedPort;
        _cts = new CancellationTokenSource();
        _ = LoopAsync(_cts.Token);
    }

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync().ConfigureAwait(false);
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
            var path = context.Request.Url?.AbsolutePath ?? "/";
            var method = context.Request.HttpMethod;
            if (method == "GET" && (path == "/api/gate1" || path == "/api/telemetry" || path == "/api/state"))
            {
                WriteJson(context, 200, _snapshot().ToWire());
                return;
            }
            if (method == "GET" && path == "/api/health")
            {
                var snapshot = _snapshot();
                WriteJson(context, 200, new
                {
                    ok = snapshot.RuntimeStatus is RuntimeHealthStatus.Healthy or RuntimeHealthStatus.Degraded,
                    service = "gate1-operator",
                    runtimeStatus = snapshot.RuntimeStatus.ToString(),
                    contractVersion = snapshot.ContractVersion
                });
                return;
            }
            if (method == "POST" && path == "/api/command")
            {
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                var command = reader.ReadToEnd();
                _commandHandler(command);
                WriteJson(context, 202, new { status = "ACCEPTED", execution = "disabled_in_gate1" });
                return;
            }

            var html = Gate1DashboardHtml.Render();
            var bytes = Encoding.UTF8.GetBytes(html);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.StatusCode = 200;
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        }
        finally
        {
            context.Response.OutputStream.Close();
        }
    }

    private static void WriteJson(HttpListenerContext context, int status, object value)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions { WriteIndented = true });
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.StatusCode = status;
        context.Response.ContentLength64 = payload.Length;
        context.Response.OutputStream.Write(payload, 0, payload.Length);
    }

    public ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_listener.IsListening)
        {
            _listener.Stop();
            _listener.Close();
        }
        _cts?.Dispose();
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
