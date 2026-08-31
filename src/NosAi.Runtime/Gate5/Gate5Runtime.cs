// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Gate 5 — Local-first Provider Router, Hardware Baseline, Storage Discovery,
//          3-layer Eye AI View and loopback REST Control Center
// ============================================================================
//
// Every value this module exposes declares its own provenance
// (LIVE/DERIVED/CACHED/SIMULATED/UNKNOWN). An unobserved value is UNKNOWN,
// never zero, never a plausible number invented to fill the gap (ADR-0002).

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Gate5;

public enum ProviderType : byte { HeuristicRuleEngine = 0, LocalLlamaCpp = 1, CloudEscalation = 2 }
public enum ProviderRoutingPolicy : byte { StrictLocalOnly = 0, LocalWithCloudFallback = 1, ManualOverride = 2 }

/// <summary>
/// One decision suggestion. <see cref="Source"/> declares whether it came from a
/// real deterministic rule (DERIVED) or from a simulated inference stub
/// (SIMULATED): a stub must never pass itself off as real inference.
/// </summary>
public sealed record DecisionSuggestion(Guid SuggestionId, ProviderType SourceProvider, string ActionIntent,
    float ConfidenceScore, string ReasoningTrace, long LatencyMs, DateTime GeneratedAtUtc, DataSourceKind Source);

public interface IDecisionProvider
{
    ProviderType Type { get; }
    bool IsLoaded { get; }
    Task<bool> LoadModelAsync(CancellationToken cancellationToken = default);
    Task<DecisionSuggestion> GenerateDecisionAsync(string promptContext, CancellationToken cancellationToken = default);
    Task UnloadModelAsync();
}

/// <summary>Deterministic threshold rules: real logic, output classified DERIVED.</summary>
public sealed class HeuristicRuleProvider : IDecisionProvider
{
    public ProviderType Type => ProviderType.HeuristicRuleEngine;
    public bool IsLoaded => true;
    public Task<bool> LoadModelAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<DecisionSuggestion> GenerateDecisionAsync(string promptContext, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        string intent = promptContext.Contains("HP_CRITICAL", StringComparison.Ordinal)
            ? "ACTION_REST_OR_POTION" : "ACTION_CONTINUE_TACTICAL";
        sw.Stop();
        return Task.FromResult(new DecisionSuggestion(Guid.NewGuid(), Type, intent, 0.96f,
            "Regola deterministica basata su soglie di sicurezza vitali.", sw.ElapsedMilliseconds,
            DateTime.UtcNow, DataSourceKind.Derived));
    }
    public Task UnloadModelAsync() => Task.CompletedTask;
}

/// <summary>
/// SIMULATED stand-in for the llama.cpp slot. No real model is loaded and no real
/// inference happens; every suggestion says so. The real integration is a separate,
/// explicit milestone — until then this stub keeps the routing surface testable
/// without labelling simulated output as live (ADR-0002).
/// </summary>
public sealed class SimulatedLocalInferenceProvider : IDecisionProvider
{
    public ProviderType Type => ProviderType.LocalLlamaCpp;
    private bool _isLoaded;
    public bool IsLoaded => _isLoaded;
    public async Task<bool> LoadModelAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(40, cancellationToken).ConfigureAwait(false);
        _isLoaded = true;
        return true;
    }
    public async Task<DecisionSuggestion> GenerateDecisionAsync(string promptContext, CancellationToken cancellationToken = default)
    {
        if (!_isLoaded) await LoadModelAsync(cancellationToken).ConfigureAwait(false);
        var sw = Stopwatch.StartNew();
        await Task.Delay(25, cancellationToken).ConfigureAwait(false);
        sw.Stop();
        return new DecisionSuggestion(Guid.NewGuid(), Type, "ACTION_TACTICAL_ENGAGE", 0.91f,
            "STUB SIMULATO: nessuna inferenza llama.cpp reale è stata eseguita.", sw.ElapsedMilliseconds,
            DateTime.UtcNow, DataSourceKind.Simulated);
    }
    public Task UnloadModelAsync() { _isLoaded = false; return Task.CompletedTask; }
}

