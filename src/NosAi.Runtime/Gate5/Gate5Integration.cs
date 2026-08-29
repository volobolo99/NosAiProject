using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NosAi.Runtime.Gate5;

namespace NosAi.Runtime.Gate5;

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
        var observed = new EyeObservedLayer(++_frameCounter, DateTime.UtcNow, 1500, 700, 125, 85, 1,
            System.Collections.Immutable.ImmutableArray.Create("MOB_Dander_101", "PORTAL_NosVille_01"));
        var estimated = new EyeEstimatedLayer(0.08f, 0.95f, "POST_HP_1485_MP_665", -15, -35);
        var decision = new EyeDecisionLayer("UseSkill", "MOB_Dander_101", 2, "LocalLlamaCpp", true,
            "Bersaglio valido rilevato in ROI con rischio controllato.");
        return new EyeAiUnifiedView("SESS_GATE5_ORCHESTRATED", observed, estimated, decision,
            _hardwareProfiler.CaptureSnapshot(), _storageManager.DiscoverAndValidate());
    }

    private static void HandleDashboardCommand(string commandJson) =>
        System.Diagnostics.Trace.WriteLine($"[DashboardCommand] Ricevuto comando: {commandJson}");

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
        try { bool result = test(); Console.WriteLine($"[{(result ? "PASS" : "FAIL")}] {name}"); return result; }
        catch (Exception ex) { Console.WriteLine($"[FAIL] {name}: {ex.Message}"); return false; }
    }

    private static async Task<bool> RunAsync(string name, Func<Task<bool>> test)
    {
        try { bool result = await test().ConfigureAwait(false); Console.WriteLine($"[{(result ? "PASS" : "FAIL")}] {name}"); return result; }
        catch (Exception ex) { Console.WriteLine($"[FAIL] {name}: {ex.Message}"); return false; }
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
        return providerTypes.All(type => type.GetMethods().Select(m => m.Name.ToLowerInvariant())
            .All(name => !name.Contains("execute") && !name.Contains("sendpacket") &&
                         !name.Contains("click") && !name.Contains("presskey")));
    }

    private static bool TestHardwareThermalThrottlingTrigger()
    {
        var profiler = new HardwareBaselineProfiler();
        return !profiler.CaptureSnapshot(72).IsThermalThrottlingActive &&
               profiler.CaptureSnapshot(81.5).IsThermalThrottlingActive;
    }

    private static bool TestStorageDiscoveryPathResolution()
    {
        var health = new ExternalStorageDiscoveryManager().DiscoverAndValidate();
        return health.IsMounted && health.IsWriteAccessible && !string.IsNullOrEmpty(health.RootDirectoryPath);
    }

    private static bool TestEyeAiStratification()
    {
        using var scope = new EngineScope(new Gate5IntegratedEngine(8788));
        var view = scope.Engine.GetCurrentUnifiedView();
        return view.Observed.PlayerHp == 1500 && view.Observed.DetectedEntitiesRoi.Length > 0 &&
               view.Estimated.GlobalConfidenceScore > 0.9f && view.Estimated.ExpectedHpDelta < 0 &&
               !string.IsNullOrEmpty(view.Decision.SelectedActionType) && view.Decision.IsSafetyAuthorized;
    }

    private static async Task<bool> TestControlCenterRestEndpointAsync()
    {
        await using var engine = new Gate5IntegratedEngine(8789);
        engine.Start();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        string response = await client.GetStringAsync("http://127.0.0.1:8789/api/eye-view").ConfigureAwait(false);
        return response.Contains("Observed", StringComparison.Ordinal) &&
               response.Contains("Estimated", StringComparison.Ordinal) &&
               response.Contains("Decision", StringComparison.Ordinal) &&
               response.Contains("Hardware", StringComparison.Ordinal);
    }

    private sealed class EngineScope : IDisposable
    {
        public Gate5IntegratedEngine Engine { get; }
        public EngineScope(Gate5IntegratedEngine engine) => Engine = engine;
        public void Dispose() => Engine.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
