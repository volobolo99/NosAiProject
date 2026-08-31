// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Gate 5 — Integrated engine (router, hardware, storage, Eye AI View, REST) and
//          its nominal certification suite
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Gate5;

/// <summary>
/// Composition root of the Gate 5 module. Without an attached observation source
/// the Eye AI layers report UNKNOWN with a reason — the engine never invents a
/// world, an estimate or a safety authorization it does not have (ADR-0002/0003).
/// </summary>
public sealed class Gate5IntegratedEngine : IAsyncDisposable
{
    private readonly ProviderRouter _providerRouter;
    private readonly HardwareBaselineProfiler _hardwareProfiler;
    private readonly ExternalStorageDiscoveryManager _storageManager;
    private readonly ControlCenterDashboardServer _dashboardServer;
    private readonly ConcurrentQueue<string> _receivedCommandRequests = new();
    private Func<(EyeObservedLayer Observed, EyeEstimatedLayer Estimated, EyeDecisionLayer Decision)>? _layerSource;
    private ulong _viewCounter;

    public ProviderRouter Router => _providerRouter;
    public HardwareBaselineProfiler HardwareProfiler => _hardwareProfiler;
    public ExternalStorageDiscoveryManager StorageManager => _storageManager;

    /// <summary>Reason the dashboard is degraded, or null when it is serving.</summary>
    public string? DashboardDegradedReason { get; private set; }

    public int ReceivedCommandRequestCount => _receivedCommandRequests.Count;

    // 8765 is the Python operator UI's port. Every embedded HTTP surface in the
    // repository gets its own default so two of them can run side by side.
    public const int DefaultHttpPort = 8768;

    public Gate5IntegratedEngine(int httpPort = DefaultHttpPort)
    {
        _providerRouter = new ProviderRouter(ProviderRoutingPolicy.StrictLocalOnly);
        _hardwareProfiler = new HardwareBaselineProfiler();
        _storageManager = new ExternalStorageDiscoveryManager();
        _dashboardServer = new ControlCenterDashboardServer(httpPort, GetCurrentUnifiedView, EnqueueCommandRequest);
    }

    /// <summary>
    /// Starts the loopback dashboard. A busy port degrades the dashboard and is
    /// reported by name; it never takes the engine down (Gate 1 house rule).
    /// </summary>
    public bool Start()
    {
        try
        {
            _dashboardServer.Start();
            DashboardDegradedReason = null;
            return true;
        }
        catch (HttpListenerException ex)
        {
            DashboardDegradedReason = $"dashboard_port_bind_failed:{ex.ErrorCode}";
            return false;
        }
    }

    /// <summary>
    /// Explicit adapter seam for the master host: attach real Observed/Estimated/
    /// Decision layer providers. Until then the layers stay honestly UNKNOWN.
    /// </summary>
    public void AttachLayerSource(Func<(EyeObservedLayer, EyeEstimatedLayer, EyeDecisionLayer)> layerSource) =>
        _layerSource = layerSource ?? throw new ArgumentNullException(nameof(layerSource));

    public EyeAiUnifiedView GetCurrentUnifiedView()
    {
        ulong frame = ++_viewCounter;
        EyeObservedLayer observed;
        EyeEstimatedLayer estimated;
        EyeDecisionLayer decision;
        if (_layerSource is { } source)
        {
            (observed, estimated, decision) = source();
        }
        else
        {
            observed = EyeObservedLayer.Unavailable(frame, "no_observation_source_attached");
            estimated = EyeEstimatedLayer.Unavailable("no_estimation_source_attached");
            decision = EyeDecisionLayer.Unavailable("no_decision_source_attached");
        }
        return new EyeAiUnifiedView("SESS_GATE5_LOCAL", observed, estimated, decision,
            _hardwareProfiler.CaptureSnapshot(), _storageManager.DiscoverAndValidate());
    }

    private void EnqueueCommandRequest(string commandJson)
    {
        _receivedCommandRequests.Enqueue(commandJson);
        while (_receivedCommandRequests.Count > 100) _receivedCommandRequests.TryDequeue(out _);
    }

    public async ValueTask DisposeAsync()
    {
        await _providerRouter.ReleaseAllVramAsync().ConfigureAwait(false);
        await _dashboardServer.DisposeAsync().ConfigureAwait(false);
    }
}

public static class Gate5TestRunner
{
    // Fixed loopback ports for the REST checks, chosen away from the defaults so
    // certification does not collide with a running engine (8768) or the UIs.
    private const int RestCheckPort = 18788;
    private const int BusyPortCheckPort = 18789;

