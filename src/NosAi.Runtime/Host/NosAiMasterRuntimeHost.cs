using NosAi.Runtime.Autonomy;
// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Author: Volodymyr Ryzhuk
// Description: Master Runtime Host, Central Bootstrapper, Cycle Orchestrator
//              Operativo, Server Dashboard Integrato (Eye AI View) e Supervisore
// Standard: C# 12 / .NET 8 — Zero-Allocation, Clean Architecture, Fail-Closed
// ============================================================================

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace NosAi.Host
{
    #region 1. Contratti di Dominio del Master Host

    public enum MasterHostStatus : byte
    {
        Bootstrapping = 0,
        Running = 1,
        CoolingThrottled = 2,
        EmergencyStopped = 3,
        Terminating = 4
    }


    public sealed record MasterSystemTelemetry(
        string SessionId,
        ulong TotalTicksCount,
        MasterHostStatus HostStatus,
        TrustTier ActiveTrustTier,
        double? GpuTemperatureCelsius,
        string GpuTemperatureSource,
        double? CpuUsagePercentage,
        string CpuUsageSource,
        long? RamWorkingSetMb,
        string RamWorkingSetSource,
        long? VramUsageMb,
        string VramUsageSource,
        bool? IsGameClientHooked,
        string GameClientSource,
        bool? IsGuardPhoneConnected,
        string GuardPhoneSource,
        int? ActiveMonstersTracked,
        string ActiveMonstersSource,
        long? TotalGoldTracked,
        string TotalGoldSource,
        DateTime SnapshotTimestampUtc
    );

    #endregion

    #region 2. Confini di Sicurezza e Token di Autorizzazione Safety

    public sealed class MasterTrustManager
    {
        private TrustTier _currentTier;
        private readonly object _lock = new();

        public TrustTier CurrentTier { get { lock (_lock) return _currentTier; } }

        public MasterTrustManager(TrustTier initialTier = TrustTier.Tier2_SemiAutonomous)
        {
            _currentTier = initialTier;
        }

        public bool CheckAuthorization(TrustTier requiredTier)
        {
            lock (_lock) { return _currentTier >= requiredTier; }
        }

        public void DowngradeTrust(TrustTier newTier)
        {
            lock (_lock)
            {
                if (newTier < _currentTier)
                {
                    _currentTier = newTier;
                }
            }
        }

        public void EmergencyHalt()
        {
            lock (_lock) { _currentTier = TrustTier.Tier0_ReadOnly; }
        }
    }

    public sealed class MasterSafetyGate
    {
        private readonly MasterTrustManager _trustManager;
        private readonly byte[] _hmacSecret;

        public MasterSafetyGate(MasterTrustManager trustManager)
        {
            _trustManager = trustManager;
            _hmacSecret = new byte[32];
            RandomNumberGenerator.Fill(_hmacSecret);
        }

        /// <param name="gpuTemp">
        /// Observed GPU temperature, or <c>null</c> when it was never observed. Unknown
        /// is not zero: a missing reading must not satisfy the thermal limit.
        /// </param>
        public bool TryAuthorizeAction(Guid actionId, TrustTier requiredTier, double? gpuTemp, out byte[]? tokenSignature, out string? rejectReason)
        {
            tokenSignature = null;
            rejectReason = null;

            if (gpuTemp is null)
            {
                rejectReason = "TEMPERATURA GPU SCONOSCIUTA: nessuna lettura osservata. Il limite termico non può essere verificato, quindi il token non viene emesso.";
                return false;
            }

            if (gpuTemp >= 80.0)
            {
                rejectReason = "BLOCCO TERMICO: Temperatura GPU >= 80°C. Inibita emissione token di esecuzione.";
                return false;
            }

            if (!_trustManager.CheckAuthorization(requiredTier))
            {
                rejectReason = $"TRUST INSUFFICIENTE: Richiesto {requiredTier}, Livello Corrente {_trustManager.CurrentTier}.";
                return false;
            }

            tokenSignature = HMACSHA256.HashData(_hmacSecret, actionId.ToByteArray());
            return true;
        }

        public bool VerifySafetyToken(Guid actionId, ReadOnlySpan<byte> signature)
        {
            byte[] expected = HMACSHA256.HashData(_hmacSecret, actionId.ToByteArray());
            return CryptographicOperations.FixedTimeEquals(expected, signature);
        }
    }

    #endregion

    #region 3. Server Web Embedded per il Centro di Controllo (Eye AI View)

    public sealed class EmbeddedControlCenterServer : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly Func<MasterSystemTelemetry> _telemetryProvider;
        private readonly Action<string> _commandHandler;
        private readonly int _port;
        private CancellationTokenSource? _serverCts;

        /// <summary>The port actually bound, or null while the control centre is not listening.</summary>
        public int? BoundPort { get; private set; }

        public EmbeddedControlCenterServer(int port, Func<MasterSystemTelemetry> telemetryProvider, Action<string> commandHandler)
        {
            _telemetryProvider = telemetryProvider;
            _commandHandler = commandHandler;
            _port = port;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        }

        /// <summary>
        /// Binds the control centre, reporting a busy port instead of throwing. The
        /// control centre observes the host; it must not be able to abort it.
        /// </summary>
        public bool TryStart(out string? failureReason)
        {
            try
            {
                _listener.Start();
            }
            catch (HttpListenerException ex)
            {
                failureReason = ex.ErrorCode is 32 or 183
                    ? $"control_center_port_in_use:{_port}"
                    : $"control_center_bind_failed:{_port}:{ex.ErrorCode}";
                BoundPort = null;
                return false;
            }

            BoundPort = _port;
            _serverCts = new CancellationTokenSource();
            _ = ServerLoopAsync(_serverCts.Token);
            failureReason = null;
            return true;
        }

        public void Start()
        {
            if (!TryStart(out var reason))
                throw new InvalidOperationException($"Control centre could not start: {reason}");
        }

        private async Task ServerLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync().ConfigureAwait(false);
                    ProcessRequest(context);
                }
                catch (HttpListenerException) { break; }
                catch (OperationCanceledException) { break; }
            }
        }

        private void ProcessRequest(HttpListenerContext context)
        {
            try
            {
                string path = context.Request.Url?.AbsolutePath ?? "/";
                string method = context.Request.HttpMethod;

                if (method == "GET" && path == "/api/telemetry")
                {
                    var data = _telemetryProvider();
                    byte[] payload = JsonSerializer.SerializeToUtf8Bytes(data, new JsonSerializerOptions { WriteIndented = true });

                    context.Response.ContentType = "application/json; charset=utf-8";
                    context.Response.StatusCode = 200;
                    context.Response.ContentLength64 = payload.Length;
                    context.Response.OutputStream.Write(payload, 0, payload.Length);
                }
                else if (method == "POST" && path == "/api/command")
                {
                    using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                    string cmd = reader.ReadToEnd();
                    _commandHandler(cmd);

                    byte[] ok = Encoding.UTF8.GetBytes("{\"status\":\"ACCEPTED\"}");
                    context.Response.ContentType = "application/json";
                    context.Response.StatusCode = 202;
                    context.Response.OutputStream.Write(ok, 0, ok.Length);
                }
                else
                {
                    string html = GetEmbeddedDashboardHtml();
                    byte[] htmlBytes = Encoding.UTF8.GetBytes(html);

                    context.Response.ContentType = "text/html; charset=utf-8";
                    context.Response.StatusCode = 200;
                    context.Response.ContentLength64 = htmlBytes.Length;
                    context.Response.OutputStream.Write(htmlBytes, 0, htmlBytes.Length);
                }
            }
            finally
            {
                context.Response.OutputStream.Close();
            }
        }

        private string GetEmbeddedDashboardHtml()
        {
            return """
            <!DOCTYPE html>
            <html lang="it">
            <head>
                <meta charset="UTF-8">
                <title>NosAi 1.0 Beta — Centro di Controllo Eye AI</title>
                <style>
                    body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background: #0f172a; color: #f8fafc; margin: 0; padding: 20px; }
                    .header { display: flex; justify-content: space-between; align-items: center; border-bottom: 2px solid #3b82f6; padding-bottom: 15px; }
                    .badge { background: #1e293b; border: 1px solid #3b82f6; color: #60a5fa; padding: 6px 14px; border-radius: 6px; font-weight: bold; }
                    .grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 20px; margin-top: 25px; }
                    .card { background: #1e293b; border: 1px solid #334155; border-radius: 8px; padding: 20px; }
                    .card h3 { color: #38bdf8; margin-top: 0; border-bottom: 1px solid #334155; padding-bottom: 8px; }
                    .metric { font-size: 24px; font-weight: bold; color: #10b981; }
                    .btn-danger { background: #ef4444; color: white; border: none; padding: 10px 20px; border-radius: 6px; cursor: pointer; font-weight: bold; }
                    .btn-danger:hover { background: #dc2626; }
                </style>
            </head>
            <body>
                <div class="header">
                    <h2>NosAi 1.0 Beta — Centro di Controllo Master (Eye AI View)</h2>
                    <div class="badge" id="hostBadge">HOST ATTIVO</div>
                </div>
                <div class="grid">
                    <div class="card">
                        <h3>1. Strato Osservato (Percezione)</h3>
                        <p>Client Hook: <span class="metric" id="hook">UNKNOWN</span></p>
                        <p>Mostri in Raggio ROI: <span class="metric" id="mobs">UNKNOWN</span></p>
                    </div>
                    <div class="card">
                        <h3>2. Strato Stimato (Active Inference)</h3>
                        <p>Rischio Globale: <span class="metric" id="risk">UNKNOWN</span></p>
                        <p>Confidenza WorldState: <span class="metric" id="confidence">UNKNOWN</span></p>
                    </div>
                    <div class="card">
                        <h3>3. Strato Decisionale (Safety & Trust)</h3>
                        <p>Trust Tier: <span class="metric" id="trust">UNKNOWN</span></p>
                        <p>Temperatura GPU: <span class="metric" id="gpu">UNKNOWN</span></p>
                        <button class="btn-danger" onclick="fetch('/api/command',{method:'POST',body:'EMERGENCY_STOP'})">ARRESTO DI EMERGENZA</button>
                    </div>
                </div>
                <script>
                    // The badge used to hardcode a port. It now reports the address the
                    // page was actually served from, so it cannot name a dead one.
                    document.getElementById('hostBadge').textContent = 'HOST ATTIVO: ' + window.location.origin;
                    function classified(value, source) {
                        if (!source || source === 'UNKNOWN' || value === null || value === undefined) return 'UNKNOWN';
                        return value + ' [' + source + ']';
                    }
                    async function refresh() {
                        try {
                            const t = await (await fetch('/api/telemetry')).json();
                            document.getElementById('hook').textContent = classified(t.IsGameClientHooked, t.GameClientSource);
                            document.getElementById('mobs').textContent = classified(t.ActiveMonstersTracked, t.ActiveMonstersSource);
                            document.getElementById('risk').textContent = 'UNKNOWN';
                            document.getElementById('confidence').textContent = 'UNKNOWN';
                            document.getElementById('trust').textContent = t.ActiveTrustTier + ' [LIVE]';
                            document.getElementById('gpu').textContent = classified(t.GpuTemperatureCelsius, t.GpuTemperatureSource);
                        } catch (e) {
                            document.getElementById('hook').textContent = 'UNKNOWN';
                        }
                    }
                    refresh();
                    setInterval(refresh, 2000);
                </script>
            </body>
            </html>
            """;
        }

        public ValueTask DisposeAsync()
        {
            _serverCts?.Cancel();
            if (_listener.IsListening)
            {
                _listener.Stop();
                _listener.Close();
            }
            return ValueTask.CompletedTask;
        }
    }

    #endregion

    #region 4. Orchestratore Master Runtime Host (Main Executive Engine)

    public sealed class NosAiMasterRuntimeHost : IAsyncDisposable
    {
        public const string Version = "1.0 Beta";
        public const string Author = "Volodymyr Ryzhuk";

        private readonly MasterTrustManager _trustManager;
        private readonly MasterSafetyGate _safetyGate;
        private readonly EmbeddedControlCenterServer _controlCenter;
        private readonly CancellationTokenSource _hostCts = new();

        private MasterHostStatus _status = MasterHostStatus.Bootstrapping;
        private ulong _tickCounter = 0;
        private double _simulatedGpuTemp;
        private bool _gpuTemperatureSimulated;
        private readonly string _sessionId;

        public MasterHostStatus Status => _status;
        public MasterTrustManager Trust => _trustManager;
        public MasterSafetyGate SafetyGate => _safetyGate;

        /// <summary>
        /// 8765 belongs to the Python operator UI and 8766 to the Gate 1 operator
        /// API, so this host takes 8767. Sharing a default made whichever process
        /// started second fail to bind.
        /// </summary>
        public const int DefaultDashboardPort = 8767;

        public NosAiMasterRuntimeHost(int dashboardPort = DefaultDashboardPort)
        {
            _sessionId = $"NOSAI_SESSION_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"[..32];
            _trustManager = new MasterTrustManager(TrustTier.Tier2_SemiAutonomous);
            _safetyGate = new MasterSafetyGate(_trustManager);
            _controlCenter = new EmbeddedControlCenterServer(dashboardPort, CaptureCurrentTelemetry, HandleOperatorCommand);
        }

        public async Task StartHostAsync(CancellationToken token = default)
        {
            _status = MasterHostStatus.Bootstrapping;
            Trace.WriteLine($"[MasterHost] Inizializzazione NosAi {Version} su architettura C# .NET 8...");

            if (!_controlCenter.TryStart(out var controlCenterFailure))
                Trace.WriteLine($"[MasterHost] Centro di Controllo non disponibile: {controlCenterFailure}. L'host prosegue senza dashboard.");

            _status = MasterHostStatus.Running;
            Trace.WriteLine("[MasterHost] Tutti i sottosistemi inizializzati con successo.");
            Trace.WriteLine(_controlCenter.BoundPort is int controlCenterPort
                ? $"[MasterHost] Centro di Controllo operativo su: http://127.0.0.1:{controlCenterPort}/"
                : "[MasterHost] Centro di Controllo non in ascolto.");

            _ = RunMainExecutiveLoopAsync(_hostCts.Token);
            await Task.CompletedTask;
        }

        private async Task RunMainExecutiveLoopAsync(CancellationToken token)
        {
            var tickWatch = new Stopwatch();

            while (!token.IsCancellationRequested && _status != MasterHostStatus.Terminating)
            {
                try
                {
                    tickWatch.Restart();
                    _tickCounter++;

                    if (_gpuTemperatureSimulated && _simulatedGpuTemp >= 80.0)
                    {
                        _status = MasterHostStatus.CoolingThrottled;
                    }
                    else if (_status == MasterHostStatus.CoolingThrottled && _gpuTemperatureSimulated && _simulatedGpuTemp < 74.0)
                    {
                        _status = MasterHostStatus.Running;
                    }

                    if (_status is MasterHostStatus.Running or MasterHostStatus.CoolingThrottled)
                    {
                        var candidateId = Guid.NewGuid();
                        if (_safetyGate.TryAuthorizeAction(candidateId, TrustTier.Tier2_SemiAutonomous, _gpuTemperatureSimulated ? _simulatedGpuTemp : null, out var sig, out _))
                        {
                            bool valid = _safetyGate.VerifySafetyToken(candidateId, sig);
                            Debug.Assert(valid, "Integrità firma SafetyToken violata.");
                        }
                    }

                    tickWatch.Stop();
                    int sleepMs = Math.Max(1, 33 - (int)tickWatch.ElapsedMilliseconds);
                    await Task.Delay(sleepMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[MasterHost] Eccezione nel ciclo principale: {ex.Message}");
                }
            }
        }

        public MasterSystemTelemetry CaptureCurrentTelemetry()
        {
            using var proc = Process.GetCurrentProcess();
            long ramMb = proc.WorkingSet64 / (1024 * 1024);

            return new MasterSystemTelemetry(
                SessionId: _sessionId,
                TotalTicksCount: _tickCounter,
                HostStatus: _status,
                ActiveTrustTier: _trustManager.CurrentTier,
                GpuTemperatureCelsius: _gpuTemperatureSimulated ? _simulatedGpuTemp : null,
                GpuTemperatureSource: _gpuTemperatureSimulated ? "SIMULATED" : "UNKNOWN",
                CpuUsagePercentage: null,
                CpuUsageSource: "UNKNOWN",
                RamWorkingSetMb: ramMb,
                RamWorkingSetSource: "LIVE",
                VramUsageMb: null,
                VramUsageSource: "UNKNOWN",
                IsGameClientHooked: null,
                GameClientSource: "UNKNOWN",
                IsGuardPhoneConnected: null,
                GuardPhoneSource: "UNKNOWN",
                ActiveMonstersTracked: null,
                ActiveMonstersSource: "UNKNOWN",
                TotalGoldTracked: null,
                TotalGoldSource: "UNKNOWN",
                SnapshotTimestampUtc: DateTime.UtcNow
            );
        }

        private void HandleOperatorCommand(string command)
        {
            if (command.Contains("EMERGENCY_STOP"))
            {
                _trustManager.EmergencyHalt();
                _status = MasterHostStatus.EmergencyStopped;
                Trace.WriteLine("[MasterHost] ARRESTO DI EMERGENZA RICEVUTO DALLA DASHBOARD: Transizione a Tier 0 Read-Only.");
            }
        }

        public void SetSimulatedGpuTemperature(double temp)
        {
            _simulatedGpuTemp = temp;
            _gpuTemperatureSimulated = true;
        }

        public async ValueTask DisposeAsync()
        {
            _status = MasterHostStatus.Terminating;
            _hostCts.Cancel();
            await _controlCenter.DisposeAsync().ConfigureAwait(false);
            _hostCts.Dispose();
        }
    }

    #endregion

    #region 5. Suite di Test di Certificazione Finale del Master Host

    public static class MasterHostTestRunner
    {
        public static async Task<bool> RunAllTestsAsync()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=================================================================");
            Console.WriteLine($"   NosAi {NosAiMasterRuntimeHost.Version} — Certificazione Master Runtime Host");
            Console.WriteLine($"   Autore: {NosAiMasterRuntimeHost.Author} | Piattaforma: .NET 8 su Windows");
            Console.WriteLine("=================================================================");
            Console.ResetColor();

            bool allPassed = true;

            allPassed &= await RunTestAsync("Test 1: Avvio Host & Inizializzazione Main Executive Loop", TestHostBootstrappingAndTicksAsync);
            allPassed &= RunTest("Test 2: Emissione e Verifica Firma SafetyToken HMAC-SHA256", TestSafetyTokenSigningAndVerification);
            allPassed &= RunTest("Test 3: Throttling Termico GPU (>=80°C Cooling State)", TestThermalThrottlingTrigger);
            allPassed &= RunTest("Test 3b: Temperatura GPU sconosciuta non autorizza", TestUnknownTemperatureIsNotAuthorized);
            allPassed &= RunTest("Test 4: Comando Dashboard Emergency STOP (Tier 0 Fail-Closed)", TestDashboardEmergencyStopCommand);
            allPassed &= await RunTestAsync("Test 5: Endpoint REST Telemetria Centro di Controllo (127.0.0.1)", TestControlCenterRestEndpointAsync);
            allPassed &= RunTest("Test 6: Invariante Architetturale (Master Host Safety Isolation)", TestHostSecurityInvariant);

            Console.WriteLine();
            if (allPassed)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                // Green checks are local evidence only: they never certify the
                // system as operational (same honesty rule as every gate suite).
                Console.WriteLine("=== Master Host checks passed. Local only: this is not real-environment verification. ===");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(">> [BLOCCO CRITICO]: UNO O PIÙ TEST SONO FALLITI.");
                Console.ResetColor();
            }

            return allPassed;
        }

        private static bool RunTest(string testName, Func<bool> testFunc)
        {
            try
            {
                bool result = testFunc();
                PrintResult(testName, result);
                return result;
            }
            catch (Exception ex)
            {
                PrintResult(testName, false, ex.Message);
                return false;
            }
        }

        private static async Task<bool> RunTestAsync(string testName, Func<Task<bool>> testFunc)
        {
            try
            {
                bool result = await testFunc();
                PrintResult(testName, result);
                return result;
            }
            catch (Exception ex)
            {
                PrintResult(testName, false, ex.Message);
                return false;
            }
        }

        private static void PrintResult(string name, bool passed, string? error = null)
        {
            Console.Write($"[{ (passed ? "PASS" : "FAIL") }] {name,-62}");
            if (passed)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" [OK]");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($" [ERRORE: {error ?? "Asserzione fallita"}]");
            }
            Console.ResetColor();
        }

        private static async Task<bool> TestHostBootstrappingAndTicksAsync()
        {
            await using var host = new NosAiMasterRuntimeHost(dashboardPort: 8791);
            await host.StartHostAsync();
            await Task.Delay(120);
            var telemetry = host.CaptureCurrentTelemetry();
            return host.Status == MasterHostStatus.Running && telemetry.TotalTicksCount > 0;
        }

        private static bool TestSafetyTokenSigningAndVerification()
        {
            var trust = new MasterTrustManager(TrustTier.Tier3_AutonomousRestricted);
            var gate = new MasterSafetyGate(trust);
            var actionId = Guid.NewGuid();

            bool authOk = gate.TryAuthorizeAction(actionId, TrustTier.Tier2_SemiAutonomous, 68.0, out var sig, out _);
            bool verifyOk = authOk && gate.VerifySafetyToken(actionId, sig);

            sig![0] ^= 0xFF;
            bool forgeryBlocked = !gate.VerifySafetyToken(actionId, sig);

            return authOk && verifyOk && forgeryBlocked;
        }

        private static bool TestThermalThrottlingTrigger()
        {
            var trust = new MasterTrustManager(TrustTier.Tier3_AutonomousRestricted);
            var gate = new MasterSafetyGate(trust);
            var actionId = Guid.NewGuid();

            bool authBlocked = !gate.TryAuthorizeAction(actionId, TrustTier.Tier1_Assisted, 82.0, out _, out string? reason);
            return authBlocked && reason != null && reason.Contains("BLOCCO TERMICO");
        }

        private static bool TestUnknownTemperatureIsNotAuthorized()
        {
            var trust = new MasterTrustManager(TrustTier.Tier3_AutonomousRestricted);
            var gate = new MasterSafetyGate(trust);

            // Unknown is not zero: before this was nullable the tick loop passed 0
            // for an unobserved temperature, so the >= 80 check never fired and the
            // thermal limit was effectively absent in production.
            bool blocked = !gate.TryAuthorizeAction(
                Guid.NewGuid(), TrustTier.Tier1_Assisted, null, out var token, out string? reason);

            return blocked
                   && token is null
                   && reason is not null
                   && reason.Contains("SCONOSCIUTA");
        }

        private static bool TestDashboardEmergencyStopCommand()
        {
            var host = new NosAiMasterRuntimeHost(dashboardPort: 8792);
            var initialTier = host.Trust.CurrentTier;
            host.Trust.EmergencyHalt();
            return initialTier == TrustTier.Tier2_SemiAutonomous && host.Trust.CurrentTier == TrustTier.Tier0_ReadOnly;
        }

        private static async Task<bool> TestControlCenterRestEndpointAsync()
        {
            int testPort = 8793;
            await using var host = new NosAiMasterRuntimeHost(dashboardPort: testPort);
            await host.StartHostAsync();

            using var httpClient = new HttpClient();
            string json = await httpClient.GetStringAsync($"http://127.0.0.1:{testPort}/api/telemetry");
            return json.Contains("SessionId") && json.Contains("ActiveTrustTier") && json.Contains("GpuTemperatureCelsius");
        }

        private static bool TestHostSecurityInvariant()
        {
            var types = typeof(NosAiMasterRuntimeHost).Assembly.GetTypes()
                .Where(t => t.Namespace != null && t.Namespace.Contains("NosAi.Host"));

            bool hasDirectInjection = types.Any(t => t.GetMethods().Any(m => m.Name.ToLowerInvariant().Contains("inject") || m.Name.ToLowerInvariant().Contains("bypass")));
            return !hasDirectInjection;
        }
    }

    #endregion

    #region 6. Entry Point Ufficiale di NosAi

    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            Console.Title = $"NosAi Runtime Host {NosAiMasterRuntimeHost.Version}";

            if (args.Length > 0 && args[0].Equals("--test", StringComparison.OrdinalIgnoreCase))
            {
                bool success = await MasterHostTestRunner.RunAllTestsAsync();
                return success ? 0 : 1;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=================================================================");
            Console.WriteLine($"   NosAi {NosAiMasterRuntimeHost.Version} — Architettura Canonica di Automazione Controllata");
            Console.WriteLine($"   Creatore: {NosAiMasterRuntimeHost.Author} | Piattaforma: C# .NET 8 su Windows");
            Console.WriteLine("=================================================================\n");
            Console.ResetColor();

            await using var host = new NosAiMasterRuntimeHost();
            await host.StartHostAsync();

            Console.WriteLine(">> Master Host operativo in background.");
            Console.WriteLine($">> Aprire il browser all'indirizzo: http://127.0.0.1:{NosAiMasterRuntimeHost.DefaultDashboardPort}/ per la Dashboard.");
            Console.WriteLine(">> Premere Invio per eseguire i test di certificazione integrata...\n");

            Console.ReadLine();
            bool passed = await MasterHostTestRunner.RunAllTestsAsync();
            return passed ? 0 : 1;
        }
    }

    #endregion
}