/// <summary>SIMULATED stand-in for the cloud escalation slot; see the local stub.</summary>
public sealed class SimulatedCloudEscalationProvider : IDecisionProvider
{
    public ProviderType Type => ProviderType.CloudEscalation;
    public bool IsLoaded => true;
    public Task<bool> LoadModelAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    public async Task<DecisionSuggestion> GenerateDecisionAsync(string promptContext, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        await Task.Delay(120, cancellationToken).ConfigureAwait(false);
        sw.Stop();
        return new DecisionSuggestion(Guid.NewGuid(), Type, "ACTION_STRATEGIC_DEEP_PLAN", 0.98f,
            "STUB SIMULATO: nessuna chiamata cloud reale è stata eseguita.", sw.ElapsedMilliseconds,
            DateTime.UtcNow, DataSourceKind.Simulated);
    }
    public Task UnloadModelAsync() => Task.CompletedTask;
}

/// <summary>
/// Local-first decision routing. Cloud escalation is fail-closed: even under
/// <see cref="ProviderRoutingPolicy.LocalWithCloudFallback"/> the cloud slot is
/// reachable only after an explicit <see cref="AuthorizeCloudEscalation"/> — a
/// local failure must not silently widen the privacy boundary.
/// </summary>
public sealed class ProviderRouter
{
    private readonly Dictionary<ProviderType, IDecisionProvider> _providers = new();
    private ProviderRoutingPolicy _policy;

    public ProviderRoutingPolicy Policy => _policy;

    /// <summary>Explicit operator consent for the cloud slot. Defaults to false.</summary>
    public bool CloudEscalationAuthorized { get; private set; }

    public ProviderRouter(ProviderRoutingPolicy policy = ProviderRoutingPolicy.StrictLocalOnly,
        IReadOnlyDictionary<ProviderType, IDecisionProvider>? providerOverrides = null)
    {
        _policy = policy;
        _providers[ProviderType.HeuristicRuleEngine] = new HeuristicRuleProvider();
        _providers[ProviderType.LocalLlamaCpp] = new SimulatedLocalInferenceProvider();
        _providers[ProviderType.CloudEscalation] = new SimulatedCloudEscalationProvider();
        if (providerOverrides is not null)
        {
            // Explicit test/integration seam: certification needs to observe the
            // routing rules under provider failure without a real failing model.
            foreach (var (slot, provider) in providerOverrides) _providers[slot] = provider;
        }
    }

    public void SetPolicy(ProviderRoutingPolicy newPolicy) => _policy = newPolicy;
    public void AuthorizeCloudEscalation() => CloudEscalationAuthorized = true;
    public void RevokeCloudEscalation() => CloudEscalationAuthorized = false;

    public async Task<DecisionSuggestion> RouteAndExecuteAsync(string promptContext, bool requiresComplexReasoning = false, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(promptContext)) throw new ArgumentException("Il contesto decisionale è obbligatorio.", nameof(promptContext));
        if (!requiresComplexReasoning)
            return await _providers[ProviderType.HeuristicRuleEngine].GenerateDecisionAsync(promptContext, token).ConfigureAwait(false);
        if (_policy == ProviderRoutingPolicy.StrictLocalOnly)
            return await _providers[ProviderType.LocalLlamaCpp].GenerateDecisionAsync(promptContext, token).ConfigureAwait(false);
        if (_policy == ProviderRoutingPolicy.LocalWithCloudFallback)
        {
            try { return await _providers[ProviderType.LocalLlamaCpp].GenerateDecisionAsync(promptContext, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception localFailure)
            {
                if (!CloudEscalationAuthorized)
                    throw new InvalidOperationException(
                        "Provider locale in errore e escalation cloud NON autorizzata: fail-closed.", localFailure);
                return await _providers[ProviderType.CloudEscalation].GenerateDecisionAsync(promptContext, token).ConfigureAwait(false);
            }
        }
        return await _providers[ProviderType.HeuristicRuleEngine].GenerateDecisionAsync(promptContext, token).ConfigureAwait(false);
    }