    /// <summary>
    /// Runs every Gate 5 check and reports each one by name (same contract as the
    /// Gate 1 and Gate 2 runners: no short-circuit, a throwing check is a named
    /// failure, never a silent one).
    /// </summary>
    public static async Task<bool> RunAllTestsAsync()
    {
        Console.WriteLine("=== Gate 5 checks ===");

        bool allPassed = true;
        allPassed &= await RunAsync("Strict local-only routes complex reasoning to the local slot", TestStrictLocalOnlyRoutingAsync).ConfigureAwait(false);
        allPassed &= await RunAsync("Heuristic suggestions are DERIVED and deterministic", TestHeuristicSuggestionsAreDerivedAsync).ConfigureAwait(false);
        allPassed &= await RunAsync("Simulated inference is labeled SIMULATED", TestSimulatedInferenceIsLabeledAsync).ConfigureAwait(false);
        allPassed &= await RunAsync("Cloud escalation without authorization fails closed", TestCloudEscalationFailsClosedAsync).ConfigureAwait(false);
        allPassed &= await RunAsync("Authorized cloud escalation is still labeled SIMULATED", TestAuthorizedCloudEscalationIsLabeledAsync).ConfigureAwait(false);
        allPassed &= Run("Providers expose no execution surface", TestProviderNonExecutableInvariant);
        allPassed &= Run("Real hardware probe publishes UNKNOWN, not invented values", TestHardwareProbeIsHonest);
        allPassed &= Run("Simulated hardware snapshot is labeled SIMULATED", TestSimulatedHardwareIsLabeled);
        allPassed &= Run("Thermal verdict inherits provenance and fails closed on unknown", TestThermalVerdictProvenance);
        allPassed &= Run("Storage discovery reports the fallback honestly", TestStorageDiscoveryHonesty);
        allPassed &= Run("Eye layers without sources are UNKNOWN and never authorized", TestEyeLayersHonestWhenUnavailable);
        allPassed &= await RunAsync("Control Center serves the view and enforces the command allowlist", TestControlCenterRestContractAsync).ConfigureAwait(false);
        allPassed &= await RunAsync("Busy dashboard port degrades the dashboard, not the engine", TestBusyDashboardPortDegradesAsync).ConfigureAwait(false);

        Console.WriteLine(allPassed
            ? "=== Gate 5 checks passed. Local only: this is not real-environment verification. ==="
            : "=== Gate 5 checks FAILED. See the lines marked FAIL above. ===");
        return allPassed;
    }

    private static bool Run(string name, Func<bool> check)
    {
        try { return Report(name, check(), null); }
        catch (Exception ex) { return Report(name, false, $"{ex.GetType().Name}: {ex.Message}"); }
    }

    private static async Task<bool> RunAsync(string name, Func<Task<bool>> check)
    {
        try { return Report(name, await check().ConfigureAwait(false), null); }
        catch (Exception ex) { return Report(name, false, $"{ex.GetType().Name}: {ex.Message}"); }
    }

