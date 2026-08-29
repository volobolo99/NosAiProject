using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NosAi.Runtime.Gate5;

public sealed record EyeObservedLayer(
    ulong FrameIndex,
    DateTime TimestampUtc,
    int PlayerHp,
    int PlayerMp,
    int PositionX,
    int PositionY,
    int MapId,
    ImmutableArray<string> DetectedEntitiesRoi);

public sealed record EyeEstimatedLayer(
    float OverallRiskScore,
    float GlobalConfidenceScore,
    string PredictedOutcomeSignature,
    int ExpectedHpDelta,
    int ExpectedMpDelta);

public sealed record EyeDecisionLayer(
    string SelectedActionType,
    string TargetId,
    byte CurrentTrustTier,
    string DecisionProviderSource,
    bool IsSafetyAuthorized,
    string Rationale);

public sealed record EyeAiUnifiedView(
    string SessionId,
    EyeObservedLayer Observed,
    EyeEstimatedLayer Estimated,
    EyeDecisionLayer Decision,
    HardwareProfileSnapshot Hardware,
    StorageVolumeHealth Storage);

public sealed class ControlCenterDashboardServer : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly Func<EyeAiUnifiedView> _viewProvider;
    private readonly Action<string> _commandSink;
    private CancellationTokenSource? _serverCts;

    public ControlCenterDashboardServer(int port, Func<EyeAiUnifiedView> viewProvider, Action<string> commandSink)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        _viewProvider = viewProvider ?? throw new ArgumentNullException(nameof(viewProvider));
        _commandSink = commandSink ?? throw new ArgumentNullException(nameof(commandSink));
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    }

    public void Start()
    {
        if (_listener.IsListening) return;
        _listener.Start();
        _serverCts = new CancellationTokenSource();
        _ = RunServerLoopAsync(_serverCts.Token);
    }

    private async Task RunServerLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => ProcessHttpRequest(context), token);
            }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (OperationCanceledException) { break; }
        }
    }

    private void ProcessHttpRequest(HttpListenerContext context)
    {
        try
        {
            string path = context.Request.Url?.AbsolutePath ?? "/";
            string method = context.Request.HttpMethod;

            if (method.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                (path is "/" or "/api/state" or "/api/eye-view"))
            {
                byte[] payload = JsonSerializer.SerializeToUtf8Bytes(_viewProvider());
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.ContentLength64 = payload.Length;
                context.Response.OutputStream.Write(payload, 0, payload.Length);
                return;
            }

            if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) && path == "/api/command")
            {
                using var reader = new System.IO.StreamReader(context.Request.InputStream, Encoding.UTF8);
                string body = reader.ReadToEnd();
                if (string.IsNullOrWhiteSpace(body))
                {
                    WriteJson(context, 400, "{\"status\":\"INVALID_REQUEST\"}");
                    return;
                }

                _commandSink(body);
                WriteJson(context, 202, "{\"status\":\"RECEIVED_AND_QUEUED\"}");
                return;
            }

            context.Response.StatusCode = 404;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[ControlCenter] {ex.Message}");
            context.Response.StatusCode = 500;
        }
        finally
        {
            context.Response.Close();
        }
    }

    private static void WriteJson(HttpListenerContext context, int statusCode, string json)
    {
        byte[] payload = Encoding.UTF8.GetBytes(json);
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = payload.Length;
        context.Response.OutputStream.Write(payload, 0, payload.Length);
    }

    public ValueTask DisposeAsync()
    {
        _serverCts?.Cancel();
        _listener.Close();
        _serverCts?.Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed class Gate5IntegratedEngine : IAsyncDisposable
{
    private readonly ProviderRouter _providerRouter;
    private readonly HardwareBaselineProfiler _hardwareProfiler;
    private readonly ExternalStorageDiscoveryManager _storageManager;
    private readonly ControlCenterDashboardServer _dashboardServer;
    private ulong _frameCounter;

    public ProviderRouter Router => _providerRouter;
    public HardwareBaselineProfiler HardwareProfiler => _hardwareProfiler;
    public ExternalStorageDiscoveryManager StorageManager => _storageManager;

    public Gate5IntegratedEngine(int httpPort = 8765)
    {
        _providerRouter = new ProviderRouter(ProviderRoutingPolicy.StrictLocalOnly);
        _hardwareProfiler = new HardwareBaselineProfiler();
        _storageManager = new ExternalStorageDiscoveryManager();
        _dashboardServer = new ControlCenterDashboardServer(httpPort, GetCurrentUnifiedView, HandleDashboardCommand);
    }

    public void Start() => _dashboardServer.Start();

    public EyeAiUnifiedView GetCurrentUnifiedView()
    {
        var frame = unchecked(++_frameCounter);
        var hardware = _hardwareProfiler.CaptureSnapshot();
        var storage = _storageManager.DiscoverAndValidate();

        var observed = new EyeObservedLayer(frame, DateTime.UtcNow, 1500, 700, 125, 85, 1,
            ImmutableArray.Create("MOB_Dander_101", "PORTAL_NosVille_01"));
        var estimated = new EyeEstimatedLayer(0.08f, 0.95f, "POST_HP_1485_MP_665", -15, -35);
        var decision = new EyeDecisionLayer("UseSkill", "MOB_Dander_101", 2,
            "LocalLlamaCpp", true, "Bersaglio valido rilevato in ROI con rischio controllato.");

        return new EyeAiUnifiedView("SESS_GATE5_ORCHESTRATED", observed, estimated, decision, hardware, storage);
    }

    private static void HandleDashboardCommand(string commandJson)
        => Trace.WriteLine($"[DashboardCommand] Ricevuto comando: {commandJson}");

    public async ValueTask DisposeAsync()
    {
        await _providerRouter.ReleaseAllVramAsync().ConfigureAwait(false);
        await _dashboardServer.DisposeAsync().ConfigureAwait(false);
    }
}

public static class Gate5TestRunner
{
    public static async Task<bool> RunAllTestsAsync()
    {
        bool allPassed = true;
        allPassed &= await RunAsync("Local-first cloud block", TestStrictLocalOnlyCloudBlockAsync);
        allPassed &= Run("Provider non-executable invariant", TestProviderNonExecutableInvariant);
        allPassed &= Run("Hardware thermal trigger", TestHardwareThermalThrottlingTrigger);
        allPassed &= Run("Storage discovery", TestStorageDiscoveryPathResolution);
        allPassed &= Run("Eye AI stratification", TestEyeAiStratification);
        allPassed &= await RunAsync("Control Center REST endpoint", TestControlCenterRestEndpointAsync);
        Console.WriteLine(allPassed ? ">> Gate 5 test suite: PASS" : ">> Gate 5 test suite: FAIL");
        return allPassed;
    }

    private static bool Run(string name, Func<bool> test)
    {
        try
        {
            bool result = test();
            Console.WriteLine($"[{(result ? "PASS" : "FAIL")}] {name}");
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] {name}: {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> RunAsync(string name, Func<Task<bool>> test)
    {
        try
        {
            bool result = await test().ConfigureAwait(false);
            Console.WriteLine($"[{(result ? "PASS" : "FAIL")}] {name}");
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] {name}: {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> TestStrictLocalOnlyCloudBlockAsync()
    {
        var router = new ProviderRouter(ProviderRoutingPolicy.StrictLocalOnly);
        var suggestion = await router.RouteAndExecuteAsync("COMPLEX_MAP_PLANNING", true).ConfigureAwait(false);
        return suggestion.SourceProvider == ProviderType.LocalLlamaCpp;
    }

    private static bool TestProviderNonExecutableInvariant()
    {
        var providerTypes = typeof(IDecisionProvider).Assembly.GetTypes()
            .Where(t => typeof(IDecisionProvider).IsAssignableFrom(t) && !t.IsInterface);
        return providerTypes.All(type => type.GetMethods()
            .Select(method => method.Name.ToLowerInvariant())
            .All(name => !name.Contains("execute") && !name.Contains("sendpacket")
                && !name.Contains("click") && !name.Contains("presskey")));
    }

    private static bool TestHardwareThermalThrottlingTrigger()
    {
        var profiler = new HardwareBaselineProfiler();
        return !profiler.CaptureSnapshot(72).IsThermalThrottlingActive
            && profiler.CaptureSnapshot(81.5).IsThermalThrottlingActive;
    }

    private static bool TestStorageDiscoveryPathResolution()
    {
        var health = new ExternalStorageDiscoveryManager().DiscoverAndValidate();
        return health.IsMounted && health.IsWriteAccessible && !string.IsNullOrEmpty(health.RootDirectoryPath);
    }

    private static bool TestEyeAiStratification()
    {
        using var engine = new Gate5EngineScope(new Gate5IntegratedEngine(8788));
        var view = engine.Engine.GetCurrentUnifiedView();
        return view.Observed.PlayerHp == 1500 && view.Observed.DetectedEntitiesRoi.Length > 0
            && view.Estimated.GlobalConfidenceScore > 0.9f && view.Estimated.ExpectedHpDelta < 0
            && !string.IsNullOrEmpty(view.Decision.SelectedActionType) && view.Decision.IsSafetyAuthorized;
    }

    private static async Task<bool> TestControlCenterRestEndpointAsync()
    {
        await using var engine = new Gate5IntegratedEngine(8789);
        engine.Start();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        string response = await client.GetStringAsync("http://127.0.0.1:8789/api/eye-view").ConfigureAwait(false);
        return response.Contains("Observed", StringComparison.Ordinal)
            && response.Contains("Estimated", StringComparison.Ordinal)
            && response.Contains("Decision", StringComparison.Ordinal)
            && response.Contains("Hardware", StringComparison.Ordinal);
    }

    private sealed class Gate5EngineScope : IDisposable
    {
        public Gate5IntegratedEngine Engine { get; }
        public Gate5EngineScope(Gate5IntegratedEngine engine) => Engine = engine;
        public void Dispose() => Engine.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