    public async Task ReleaseAllVramAsync()
    {
        foreach (var provider in _providers.Values) await provider.UnloadModelAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Hardware baseline with per-field provenance. What this process can really
/// observe (cores, own working set) is LIVE; what would need a telemetry
/// provider this build does not have (GPU, system CPU load) is UNKNOWN.
/// </summary>
public sealed record HardwareProfileSnapshot(
    string HardwareFingerprint,
    ClassifiedValue<string> DeviceModel,
    int CpuLogicalCores,
    ClassifiedValue<double> CpuLoadPercentage,
    ClassifiedValue<double> GpuTemperatureCelsius,
    ClassifiedValue<double> GpuLoadPercentage,
    ClassifiedValue<long> VramUsedMb,
    ClassifiedValue<long> VramTotalMb,
    long ProcessWorkingSetMb,
    ClassifiedValue<long> SystemRamTotalMb,
    ClassifiedValue<bool> IsThermalThrottlingActive);

public sealed class HardwareBaselineProfiler
{
    public const double ThermalCoolingThresholdCelsius = 80.0;

    private readonly string _fingerprint;
    private ClassifiedValue<string>? _cachedDeviceModel;

    public HardwareBaselineProfiler() => _fingerprint = GenerateAnonymousFingerprint();

    private static string GenerateAnonymousFingerprint()
    {
        string raw = $"{Environment.MachineName}:{Environment.ProcessorCount}:{Environment.OSVersion.VersionString}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..16];
    }

    public string Fingerprint => _fingerprint;

    /// <summary>
    /// Thermal state derived from a classified temperature: the verdict inherits
    /// the temperature's provenance, and an unknown temperature yields an unknown
    /// verdict — never a reassuring false.
    /// </summary>
    public static ClassifiedValue<bool> EvaluateThermalThrottle(ClassifiedValue<double> gpuTemperature)
    {
        ArgumentNullException.ThrowIfNull(gpuTemperature);
        if (!gpuTemperature.HasValue)
            return ClassifiedValue<bool>.Unknown("gpu_temperature_unknown");
        bool throttling = gpuTemperature.Value >= ThermalCoolingThresholdCelsius;
        return new ClassifiedValue<bool>(throttling, gpuTemperature.Source, gpuTemperature.ObservedAtUtc, true);
    }

    /// <summary>Real observation only: no fabricated loads, temperatures or VRAM.</summary>
    public HardwareProfileSnapshot CaptureSnapshot()
    {
        using var process = Process.GetCurrentProcess();
        long workingSetMb = process.WorkingSet64 / (1024 * 1024);
        var gpuTemperature = ClassifiedValue<double>.Unknown("no_gpu_telemetry_provider");
        return new HardwareProfileSnapshot(
            _fingerprint,
            ProbeDeviceModel(),
            Environment.ProcessorCount,
            ClassifiedValue<double>.Unknown("cpu_load_not_probed"),
            gpuTemperature,
            ClassifiedValue<double>.Unknown("no_gpu_telemetry_provider"),
            ClassifiedValue<long>.Unknown("no_gpu_telemetry_provider"),
            ClassifiedValue<long>.Unknown("no_gpu_telemetry_provider"),
            workingSetMb,
            ProbeSystemRamTotalMb(),
            EvaluateThermalThrottle(gpuTemperature));
    }

    /// <summary>
    /// Fully SIMULATED snapshot for benchmarks and thermal-logic tests. Every
    /// field says SIMULATED, so it can never be mistaken for a real profile.
    /// </summary>
    public HardwareProfileSnapshot CaptureSimulatedSnapshot(double simulatedGpuTemperature)
    {
        var gpuTemperature = ClassifiedValue<double>.Simulated(simulatedGpuTemperature);
        return new HardwareProfileSnapshot(
            _fingerprint,
            ClassifiedValue<string>.Simulated("SIMULATED_BENCH_DEVICE"),
            Environment.ProcessorCount,
            ClassifiedValue<double>.Simulated(18.5),
            gpuTemperature,
            ClassifiedValue<double>.Simulated(34.0),
            ClassifiedValue<long>.Simulated(1850),
            ClassifiedValue<long>.Simulated(8192),
            0,
            ClassifiedValue<long>.Simulated(16L * 1024),
            EvaluateThermalThrottle(gpuTemperature));
    }

    private ClassifiedValue<string> ProbeDeviceModel()
    {
        if (_cachedDeviceModel is not null) return _cachedDeviceModel;
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem");
            foreach (var item in searcher.Get())
            {
                string manufacturer = Convert.ToString(item["Manufacturer"])?.Trim() ?? string.Empty;
                string model = Convert.ToString(item["Model"])?.Trim() ?? string.Empty;
                string combined = $"{manufacturer} {model}".Trim();
                if (combined.Length > 0)
                {
                    _cachedDeviceModel = ClassifiedValue<string>.Live(combined);
                    return _cachedDeviceModel;
                }
            }
            _cachedDeviceModel = ClassifiedValue<string>.Unknown("wmi_empty_computer_system");
        }
        catch (Exception ex)
        {
            _cachedDeviceModel = ClassifiedValue<string>.Unknown($"wmi_probe_failed:{ex.GetType().Name}");
        }
        return _cachedDeviceModel;
    }

    private static ClassifiedValue<long> ProbeSystemRamTotalMb()
    {
        try
        {
            long bytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            return bytes > 0
                ? ClassifiedValue<long>.Live(bytes / (1024 * 1024), warning: "gc_reported_available_memory")
                : ClassifiedValue<long>.Unknown("gc_memory_info_unavailable");
        }
        catch (Exception ex)
        {
            return ClassifiedValue<long>.Unknown($"gc_memory_probe_failed:{ex.GetType().Name}");
        }
    }
}

/// <summary>
/// Storage discovery outcome. When the dedicated NOSAI-SSD volume is absent the
/// fallback is reported as exactly that — a fallback with its real capacities —
/// never as a mounted dedicated volume with invented sizes.
/// </summary>
public sealed record StorageVolumeHealth(
    bool DedicatedVolumeFound,
    string VolumeLabel,
    string ResolvedRootPath,
    string DataDirectoryPath,
    ClassifiedValue<long> TotalCapacityBytes,
    ClassifiedValue<long> FreeSpaceBytes,
    bool IsWriteAccessible,
    int ReadWriteLatencyMs,
    bool IsFallbackPath,
    string? FailureReason);

public sealed class ExternalStorageDiscoveryManager
{
    public const string TargetVolumeLabel = "NOSAI-SSD";
    public const string FallbackLabel = "FALLBACK_LOCAL";