    private static bool Report(string name, bool passed, string? error)
    {
        var detail = error is null ? string.Empty : $" [{error}]";
        Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] {name}{detail}");
        return passed;
    }

    // ------------------------------------------------------------------ routing

    private static async Task<bool> TestStrictLocalOnlyRoutingAsync()
    {
        var router = new ProviderRouter(ProviderRoutingPolicy.StrictLocalOnly);
        var suggestion = await router.RouteAndExecuteAsync("COMPLEX_MAP_PLANNING", requiresComplexReasoning: true).ConfigureAwait(false);
        return suggestion.SourceProvider == ProviderType.LocalLlamaCpp;
    }

    private static async Task<bool> TestHeuristicSuggestionsAreDerivedAsync()
    {
        var router = new ProviderRouter(ProviderRoutingPolicy.StrictLocalOnly);
        var critical = await router.RouteAndExecuteAsync("HP_CRITICAL").ConfigureAwait(false);
        var normal = await router.RouteAndExecuteAsync("ROUTINE_TICK").ConfigureAwait(false);
        return critical.Source == DataSourceKind.Derived
            && critical.ActionIntent == "ACTION_REST_OR_POTION"
            && normal.ActionIntent == "ACTION_CONTINUE_TACTICAL";
    }

    private static async Task<bool> TestSimulatedInferenceIsLabeledAsync()
    {
        var router = new ProviderRouter(ProviderRoutingPolicy.StrictLocalOnly);
        var suggestion = await router.RouteAndExecuteAsync("COMPLEX_MAP_PLANNING", requiresComplexReasoning: true).ConfigureAwait(false);
        return suggestion.Source == DataSourceKind.Simulated
            && suggestion.ReasoningTrace.Contains("SIMULATO", StringComparison.Ordinal);
    }

    private sealed class AlwaysFailingProvider : IDecisionProvider
    {
        public ProviderType Type => ProviderType.LocalLlamaCpp;
        public bool IsLoaded => false;
        public Task<bool> LoadModelAsync(System.Threading.CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<DecisionSuggestion> GenerateDecisionAsync(string promptContext, System.Threading.CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("provider_intentionally_failing");
        public Task UnloadModelAsync() => Task.CompletedTask;
    }

    private static async Task<bool> TestCloudEscalationFailsClosedAsync()
    {
        var router = new ProviderRouter(ProviderRoutingPolicy.LocalWithCloudFallback,
            new Dictionary<ProviderType, IDecisionProvider> { [ProviderType.LocalLlamaCpp] = new AlwaysFailingProvider() });
        try
        {
            await router.RouteAndExecuteAsync("COMPLEX_MAP_PLANNING", requiresComplexReasoning: true).ConfigureAwait(false);
            return false;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message.Contains("fail-closed", StringComparison.Ordinal);
        }
    }

    private static async Task<bool> TestAuthorizedCloudEscalationIsLabeledAsync()
    {
        var router = new ProviderRouter(ProviderRoutingPolicy.LocalWithCloudFallback,
            new Dictionary<ProviderType, IDecisionProvider> { [ProviderType.LocalLlamaCpp] = new AlwaysFailingProvider() });
        router.AuthorizeCloudEscalation();
        var suggestion = await router.RouteAndExecuteAsync("COMPLEX_MAP_PLANNING", requiresComplexReasoning: true).ConfigureAwait(false);
        return suggestion.SourceProvider == ProviderType.CloudEscalation
            && suggestion.Source == DataSourceKind.Simulated;
    }

    private static bool TestProviderNonExecutableInvariant()
    {
        var providerTypes = typeof(IDecisionProvider).Assembly.GetTypes()
            .Where(t => typeof(IDecisionProvider).IsAssignableFrom(t) && !t.IsInterface);
        return providerTypes.All(type => type.GetMethods().Select(m => m.Name.ToLowerInvariant())
            .All(name => !name.Contains("execute") && !name.Contains("sendpacket") &&
                         !name.Contains("click") && !name.Contains("presskey")));
    }

    // ------------------------------------------------------------------ hardware

    private static bool TestHardwareProbeIsHonest()
    {
        var snapshot = new HardwareBaselineProfiler().CaptureSnapshot();
        // The build ships no GPU/system-load telemetry provider: those fields must
        // say UNKNOWN. What the process really observes must be LIVE.
        return snapshot.GpuTemperatureCelsius.Source == DataSourceKind.Unknown
            && snapshot.GpuLoadPercentage.Source == DataSourceKind.Unknown
            && snapshot.VramUsedMb.Source == DataSourceKind.Unknown
            && snapshot.CpuLoadPercentage.Source == DataSourceKind.Unknown
            && snapshot.IsThermalThrottlingActive.Source == DataSourceKind.Unknown
            && snapshot.CpuLogicalCores == Environment.ProcessorCount
            && snapshot.ProcessWorkingSetMb > 0;
    }

    private static bool TestSimulatedHardwareIsLabeled()
    {
        var snapshot = new HardwareBaselineProfiler().CaptureSimulatedSnapshot(69.5);
        return snapshot.DeviceModel.Source == DataSourceKind.Simulated
            && snapshot.GpuTemperatureCelsius.Source == DataSourceKind.Simulated
            && snapshot.VramTotalMb.Source == DataSourceKind.Simulated
            && snapshot.IsThermalThrottlingActive.Source == DataSourceKind.Simulated;
    }

    private static bool TestThermalVerdictProvenance()
    {
        var cool = HardwareBaselineProfiler.EvaluateThermalThrottle(ClassifiedValue<double>.Simulated(72.0));
        var hot = HardwareBaselineProfiler.EvaluateThermalThrottle(ClassifiedValue<double>.Simulated(81.5));
        var unknown = HardwareBaselineProfiler.EvaluateThermalThrottle(ClassifiedValue<double>.Unknown("no_probe"));
        return cool.HasValue && !cool.Value && cool.Source == DataSourceKind.Simulated
            && hot.HasValue && hot.Value && hot.Source == DataSourceKind.Simulated
            && !unknown.HasValue && unknown.Source == DataSourceKind.Unknown;
    }

    // ------------------------------------------------------------------ storage

    private static bool TestStorageDiscoveryHonesty()
    {
        var health = new ExternalStorageDiscoveryManager().DiscoverAndValidate();
        if (!health.IsWriteAccessible || string.IsNullOrEmpty(health.DataDirectoryPath)) return false;
        if (health.DedicatedVolumeFound)
        {
            // The dedicated volume really is mounted on this machine: capacities
            // must come from the drive, and the label must be the real one.
            return string.Equals(health.VolumeLabel, ExternalStorageDiscoveryManager.TargetVolumeLabel, StringComparison.OrdinalIgnoreCase)
                && !health.IsFallbackPath
                && health.TotalCapacityBytes.Source == DataSourceKind.Live;
        }
        // Absent volume: the fallback must say it is a fallback, with the real
        // capacities of the hosting drive — never invented sizes.
        return health.IsFallbackPath
            && health.VolumeLabel == ExternalStorageDiscoveryManager.FallbackLabel
            && health.TotalCapacityBytes.Source == DataSourceKind.Live
            && health.FreeSpaceBytes.Source == DataSourceKind.Live;
    }

    // ------------------------------------------------------------------ eye view

    private static bool TestEyeLayersHonestWhenUnavailable()
    {
        using var scope = new EngineScope(new Gate5IntegratedEngine(RestCheckPort));
        var view = scope.Engine.GetCurrentUnifiedView();
        return view.Observed.Source == DataSourceKind.Unknown
            && view.Observed.PlayerHp is null
            && view.Observed.UnavailableReason == "no_observation_source_attached"
            && view.Estimated.Source == DataSourceKind.Unknown
            && view.Estimated.GlobalConfidenceScore is null
            && view.Decision.Source == DataSourceKind.Unknown
            && !view.Decision.IsSafetyAuthorized;
    }

    // ------------------------------------------------------------------ REST

    private static async Task<bool> TestControlCenterRestContractAsync()
    {
        await using var engine = new Gate5IntegratedEngine(RestCheckPort);
        if (!engine.Start()) return false;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        string baseUrl = $"http://127.0.0.1:{RestCheckPort}";

        string view = await client.GetStringAsync($"{baseUrl}/api/eye-view").ConfigureAwait(false);
        bool viewHonest = view.Contains("UNKNOWN", StringComparison.Ordinal) &&
                          view.Contains("no_observation_source_attached", StringComparison.Ordinal);

        var allowed = await client.PostAsync($"{baseUrl}/api/command",
            new StringContent("{\"action\":\"pause\"}", System.Text.Encoding.UTF8, "application/json")).ConfigureAwait(false);
        var rejected = await client.PostAsync($"{baseUrl}/api/command",
            new StringContent("{\"action\":\"format_disk\"}", System.Text.Encoding.UTF8, "application/json")).ConfigureAwait(false);
        var invalid = await client.PostAsync($"{baseUrl}/api/command",
            new StringContent("not-json", System.Text.Encoding.UTF8, "application/json")).ConfigureAwait(false);

        return viewHonest
            && allowed.StatusCode == HttpStatusCode.Accepted
            && rejected.StatusCode == HttpStatusCode.BadRequest
            && invalid.StatusCode == HttpStatusCode.BadRequest
            && engine.ReceivedCommandRequestCount == 1;
    }

    private static async Task<bool> TestBusyDashboardPortDegradesAsync()
    {
        await using var first = new Gate5IntegratedEngine(BusyPortCheckPort);
        if (!first.Start()) return false;
        await using var second = new Gate5IntegratedEngine(BusyPortCheckPort);
        bool secondStarted = second.Start();
        // The port is taken: the second dashboard degrades with a named reason,
        // and the engine object itself remains usable (view still served).
        var view = second.GetCurrentUnifiedView();
        return !secondStarted
            && second.DashboardDegradedReason is not null
            && second.DashboardDegradedReason.StartsWith("dashboard_port_bind_failed:", StringComparison.Ordinal)
            && view.Hardware.CpuLogicalCores == Environment.ProcessorCount;
    }

    private sealed class EngineScope : IDisposable
    {
        public Gate5IntegratedEngine Engine { get; }
        public EngineScope(Gate5IntegratedEngine engine) => Engine = engine;
        public void Dispose() => Engine.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
