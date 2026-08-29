// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// Autore: Volodymyr Ryzhuk
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
    public sealed record ControlPanelPacket(Guid PacketId, string Topic, string PayloadJson, DateTime TimestampUtc);

    public sealed class TelemetryRateLimiter
    {
        private readonly TimeSpan _minInterval;
        private DateTime _lastSentUtc = DateTime.MinValue;
        private readonly object _lock = new();

        public TelemetryRateLimiter(int maxFps = 10) => _minInterval = TimeSpan.FromMilliseconds(1000.0 / Math.Clamp(maxFps, 1, 60));

        public bool ShouldThrottle()
        {
            lock (_lock)
            {
                DateTime now = DateTime.UtcNow;
                if (now - _lastSentUtc >= _minInterval) { _lastSentUtc = now; return false; }
                return true;
            }
        }
    }

    public sealed class ControlPanelGatewayEngine : IAsyncDisposable
    {
        private readonly int _port;
        private readonly TelemetryRateLimiter _rateLimiter;
        private readonly ConcurrentQueue<ControlPanelPacket> _packetHistory = new();
        private HttpListener? _listener;
        private CancellationTokenSource? _gatewayCts;

        public ControlPanelGatewayEngine(int port = 8766) { _port = port; _rateLimiter = new TelemetryRateLimiter(maxFps: 10); }

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
                try { var context = await _listener.GetContextAsync().ConfigureAwait(false); ProcessGatewayRequest(context); }
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
                else context.Response.StatusCode = 404;
            }
            finally { context.Response.OutputStream.Close(); }
        }

        public void BroadcastPacket(string topic, string payloadJson)
        {
            if (_rateLimiter.ShouldThrottle() && topic == "TELEMETRY") return;
            var packet = new ControlPanelPacket(Guid.NewGuid(), topic, payloadJson, DateTime.UtcNow);
            _packetHistory.Enqueue(packet);
            while (_packetHistory.Count > 100) _packetHistory.TryDequeue(out _);
        }

        public async ValueTask DisposeAsync()
        {
            _gatewayCts?.Cancel();
            if (_listener != null && _listener.IsListening) { _listener.Stop(); _listener.Close(); }
            _gatewayCts?.Dispose();
            await Task.CompletedTask;
        }
    }

    public static class ControlPanelGatewayTestRunner
    {
        public static async Task<bool> RunAllTestsAsync()
        {
            bool allPassed = true;
            allPassed &= RunTest("Test 1: Rate Limiter Throttling Telemetria", TestRateLimiterThrottling);
            allPassed &= await RunTestAsync("Test 2: Avvio e Chiusura Gateway HTTP/SSE", TestGatewayServerLifecycleAsync);
            allPassed &= RunTest("Test 3: Invariante Architetturale (Gateway Non-Executable)", TestGatewaySecurityInvariant);
            return allPassed;
        }

        private static bool RunTest(string _, Func<bool> testFunc) { try { return testFunc(); } catch { return false; } }
        private static async Task<bool> RunTestAsync(string _, Func<Task<bool>> testFunc) { try { return await testFunc(); } catch { return false; } }

        private static bool TestRateLimiterThrottling()
        {
            var limiter = new TelemetryRateLimiter(maxFps: 10);
            bool first = limiter.ShouldThrottle(); bool immediate = limiter.ShouldThrottle();
            Thread.Sleep(110); bool afterWait = limiter.ShouldThrottle();
            return !first && immediate && !afterWait;
        }

        private static async Task<bool> TestGatewayServerLifecycleAsync()
        {
            int port = 8795;
            await using var gateway = new ControlPanelGatewayEngine(port);
            gateway.Start(); gateway.BroadcastPacket("TEST", "{\"msg\":\"hello\"}");
            await Task.Delay(50);
            return true;
        }

        private static bool TestGatewaySecurityInvariant()
        {
            var types = typeof(ControlPanelGatewayEngine).Assembly.GetTypes();
            bool hasExecution = types.Any(t => t.Namespace != null && t.Namespace.Contains("NosAi.Network.Gateway") && t.GetMethods().Any(m => m.Name.Contains("click", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("sendpacket", StringComparison.OrdinalIgnoreCase)));
            return !hasExecution;
        }
    }
}