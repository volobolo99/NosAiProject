// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// Autore: Volodymyr Ryzhuk
// Descrizione: Implementazione del Gate 5 (Provider Router Local-First, Escalation Policy,
// Hardware Baseline Autoscale, Storage Discovery NOSAI-SSD e Centro di Controllo Eye AI)
// Standard: C# 12 / .NET 8 — Zero-Allocation, Fail-Closed Security, Clean Code
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace NosAi.Runtime.Gate5
{
    // Imported source preserved as supplied. The attached source is truncated
    // inside ControlCenterDashboardServer.ProcessHttpRequest, therefore the
    // implementation is intentionally not completed or certified here.

    #region 1. Contratti di Dominio
    public enum ProviderType : byte
    {
        HeuristicRuleEngine = 0,
        LocalLlamaCpp = 1,
        CloudEscalation = 2
    }

    public enum ProviderRoutingPolicy : byte
    {
        StrictLocalOnly = 0,
        LocalWithCloudFallback = 1,
        ManualOverride = 2
    }

    public sealed record DecisionSuggestion(
        Guid SuggestionId,
        ProviderType SourceProvider,
        string ActionIntent,
        float ConfidenceScore,
        string ReasoningTrace,
        long LatencyMs,
        DateTime GeneratedAtUtc
    );

    public sealed record HardwareProfileSnapshot(
        string HardwareFingerprint,
        string DeviceModel,
        int CpuLogicalCores,
        double CpuLoadPercentage,
        double GpuTemperatureCelsius,
        double GpuLoadPercentage,
        long VramUsedMb,
        long VramTotalMb,
        long SystemRamUsedMb,
        long SystemRamTotalMb,
        bool IsThermalThrottlingActive
    );

    public sealed record StorageVolumeHealth(
        bool IsMounted,
        string VolumeLabel,
        string ResolvedDriveLetter,
        string RootDirectoryPath,
        long TotalCapacityBytes,
        long FreeSpaceBytes,
        bool IsWriteAccessible,
        int ReadWriteLatencyMs
    );
    #endregion

    #region 2. Provider Router
    public interface IDecisionProvider
    {
        ProviderType Type { get; }
        bool IsLoaded { get; }
        Task<bool> LoadModelAsync(CancellationToken cancellationToken = default);
        Task<DecisionSuggestion> GenerateDecisionAsync(string promptContext, CancellationToken cancellationToken = default);
        Task UnloadModelAsync();
    }

    public sealed class HeuristicRuleProvider : IDecisionProvider
    {
        public ProviderType Type => ProviderType.HeuristicRuleEngine;
        public bool IsLoaded => true;
        public Task<bool> LoadModelAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<DecisionSuggestion> GenerateDecisionAsync(string promptContext, CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            string intent = promptContext.Contains("HP_CRITICAL", StringComparison.Ordinal)
                ? "ACTION_REST_OR_POTION"
                : "ACTION_CONTINUE_TACTICAL";
            sw.Stop();
            return Task.FromResult(new DecisionSuggestion(Guid.NewGuid(), Type, intent, 0.96f,
                "Regola deterministica basata su soglie di sicurezza vitali.", sw.ElapsedMilliseconds, DateTime.UtcNow));
        }

        public Task UnloadModelAsync() => Task.CompletedTask;
    }

    public sealed class LocalLlamaProvider : IDecisionProvider
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
            if (!_isLoaded)
                await LoadModelAsync(cancellationToken).ConfigureAwait(false);
            var sw = Stopwatch.StartNew();
            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            sw.Stop();
            return new DecisionSuggestion(Guid.NewGuid(), Type, "ACTION_TACTICAL_ENGAGE", 0.91f,
                "Inferenza locale SLM: valutazione densità nemici e calcolo percorso ottimale.", sw.ElapsedMilliseconds, DateTime.UtcNow);
        }

        public Task UnloadModelAsync()
        {
            _isLoaded = false;
            return Task.CompletedTask;
        }
    }

    public sealed class CloudEscalationProvider : IDecisionProvider
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
                "Escalation cloud autorizzata per pianificazione complessa di lungo periodo.", sw.ElapsedMilliseconds, DateTime.UtcNow);
        }

        public Task UnloadModelAsync() => Task.CompletedTask;
    }

    public sealed class ProviderRouter
    {
        private readonly Dictionary<ProviderType, IDecisionProvider> _providers = new();
        private ProviderRoutingPolicy _policy;
        public ProviderRoutingPolicy Policy => _policy;

        public ProviderRouter(ProviderRoutingPolicy policy = ProviderRoutingPolicy.StrictLocalOnly)
        {
            _policy = policy;
            _providers[ProviderType.HeuristicRuleEngine] = new HeuristicRuleProvider();
            _providers[ProviderType.LocalLlamaCpp] = new LocalLlamaProvider();
            _providers[ProviderType.CloudEscalation] = new CloudEscalationProvider();
        }

        public void SetPolicy(ProviderRoutingPolicy newPolicy) => _policy = newPolicy;

        public async Task<DecisionSuggestion> RouteAndExecuteAsync(string promptContext, bool requiresComplexReasoning = false, CancellationToken token = default)
        {
            if (!requiresComplexReasoning)
                return await _providers[ProviderType.HeuristicRuleEngine].GenerateDecisionAsync(promptContext, token).ConfigureAwait(false);

            if (_policy == ProviderRoutingPolicy.StrictLocalOnly)
                return await _providers[ProviderType.LocalLlamaCpp].GenerateDecisionAsync(promptContext, token).ConfigureAwait(false);

            if (_policy == ProviderRoutingPolicy.LocalWithCloudFallback)
            {
                try
                {
                    return await _providers[ProviderType.LocalLlamaCpp].GenerateDecisionAsync(promptContext, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    return await _providers[ProviderType.CloudEscalation].GenerateDecisionAsync(promptContext, token).ConfigureAwait(false);
                }
            }

            return await _providers[ProviderType.HeuristicRuleEngine].GenerateDecisionAsync(promptContext, token).ConfigureAwait(false);
        }

        public async Task ReleaseAllVramAsync()
        {
            foreach (var provider in _providers.Values)
                await provider.UnloadModelAsync().ConfigureAwait(false);
        }
    }
    #endregion

    #region 3. Hardware Baseline
    public sealed class HardwareBaselineProfiler
    {
        private const double ThermalCoolingThresholdCelsius = 80.0;
        private readonly string _fingerprint;

        public HardwareBaselineProfiler() => _fingerprint = GenerateAnonymousFingerprint();

        private static string GenerateAnonymousFingerprint()
        {
            string raw = $"{Environment.MachineName}:{Environment.ProcessorCount}:{Environment.OSVersion.VersionString}";
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(hash)[..16];
        }

        public HardwareProfileSnapshot CaptureSnapshot(double simulatedGpuTemp = 69.5)
        {
            using var proc = Process.GetCurrentProcess();
            long ramUsedMb = proc.WorkingSet64 / (1024 * 1024);
            long ramTotalMb = 16L * 1024;
            bool isThrottling = simulatedGpuTemp >= ThermalCoolingThresholdCelsius;

            return new HardwareProfileSnapshot(_fingerprint,
                "Acer Nitro V 16 AI (AMD Ryzen 7 260 + RTX 5060 8GB GDDR7)",
                Environment.ProcessorCount, 18.5, simulatedGpuTemp, 34.0,
                1850, 8192, ramUsedMb, ramTotalMb, isThrottling);
        }
    }
    #endregion

    #region 4. Storage Discovery
    public sealed class ExternalStorageDiscoveryManager
    {
        private const string TargetVolumeLabel = "NOSAI-SSD";
        private readonly string _fallbackPath;

        public ExternalStorageDiscoveryManager(string fallbackPath = "data")
            => _fallbackPath = Path.GetFullPath(fallbackPath);

        public StorageVolumeHealth DiscoverAndValidate()
        {
            DriveInfo? targetDrive = null;
            try
            {
                targetDrive = DriveInfo.GetDrives().Where(d => d.IsReady)
                    .FirstOrDefault(d => string.Equals(d.VolumeLabel, TargetVolumeLabel, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                // Ambiente non supportato o permessi limitati: usare fallback controllato.
            }

            if (targetDrive != null)
            {
                string root = Path.Combine(targetDrive.RootDirectory.FullName, "NosAiData");
                return ValidateDirectoryAccess(targetDrive.VolumeLabel, targetDrive.Name, root, targetDrive.TotalSize, targetDrive.AvailableFreeSpace);
            }

            if (!Directory.Exists(_fallbackPath)) Directory.CreateDirectory(_fallbackPath);
            return ValidateDirectoryAccess("LOCAL_EMULATED", "C:", _fallbackPath,
                100L * 1024 * 1024 * 1024, 50L * 1024 * 1024 * 1024);
        }

        private static StorageVolumeHealth ValidateDirectoryAccess(string label, string driveLetter, string path, long total, long free)
        {
            var sw = Stopwatch.StartNew();
            bool writeOk = false;
            try
            {
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                string testFile = Path.Combine(path, $".probe_{Guid.NewGuid():N}.tmp");
                File.WriteAllText(testFile, "NOSAI_PROBE");
                File.Delete(testFile);
                writeOk = true;
            }
            catch
            {
                writeOk = false;
            }
            finally
            {
                sw.Stop();
            }

            return new StorageVolumeHealth(true, label, driveLetter, path, total, free, writeOk, (int)sw.ElapsedMilliseconds);
        }
    }
    #endregion

    #region 5. Eye AI View
    public sealed record EyeObservedLayer(ulong FrameIndex, DateTime TimestampUtc, int PlayerHp, int PlayerMp,
        int PositionX, int PositionY, int MapId, ImmutableArray<string> DetectedEntitiesRoi);

    public sealed record EyeEstimatedLayer(float OverallRiskScore, float GlobalConfidenceScore,
        string PredictedOutcomeSignature, int ExpectedHpDelta, int ExpectedMpDelta);

    public sealed record EyeDecisionLayer(string SelectedActionType, string TargetId, byte CurrentTrustTier,
        string DecisionProviderSource, bool IsSafetyAuthorized, string Rationale);

    public sealed record EyeAiUnifiedView(string SessionId, EyeObservedLayer Observed, EyeEstimatedLayer Estimated,
        EyeDecisionLayer Decision, HardwareProfileSnapshot Hardware, StorageVolumeHealth Storage);

    /// <summary>
    /// Skeleton importato fino al punto esatto in cui il sorgente allegato è stato troncato.
    /// Non viene introdotto un endpoint di comando inventato.
    /// </summary>
    public sealed class ControlCenterDashboardServer : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly Func<EyeAiUnifiedView> _viewProvider;
        private readonly Action<string> _commandSink;
        private CancellationTokenSource? _serverCts;

        public ControlCenterDashboardServer(int port, Func<EyeAiUnifiedView> viewProvider, Action<string> commandSink)
        {
            _viewProvider = viewProvider;
            _commandSink = commandSink;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        }

        public void Start()
        {
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
            // Source supplied by the user ends here; intentionally left incomplete.
            context.Response.StatusCode = 501;
            context.Response.Close();
        }

        public ValueTask DisposeAsync()
        {
            try { _serverCts?.Cancel(); } catch { }
            _listener.Close();
            _serverCts?.Dispose();
            return ValueTask.CompletedTask;
        }
    }
    #endregion
}
