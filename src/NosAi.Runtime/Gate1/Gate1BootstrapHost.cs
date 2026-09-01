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
using NosAi.Runtime.Safety;
using NosAi.Runtime.Testing;
using NosAi.Runtime.Security;
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

    /// <summary>
    /// Keeps <see cref="_correlationId"/> current for every log line this host and
    /// everything it starts (accept loop, watchdog, discovery, dashboard) emits for
    /// the rest of its life. Disposed only in <see cref="DisposeAsync"/>.
    /// </summary>
    private readonly IDisposable _correlationScope;
    private RuntimeHealthStatus _health = RuntimeHealthStatus.Bootstrapping;
    private RSA? _devKey;
    private bool _disposed;

    /// <summary>The composed runtime, held so the safety switches stay reachable.</summary>
    private readonly RuntimeComponents _runtime;

    /// <summary>The test console behind /tests. Null when the repository is not on disk.</summary>
    private readonly TestConsoleService? _testConsole;

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
    /// <summary>
    /// The environment preconditions as they were observed for this start. Always
    /// satisfied for a host that exists: an unsatisfied report throws out of the
    /// constructor rather than producing a host.
    /// </summary>
    public EnvironmentReport EnvironmentReport { get; }

    public Gate1RuntimeSnapshotProvider SnapshotProvider => _snapshot;
    public RealClientConnector Client => _client;
    public GuardAiNetworkChannel Channel => _channel;

    public Gate1BootstrapHost(Gate1HostOptions options, IRuntimeLogger? logger = null, IHardwareProbe? probe = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _logger = logger ?? new ConsoleRuntimeLogger();
        // Opened before the first log call so "Runtime identity loaded." -- until
        // now always correlationId=none -- carries the same id as every line after
        // it, including the gate1.snapshot.v1 this host will go on to publish.
        _correlationScope = CorrelationScope.Begin(_correlationId);

        // Checked before anything touches the disk. RuntimeIdentity.LoadOrCreate
        // below writes into the data directory, and when that directory was not
        // writable the first sign of it was an IOException from inside key
        // custody -- several frames from anything that named the directory.
        EnvironmentReport = RuntimeEnvironmentValidator.Validate(_options);
        foreach (EnvironmentCheck check in EnvironmentReport.Checks)
        {
            var properties = new Dictionary<string, object?>
            {
                ["check"] = check.Name,
                ["status"] = check.Status.ToString(),
                ["required"] = check.Required,
                ["detail"] = check.Detail
            };
            if (check.Status == EnvironmentCheckStatus.Passed)
                _logger.Info("Environment check passed.", properties);
            else
                _logger.Warning("Environment check did not pass.", properties);
        }

        if (!EnvironmentReport.IsSatisfied)
        {
            // Fail closed: a required precondition that was not confirmed stops the
            // boot rather than degrading it. Everything this host would go on to
            // build assumes these hold, so starting anyway would only move the
            // failure somewhere harder to read.
            _correlationScope.Dispose();
            throw new RuntimeEnvironmentException(EnvironmentReport);
        }

        _runtimeIdentity = RuntimeIdentity.LoadOrCreate();
        _logger.Info("Runtime identity loaded.", new Dictionary<string, object?>
        {
            ["publicKeyPath"] = RuntimeIdentity.PublicPathFor(RuntimeIdentity.DefaultPath),
            ["protectedKeyPath"] = RuntimeIdentity.ProtectedPathFor(RuntimeIdentity.DefaultPath)
        });
        // A readable private key is what ADR-0010 removes. If migration could not
        // delete the old one, the runtime still starts, but silence here would let
        // the file sit there indefinitely with the decision looking applied.
        if (_runtimeIdentity.UnprotectedRemnantPath is { } remnant)
        {
            _logger.Warning("A plaintext runtime identity is still on disk; delete it by hand.", new Dictionary<string, object?>
            {
                ["path"] = remnant,
                ["reason"] = "identity_plaintext_not_removed"
            });
        }
        _auth = CreateAuth(_options, _logger, _runtimeIdentity, out _devKey);
        _channel = new GuardAiNetworkChannel(
            _options.GuardPort, _auth,
            // Loopback alone would make the Wi-Fi transport impossible: the phone
            // dials this machine's LAN address, not its loopback.
            _options.GuardLoopbackOnly ? System.Net.IPAddress.Loopback : System.Net.IPAddress.Any);
        _client = new RealClientConnector(_channel, _options.ClientProcessName);
        var safeProbe = new SafeHardwareProbe(probe ?? CreateDefaultProbe());
        _hardware = new LiveHardwareTelemetry(safeProbe);
        var runtime = _runtime = RuntimeComposition.CreateSafe();
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
        _testConsole = BuildTestConsole();
        _dashboard = _options.StartDashboard
            ? new Gate1OperatorServer(_options.DashboardPort, _snapshot.Capture, HandleOperatorCommand,
                safetyState: SafetyState, safetySetter: SetSafetySwitch, tests: _testConsole)
            : null;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _health = RuntimeHealthStatus.Bootstrapping;
        // correlationId is no longer repeated here as a property: CorrelationScope
        // (opened in the constructor) already stamps it on this line and on every
        // other line this host emits.
        _logger.Info("Gate 1 bootstrap starting.", new Dictionary<string, object?>
        {
            ["guardPort"] = _options.GuardPort,
            ["dashboardPort"] = _options.DashboardPort
        });

        // Discovery runs alongside the bootstrap: the operator page must not wait
        // on it, and the runtime must not wait on the page.
        BeginTestDiscovery();

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

    /// <summary>
    /// Builds the test console, or nothing when the repository is not reachable.
    /// </summary>
    /// <remarks>
    /// A published build running away from its sources cannot execute the suites,
    /// and the page says so rather than showing an empty inventory that would read
    /// as "there are no tests".
    /// </remarks>
    private static TestConsoleService? BuildTestConsole()
    {
        string? root = TestSuiteRunner.FindRepositoryRoot(Environment.CurrentDirectory)
                       ?? TestSuiteRunner.FindRepositoryRoot();
        if (root is null)
            return null;

        var catalog = new TestCatalog(Path.Combine(root, "data", "test_evidence.json"));
        var suites = new TestSuiteRunner(root);
        var gates = new GateCertificationRunner(CertificationSuites.Resolve);
        return new TestConsoleService(catalog, suites, gates);
    }

    /// <summary>Starts test discovery without blocking the runtime's startup.</summary>
    private void BeginTestDiscovery()
    {
        if (_testConsole is null)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await _testConsole.DiscoverAllAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warning("Test discovery failed; the console lists what it already knew.",
                    new Dictionary<string, object?> { ["reason"] = $"{ex.GetType().Name}: {ex.Message}" });
            }
        });
    }

    private void HandleOperatorCommand(string command)
    {
        if (command.Contains("EMERGENCY_STOP", StringComparison.OrdinalIgnoreCase)
            || command.Contains("\"action\":\"stop\"", StringComparison.OrdinalIgnoreCase))
        {
            // Disarm first, then tear the session down: an emergency stop that
            // dropped the session while input stayed armed would leave the
            // dangerous half running.
            _runtime.Safety.EmergencyStop("operator_emergency_stop");
            _health = RuntimeHealthStatus.Failed;
            _channel.TerminateSession("operator_emergency_stop");
            _logger.Warning("Operator emergency stop accepted; every acting power disarmed.", new Dictionary<string, object?>
            {
                ["health"] = _health.ToString(),
                ["liveInput"] = _runtime.SafetyPolicy.LiveInputEnabled,
                ["packetInjection"] = _runtime.SafetyPolicy.PacketInjectionEnabled
            });
        }
    }

    /// <summary>
    /// Applies an operator switch change and reports the decision.
    /// </summary>
    /// <remarks>
    /// The operator surface may <i>ask</i>; the runtime decides (ADR-0003). The
    /// principal is fixed to <see cref="SecurityPrincipal.Operator"/> here because
    /// this endpoint is the person at the machine — a request arriving over the
    /// Guard channel goes through a different path and does not reach it.
    /// </remarks>
    public AuthorizationDecision SetSafetySwitch(SafetySwitch which, bool value)
    {
        var decision = _runtime.Safety.Set(SecurityPrincipal.Operator, which, value, "operator_api");
        _logger.Warning("Operator changed a safety switch.", new Dictionary<string, object?>
        {
            ["switch"] = which.ToString(),
            ["requested"] = value,
            ["allowed"] = decision.Allowed,
            ["reason"] = decision.Reason,
            ["executionMode"] = _runtime.Safety.ExecutionMode
        });
        return decision;
    }

    /// <summary>The current switch state and its history, for the operator surface.</summary>
    public object SafetyState() => new
    {
        executionMode = _runtime.Safety.ExecutionMode,
        switches = new
        {
            liveInput = _runtime.SafetyPolicy.LiveInputEnabled,
            packetInjection = _runtime.SafetyPolicy.PacketInjectionEnabled,
            requireClientHealthy = _runtime.SafetyPolicy.RequireClientHealthy,
            requireGuardApproval = _runtime.SafetyPolicy.RequireGuardApproval
        },
        history = _runtime.Safety.History.Select(h => new
        {
            atUtc = h.AtUtc,
            principal = h.Principal.ToString(),
            @switch = h.Switch.ToString(),
            from = h.From,
            to = h.To,
            reason = h.Reason
        }).ToArray()
    };

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
        _correlationScope.Dispose();
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

    /// <summary>Reads the current switch state. Null when the host wired none.</summary>
    private readonly Func<object>? _safetyState;

    /// <summary>Applies a switch change and returns the decision, reason included.</summary>
    private readonly Func<SafetySwitch, bool, AuthorizationDecision>? _safetySetter;

    /// <summary>The test console, when one was wired. Null leaves /tests reporting why.</summary>
    private readonly TestConsoleService? _tests;
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

    public Gate1OperatorServer(
        int port,
        Func<Gate1CanonicalSnapshot> snapshot,
        Action<string> commandHandler,
        Func<object>? safetyState = null,
        Func<SafetySwitch, bool, AuthorizationDecision>? safetySetter = null,
        TestConsoleService? tests = null)
    {
        _safetyState = safetyState;
        _safetySetter = safetySetter;
        _tests = tests;
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

            // The operator's test page: every known test and what it observed.
            if (method == "GET" && path == "/tests")
            {
                return (200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(TestConsoleHtml.Render()));
            }

            if (method == "GET" && path == "/api/tests")
            {
                return _tests is null
                    ? Json(503, new { error = "test_console_unavailable" })
                    : Json(200, _tests.Snapshot());
            }

            if (method == "POST" && path == "/api/tests/run")
            {
                if (_tests is null)
                    return Json(503, new { started = false, reason = "test_console_unavailable" });

                using var body = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                var request = JsonSerializer.Deserialize<TestRunRequest>(
                    body.ReadToEnd(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                string target = request?.Target ?? "all";
                bool started = _tests.TryStart(target, out string reason);
                return Json(started ? 202 : 409, new { started, reason, target });
            }

            if (method == "GET" && path == "/api/safety")
            {
                return _safetyState is null
                    ? Json(503, new { error = "safety_state_unavailable" })
                    : Json(200, _safetyState());
            }

            // The operator arms and disarms here. The runtime decides, and a refusal
            // comes back with its reason rather than as a silent no-op.
            if (method == "POST" && path == "/api/safety")
            {
                if (_safetySetter is null)
                    return Json(503, new { error = "safety_switch_unavailable" });

                using var body = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                var request = JsonSerializer.Deserialize<SafetySwitchRequest>(
                    body.ReadToEnd(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (request is null || !Enum.TryParse(request.Switch, ignoreCase: true, out SafetySwitch which))
                    return Json(400, new { error = "unknown_switch", accepted = Enum.GetNames<SafetySwitch>() });

                var decision = _safetySetter(which, request.Enabled);
                return Json(decision.Allowed ? 200 : 403, new
                {
                    allowed = decision.Allowed,
                    reason = decision.Reason,
                    @switch = which.ToString(),
                    enabled = request.Enabled,
                    state = _safetyState?.Invoke()
                });
            }

            if (method == "POST" && path == "/api/command")
            {
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                _commandHandler(reader.ReadToEnd());
                // Report the state the command actually left behind. The fixed
                // "disabled_in_gate1" here was true only while execution could not
                // be turned on; as an unconditional claim it would now be wrong.
                return Json(202, new { status = "ACCEPTED", state = _safetyState?.Invoke() });
            }

            return (200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(Gate1DashboardHtml.Render()));
        }
        catch (Exception ex)
        {
            return Json(500, new { error = "operator_endpoint_failed", reason = $"{ex.GetType().Name}: {ex.Message}" });
        }
    }

    /// <summary>What a switch request carries. Deliberately tiny and explicit.</summary>
    private sealed record SafetySwitchRequest(string? Switch, bool Enabled);

    /// <summary>Which suite the operator asked to run.</summary>
    private sealed record TestRunRequest(string? Target);

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
    .switches { display:grid; grid-template-columns:repeat(auto-fit,minmax(260px,1fr)); gap:12px; margin-top:12px; }
    .sw { background:#0b1220; border:1px solid #334155; border-radius:8px; padding:12px; }
    .sw .name { font-weight:700; font-size:13px; }
    .sw .desc { color:#94a3b8; font-size:11px; margin:4px 0 8px; }
    .sw .state { font-size:12px; font-weight:700; letter-spacing:.5px; }
    .on { color:#f87171; } .off { color:#4ade80; }
    .sw button { background:#334155; padding:6px 12px; font-size:12px; font-weight:600; }
    .sw button.arm { background:#b45309; }
    .hist { font-size:11px; color:#94a3b8; margin-top:6px; }
    .deny { color:#fbbf24; font-size:12px; min-height:16px; }
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

  <div class="card" style="margin-top:20px">
    <h2>Controlli di esecuzione</h2>
    <p class="muted">Ogni interruttore e' deciso dal runtime, non da questa pagina: una richiesta rifiutata torna col suo motivo. Lo stato qui sotto e' quello reale, riletto ogni 2 secondi.</p>
    <p>Modalita': <span class="value" id="exec-mode">…</span></p>
    <div class="switches" id="switches"></div>
    <p class="deny" id="deny"></p>
    <p class="hist" id="history"></p>
  </div>

  <p><button onclick="stopAll()">EMERGENCY STOP — disarma tutto</button></p>
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
    // Each switch is described by what it actually permits, so the operator is
    // arming a known power rather than a label.
    const SWITCHES = [
      { key:'LiveInput',            field:'liveInput',            name:'Input diretto',
        desc:'Permette a tastiera e mouse sintetici di raggiungere il client.' },
      { key:'PacketInjection',      field:'packetInjection',      name:'Injection pacchetti',
        desc:'Permette di mettere pacchetti sul filo verso il server di gioco.' },
      { key:'RequireClientHealthy', field:'requireClientHealthy', name:'Richiedi client sano',
        desc:'Rifiuta le azioni quando il client non e\u0027 agganciato e reattivo.' },
      { key:'RequireGuardApproval', field:'requireGuardApproval', name:'Richiedi approvazione Guard',
        desc:'Rifiuta le azioni senza il telefono abbinato in sessione.' }
    ];

    async function setSwitch(key, enabled) {
      const res = await fetch('/api/safety', { method:'POST', body: JSON.stringify({ switch:key, enabled }) });
      const body = await res.json();
      // A refusal is shown with its reason: a control that silently did nothing
      // would leave the operator guessing whether it worked.
      document.getElementById('deny').textContent = body.allowed ? '' : ('Rifiutato: ' + body.reason);
      refreshSafety();
    }

    async function stopAll() {
      await fetch('/api/command', { method:'POST', body:'EMERGENCY_STOP' });
      refreshSafety();
    }

    async function refreshSafety() {
      try {
        const s = await (await fetch('/api/safety')).json();
        document.getElementById('exec-mode').textContent = s.executionMode;
        document.getElementById('switches').innerHTML = SWITCHES.map(function (sw) {
          const on = s.switches[sw.field] === true;
          return '<div class="sw"><div class="name">' + sw.name + '</div>' +
                 '<div class="desc">' + sw.desc + '</div>' +
                 '<div class="state ' + (on ? 'on' : 'off') + '">' + (on ? 'ATTIVO' : 'SPENTO') + '</div>' +
                 '<p><button class="' + (on ? '' : 'arm') + '" onclick="setSwitch(\'' + sw.key + '\',' + (!on) + ')">' +
                 (on ? 'Disattiva' : 'Attiva') + '</button></p></div>';
        }).join('');
        const h = s.history.slice(-5).reverse();
        document.getElementById('history').textContent = h.length
          ? 'Ultimi cambi: ' + h.map(function (x) {
              return x.switch + ' ' + x.from + '\u2192' + x.to + ' (' + x.reason + ')';
            }).join(' · ')
          : 'Nessun cambio registrato in questa sessione.';
      } catch (e) {
        document.getElementById('exec-mode').textContent = 'UNKNOWN';
        document.getElementById('switches').innerHTML = '<p class="warn">Stato di sicurezza non raggiungibile.</p>';
      }
    }

    refresh();
    refreshSafety();
    setInterval(refresh, 2000);
    setInterval(refreshSafety, 2000);
  </script>
</body>
</html>
""";
}