    private readonly string _fallbackPath;

    public ExternalStorageDiscoveryManager(string fallbackPath = "data") => _fallbackPath = Path.GetFullPath(fallbackPath);

    public StorageVolumeHealth DiscoverAndValidate()
    {
        DriveInfo? targetDrive = null;
        string? enumerationFailure = null;
        try
        {
            targetDrive = DriveInfo.GetDrives().Where(d => d.IsReady).FirstOrDefault(d =>
                string.Equals(d.VolumeLabel, TargetVolumeLabel, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            enumerationFailure = $"drive_enumeration_failed:{ex.GetType().Name}";
        }

        if (targetDrive is not null)
        {
            string dataDirectory = Path.Combine(targetDrive.RootDirectory.FullName, "NosAiData");
            var (writeOk, latencyMs, failure) = ProbeWriteAccess(dataDirectory);
            return new StorageVolumeHealth(true, targetDrive.VolumeLabel, targetDrive.RootDirectory.FullName,
                dataDirectory, ReadCapacity(() => targetDrive.TotalSize), ReadCapacity(() => targetDrive.AvailableFreeSpace),
                writeOk, latencyMs, IsFallbackPath: false, failure);
        }

        // Dedicated volume absent: report the fallback honestly with the real
        // capacities of the drive that hosts it.
        var (fallbackWriteOk, fallbackLatency, fallbackFailure) = ProbeWriteAccess(_fallbackPath);
        ClassifiedValue<long> total, free;
        try
        {
            string root = Path.GetPathRoot(_fallbackPath) ?? _fallbackPath;
            var fallbackDrive = new DriveInfo(root);
            total = ReadCapacity(() => fallbackDrive.TotalSize);
            free = ReadCapacity(() => fallbackDrive.AvailableFreeSpace);
        }
        catch (Exception ex)
        {
            total = ClassifiedValue<long>.Unknown($"fallback_drive_probe_failed:{ex.GetType().Name}");
            free = ClassifiedValue<long>.Unknown($"fallback_drive_probe_failed:{ex.GetType().Name}");
        }
        return new StorageVolumeHealth(false, FallbackLabel, Path.GetPathRoot(_fallbackPath) ?? _fallbackPath,
            _fallbackPath, total, free, fallbackWriteOk, fallbackLatency, IsFallbackPath: true,
            enumerationFailure ?? fallbackFailure);
    }

    private static ClassifiedValue<long> ReadCapacity(Func<long> reader)
    {
        try { return ClassifiedValue<long>.Live(reader()); }
        catch (Exception ex) { return ClassifiedValue<long>.Unknown($"capacity_read_failed:{ex.GetType().Name}"); }
    }

    private static (bool WriteOk, int LatencyMs, string? Failure) ProbeWriteAccess(string path)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            string probeFile = Path.Combine(path, $".probe_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probeFile, "NOSAI_PROBE");
            File.Delete(probeFile);
            sw.Stop();
            return (true, (int)sw.ElapsedMilliseconds, null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return (false, (int)sw.ElapsedMilliseconds, $"write_probe_failed:{ex.GetType().Name}");
        }
    }
}

// ---------------------------------------------------------------------------
// 3-layer Eye AI View. Each layer declares its own provenance; a layer with no
// real source is UNKNOWN with a reason, never a plausible mock.
// ---------------------------------------------------------------------------

public sealed record EyeObservedLayer(ulong FrameIndex, DateTime TimestampUtc, int? PlayerHp, int? PlayerMp,
    int? PositionX, int? PositionY, int? MapId, ImmutableArray<string> DetectedEntitiesRoi,
    DataSourceKind Source, string? UnavailableReason)
{
    public static EyeObservedLayer Unavailable(ulong frameIndex, string reason) => new(
        frameIndex, DateTime.UtcNow, null, null, null, null, null,
        ImmutableArray<string>.Empty, DataSourceKind.Unknown, reason);
}

public sealed record EyeEstimatedLayer(float? OverallRiskScore, float? GlobalConfidenceScore,
    string? PredictedOutcomeSignature, int? ExpectedHpDelta, int? ExpectedMpDelta,
    DataSourceKind Source, string? UnavailableReason)
{
    public static EyeEstimatedLayer Unavailable(string reason) => new(
        null, null, null, null, null, DataSourceKind.Unknown, reason);
}

public sealed record EyeDecisionLayer(string? SelectedActionType, string? TargetId, byte CurrentTrustTier,
    string? DecisionProviderSource, bool IsSafetyAuthorized, string? Rationale,
    DataSourceKind Source, string? UnavailableReason)
{
    /// <summary>An unavailable decision layer is never safety-authorized.</summary>
    public static EyeDecisionLayer Unavailable(string reason) => new(
        null, null, 0, null, IsSafetyAuthorized: false, null, DataSourceKind.Unknown, reason);
}

public sealed record EyeAiUnifiedView(string SessionId, EyeObservedLayer Observed, EyeEstimatedLayer Estimated,
    EyeDecisionLayer Decision, HardwareProfileSnapshot Hardware, StorageVolumeHealth Storage);

/// <summary>Writes <see cref="DataSourceKind"/> in its canonical wire form.</summary>
internal sealed class DataSourceKindWireJsonConverter : System.Text.Json.Serialization.JsonConverter<DataSourceKind>
{
    public override DataSourceKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString() switch
        {
            "LIVE" => DataSourceKind.Live,
            "DERIVED" => DataSourceKind.Derived,
            "CACHED" => DataSourceKind.Cached,
            "SIMULATED" => DataSourceKind.Simulated,
            _ => DataSourceKind.Unknown,
        };

    public override void Write(Utf8JsonWriter writer, DataSourceKind value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToWire());
}

/// <summary>
/// Loopback REST surface for the Control Center. It exposes state and accepts
/// only allow-listed command requests: the dashboard may ask, never execute —
/// the runtime remains the security authority (ADR-0003).
/// </summary>
public sealed class ControlCenterDashboardServer : IAsyncDisposable
{
    public static readonly IReadOnlySet<string> AllowedCommands = new HashSet<string>(StringComparer.Ordinal)
    { "pause", "resume", "stop", "recovery", "cooling", "checkpoint", "reobserve" };

    /// <summary>
    /// Provenance serializes in its wire form ("LIVE"/"UNKNOWN"/…), matching
    /// <see cref="DataSourceKindText.ToWire"/> so the C# and Python dashboards
    /// read one representation — a numeric enum would hide the classification.
    /// </summary>
    private static readonly JsonSerializerOptions WireJsonOptions = new()
    {
        Converters = { new DataSourceKindWireJsonConverter() },
    };

    private const int MaxCommandBodyBytes = 16 * 1024;

    private readonly HttpListener _listener;
    private readonly Func<EyeAiUnifiedView> _viewProvider;
    private readonly Action<string> _commandSink;
    private CancellationTokenSource? _serverCts;
    private Task? _serverLoop;

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
        _serverLoop = Task.Run(() => RunServerLoopAsync(_serverCts.Token));
    }

    private async Task RunServerLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener.IsListening)
        {
            try
            {
                HttpListenerContext context = await _listener.GetContextAsync().ConfigureAwait(false);
                ProcessHttpRequest(context);
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
                byte[] payload = JsonSerializer.SerializeToUtf8Bytes(_viewProvider(), WireJsonOptions);
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.ContentLength64 = payload.Length;
                context.Response.OutputStream.Write(payload, 0, payload.Length);
                return;
            }
            if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) && path == "/api/command")
            {
                if (context.Request.ContentLength64 > MaxCommandBodyBytes)
                {
                    WriteJson(context, 413, "{\"status\":\"PAYLOAD_TOO_LARGE\"}");
                    return;
                }
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                string body = reader.ReadToEnd();
                if (body.Length > MaxCommandBodyBytes)
                {
                    WriteJson(context, 413, "{\"status\":\"PAYLOAD_TOO_LARGE\"}");
                    return;
                }
                if (!TryReadAllowedCommand(body, out string? rejection))
                {
                    WriteJson(context, 400, $"{{\"status\":\"{rejection}\"}}");
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
        finally { context.Response.Close(); }
    }

    private static bool TryReadAllowedCommand(string body, out string? rejection)
    {
        if (string.IsNullOrWhiteSpace(body)) { rejection = "INVALID_REQUEST"; return false; }
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("action", out var action) ||
                action.ValueKind != JsonValueKind.String)
            {
                rejection = "INVALID_REQUEST";
                return false;
            }
            if (!AllowedCommands.Contains(action.GetString() ?? string.Empty))
            {
                rejection = "UNSUPPORTED_COMMAND";
                return false;
            }
            rejection = null;
            return true;
        }
        catch (JsonException)
        {
            rejection = "INVALID_JSON";
            return false;
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

    public async ValueTask DisposeAsync()
    {
        _serverCts?.Cancel();
        _listener.Close();
        if (_serverLoop is not null)
        {
            try { await _serverLoop.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        _serverCts?.Dispose();
    }
}
