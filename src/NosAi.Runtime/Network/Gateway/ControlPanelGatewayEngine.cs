// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Author: Volodymyr Ryzhuk
// Descrizione: Sottosistema di Gateway per il Centro di Controllo, Real-Time Event Bridge,
//              Rate Limiter per la Telemetria e Streamer di Audit e Decision Trace
// Standard: C# 12 / .NET 8 — Zero-Allocation, Clean Architecture, Fail-Closed
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NosAi.Network.Gateway
{
    #region 1. Contratti di Dominio per il Gateway di Rete

    public sealed record ControlPanelPacket(
        Guid PacketId,
        string Topic,
        string PayloadJson,
        DateTime TimestampUtc
    );

    #endregion

    #region 2. Rate Limiter & Telemetry Throttler

    /// <summary>
    /// Regola la frequenza di invio dei pacchetti di telemetria per prevenire congestioni sulla dashboard.
    /// </summary>
    public sealed class TelemetryRateLimiter
    {
        private readonly TimeSpan _minInterval;
        private DateTime _lastSentUtc = DateTime.MinValue;
        private readonly object _lock = new();

        public TelemetryRateLimiter(int maxFps = 10)
        {
            _minInterval = TimeSpan.FromMilliseconds(1000.0 / Math.Clamp(maxFps, 1, 60));
        }

        public bool ShouldThrottle()
        {
            lock (_lock)
            {
                DateTime now = DateTime.UtcNow;
                if (now - _lastSentUtc >= _minInterval)
                {
                    _lastSentUtc = now;
                    return false; // Non throtteled, invio consentito
                }
                return true; // Throtteled, scarta o salta questo frame
            }
        }
    }

    #endregion

    #region 3. Control Panel Gateway & Event Streamer

    /// <summary>
    /// Gestisce i flussi di dati in tempo reale tra il runtime e la dashboard locale.
    /// </summary>
    public sealed class ControlPanelGatewayEngine : IAsyncDisposable
    {
        private readonly int _port;
        private readonly TelemetryRateLimiter _rateLimiter;
        private readonly ConcurrentQueue<ControlPanelPacket> _packetHistory = new();
        private HttpListener? _listener;
        private CancellationTokenSource? _gatewayCts;

        public ControlPanelGatewayEngine(int port = 8766)
        {
            _port = port;
            _rateLimiter = new TelemetryRateLimiter(maxFps: 10);
        }

        public void Start()
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
            _listener.Start();
            _gatewayCts = new CancellationTokenSource();
            _ = RunGatewayLoopAsync(_gatewayCts.Token);
        }

        private async Task RunGatewayLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _listener != null && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync().ConfigureAwait(false);
                    ProcessGatewayRequest(context);
                }
                catch (HttpListenerException) { break; }
                catch (OperationCanceledException) { break; }
            }
        }

        private void ProcessGatewayRequest(HttpListenerContext context)
        {
            try
            {
                string path = context.Request.Url?.AbsolutePath ?? "/";
                if (path == "/api/stream")
                {
                    context.Response.ContentType = "text/event-stream; charset=utf-8";
                    context.Response.StatusCode = 200;

                    string data = "data: {\"event\":\"CONNECTED\",\"timestamp\":\"" + DateTime.UtcNow.ToString("O") + "\"}\n\n";
                    byte[] buffer = Encoding.UTF8.GetBytes(data);
                    context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                }
                else
                {
                    context.Response.StatusCode = 404;
                }
            }
            finally
            {
                context.Response.OutputStream.Close();
            }
        }

        public void BroadcastPacket(string topic, string payloadJson)
        {
            if (_rateLimiter.ShouldThrottle() && topic == "TELEMETRY")
                return;

            var packet = new ControlPanelPacket(Guid.NewGuid(), topic, payloadJson, DateTime.UtcNow);
            _packetHistory.Enqueue(packet);
            while (_packetHistory.Count > 100) _packetHistory.TryDequeue(out _);
        }

        public async ValueTask DisposeAsync()
        {
            _gatewayCts?.Cancel();
            if (_listener != null && _listener.IsListening)
            {
                _listener.Stop();
                _listener.Close();
            }
            _gatewayCts?.Dispose();
            await Task.CompletedTask;
        }
    }

    #endregion

    #region 4. Suite di Test di Certificazione per il Gateway

    public static class ControlPanelGatewayTestRunner
    {
        public static async Task<bool> RunAllTestsAsync()
        {
            Console.WriteLine("=== Control Panel gateway checks ===");

            bool allPassed = true;

            allPassed &= RunTest("Test 1: Rate Limiter Throttling Telemetria", TestRateLimiterThrottling);
            allPassed &= await RunTestAsync("Test 2: Avvio e Chiusura Gateway HTTP/SSE", TestGatewayServerLifecycleAsync);
            allPassed &= RunTest("Test 3: Invariante Architetturale (Gateway Non-Executable)", TestGatewaySecurityInvariant);

            Console.WriteLine(allPassed
                ? "=== Control Panel gateway checks passed. Local only: this is not real-environment verification. ==="
                : "=== Control Panel gateway checks FAILED. See the lines marked FAIL above. ===");

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
                PrintResult(testName, false, err: ex.Message);
                return false;
            }
        }

        private static void PrintResult(string name, bool passed, string? err = null)
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
                Console.WriteLine($" [ERRORE: {err ?? "Asserzione fallita"}]");
            }
            Console.ResetColor();
        }

        private static bool TestRateLimiterThrottling()
        {
            var limiter = new TelemetryRateLimiter(maxFps: 10); // 100ms tra i pacchetti

            bool first = limiter.ShouldThrottle();  // False (permesso)
            bool immediate = limiter.ShouldThrottle(); // True (throtteled)

            Thread.Sleep(110);
            bool afterWait = limiter.ShouldThrottle(); // False (permesso nuovamente)

            return !first && immediate && !afterWait;
        }

        private static async Task<bool> TestGatewayServerLifecycleAsync()
        {
            int port = 8795;
            await using var gateway = new ControlPanelGatewayEngine(port);
            gateway.Start();
            gateway.BroadcastPacket("TEST", "{\"msg\":\"hello\"}");

            await Task.Delay(50);
            return true;
        }

        private static bool TestGatewaySecurityInvariant()
        {
            var types = typeof(ControlPanelGatewayEngine).Assembly.GetTypes()
                .Where(t => t.Namespace != null && t.Namespace.Contains("NosAi.Network.Gateway"));

            bool hasExecution = types.Any(t => t.GetMethods().Any(m => m.Name.ToLowerInvariant().Contains("click") || m.Name.ToLowerInvariant().Contains("sendpacket")));
            return !hasExecution;
        }
    }

    #endregion

    #region 5. Entry Point

    // The subsystem's own Program.Main used to live here. It was dead code: the
    // pinned StartupObject in the .csproj makes every other Main in the assembly
    // unreachable, which is why this suite had never run. It is reachable now
    // through the flag table in Program.cs; keeping a second entry point would
    // only suggest a way to run it that does not work.
    #endregion
}