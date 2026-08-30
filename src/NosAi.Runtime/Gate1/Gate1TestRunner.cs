using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NosAi.LiveIntegration;
using NosAi.Runtime.Configuration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Hardware;
using NosAi.Runtime.Orchestration;

namespace NosAi.Runtime.Gate1;

public static class Gate1TestRunner
{
    public static async Task<bool> RunAllAsync()
    {
        var sync = new[]
        {
            TestCanonicalWireHeader(),
            TestSequenceGuard(),
            TestRsaChallengeIsSingleUse(),
            TestMissingClientDoesNotInventGameplay(),
            TestFailedHardwareProbeIsUnknownNotZero(),
            TestConfigurationRejectsInvalidTimeout()
        };

        var asyncResults = new[]
        {
            await TestSafetyCompositionIsReadOnlyAsync().ConfigureAwait(false),
            await TestHeartbeatFailClosedAsync().ConfigureAwait(false),
            await TestReconnectAfterHeartbeatTimeoutAsync().ConfigureAwait(false),
            await TestAuthenticatedSessionReceivesClassifiedTelemetryAsync().ConfigureAwait(false),
            await TestBootstrapWithoutClientIsDegradedAsync().ConfigureAwait(false)
        };

        return sync.All(x => x) && asyncResults.All(x => x);
    }

    private static bool TestCanonicalWireHeader()
    {
        var header = new WireHeader(WireMessageType.SessionHello, 32, 7);
        Span<byte> bytes = stackalloc byte[WireHeader.HeaderSize];
        header.WriteTo(bytes);
        if (Encoding.ASCII.GetString(bytes[..4]) != "NOSA") return false;
        if (BinaryPrimitives.ReadUInt16BigEndian(bytes[6..8]) != 32) return false;
        return WireHeader.TryRead(bytes, out var decoded, out _) && decoded == header;
    }

    private static bool TestSequenceGuard()
    {
        var guard = new SequenceGuard();
        return guard.ValidateAndAdvance(1, out _) &&
               guard.ValidateAndAdvance(2, out _) &&
               !guard.ValidateAndAdvance(2, out _) &&
               !guard.ValidateAndAdvance(4, out _);
    }

    private static bool TestRsaChallengeIsSingleUse()
    {
        using var key = RSA.Create(2048);
        using var auth = new SessionAuth(key.ExportRSAPublicKeyPem());
        var challenge = auth.CreateChallenge();
        var signature = key.SignData(challenge, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return auth.VerifyAndConsume(signature) && !auth.VerifyAndConsume(signature);
    }

    private static bool TestMissingClientDoesNotInventGameplay()
    {
        var runtime = RuntimeComposition.CreateSafe();
        var world = new NosAi.Runtime.WorldModel.WorldModel();
        using var key = RSA.Create(2048);
        using var auth = new SessionAuth(key.ExportRSAPublicKeyPem());
        var channel = new GuardAiNetworkChannel(0, auth);
        var provider = new Gate1RuntimeSnapshotProvider(runtime, world, channel);
        var snapshot = provider.Capture();
        return snapshot.ContractVersion == Gate1SnapshotContract.Version
               && snapshot.Client.Attached.Value == false
               && snapshot.Client.Attached.Source == DataSourceKind.Live
               && snapshot.Client.GameplayBaseline.Source == DataSourceKind.Unknown
               && !snapshot.Client.GameplayBaseline.HasValue
               && snapshot.Safety.LiveInputEnabled.Value == false;
    }

    private static bool TestFailedHardwareProbeIsUnknownNotZero()
    {
        var telemetry = new LiveHardwareTelemetry(new ThrowingHardwareProbe());
        var snapshot = telemetry.Capture();
        return snapshot.View.SystemRamMb.Source == DataSourceKind.Unknown
               && !snapshot.View.SystemRamMb.HasValue
               && snapshot.View.Gpu.Source == DataSourceKind.Unknown
               && snapshot.View.LogicalCores.Source == DataSourceKind.Live
               && snapshot.View.LogicalCores.HasValue
               && snapshot.View.LogicalCores.Value == Environment.ProcessorCount
               && snapshot.FailureReason is not null;
    }

    private static bool TestConfigurationRejectsInvalidTimeout()
    {
        try
        {
            Gate1HostOptionsLoader.Load(new Dictionary<string, string?>(), new[] { "--timeout-ms", "1" });
            return false;
        }
        catch (InvalidOperationException)
        {
            var ok = Gate1HostOptionsLoader.Load(new Dictionary<string, string?>(), new[] { "--no-dashboard", "--guard-port", "0" });
            return !ok.StartDashboard && ok.GuardPort == 0;
        }
    }

    private static async Task<bool> TestSafetyCompositionIsReadOnlyAsync()
    {
        var runtime = RuntimeComposition.CreateSafe();
        var world = new NosAi.Runtime.WorldModel.WorldModel();
        using var key = RSA.Create(2048);
        using var auth = new SessionAuth(key.ExportRSAPublicKeyPem());
        await using var channel = new GuardAiNetworkChannel(0, auth);
        var provider = new Gate1RuntimeSnapshotProvider(runtime, world, channel);
        var json = JsonSerializer.Serialize(provider.GetSnapshot());
        return !runtime.SafetyPolicy.LiveInputEnabled &&
               !runtime.SafetyPolicy.PacketInjectionEnabled &&
               json.Contains("disabled_in_gate1", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> TestHeartbeatFailClosedAsync()
    {
        using var key = RSA.Create(2048);
        using var auth = new SessionAuth(key.ExportRSAPublicKeyPem());
        await using var server = new GuardAiNetworkChannel(0, auth);
        server.Start();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, server.LocalPort).ConfigureAwait(false);
        await Task.Delay(2300).ConfigureAwait(false);
        return !server.IsClientConnected;
    }

    private static async Task<bool> TestReconnectAfterHeartbeatTimeoutAsync()
    {
        using var key = RSA.Create(2048);
        using var auth = new SessionAuth(key.ExportRSAPublicKeyPem());
        await using var server = new GuardAiNetworkChannel(0, auth);
        server.Start();
        using (var first = new TcpClient())
        {
            await first.ConnectAsync(IPAddress.Loopback, server.LocalPort).ConfigureAwait(false);
            await Task.Delay(2300).ConfigureAwait(false);
            if (server.IsClientConnected)
                return false;
        }

        using var second = new TcpClient();
        await second.ConnectAsync(IPAddress.Loopback, server.LocalPort).ConfigureAwait(false);
        await Task.Delay(150).ConfigureAwait(false);
        if (!server.IsClientConnected)
            return false;

        uint seq = 1;
        await WriteFrameAsync(second.GetStream(), WireMessageType.SessionHello, Array.Empty<byte>(), seq++).ConfigureAwait(false);
        var (type, _) = await ReadFrameAsync(second.GetStream()).ConfigureAwait(false);
        return type == WireMessageType.Capabilities;
    }

    private static async Task<bool> TestAuthenticatedSessionReceivesClassifiedTelemetryAsync()
    {
        using var key = RSA.Create(2048);
        using var auth = new SessionAuth(key.ExportRSAPublicKeyPem());
        await using var server = new GuardAiNetworkChannel(0, auth);
        var runtime = RuntimeComposition.CreateSafe();
        var world = new NosAi.Runtime.WorldModel.WorldModel();
        var provider = new Gate1RuntimeSnapshotProvider(runtime, world, server);
        server.SetSnapshotSource(provider.Capture);
        server.Start();

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, server.LocalPort).ConfigureAwait(false);
        var stream = client.GetStream();
        uint seq = 1;
        await WriteFrameAsync(stream, WireMessageType.SessionHello, Array.Empty<byte>(), seq++).ConfigureAwait(false);
        var capabilities = await ReadFrameAsync(stream).ConfigureAwait(false);
        var challenge = await ReadFrameAsync(stream).ConfigureAwait(false);
        if (capabilities.Type != WireMessageType.Capabilities || challenge.Type != WireMessageType.AuthChallenge)
            return false;

        var signature = key.SignData(challenge.Payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        await WriteFrameAsync(stream, WireMessageType.AuthResponse, signature, seq++).ConfigureAwait(false);
        var result = await ReadFrameAsync(stream).ConfigureAwait(false);
        var telemetry = await ReadFrameAsync(stream).ConfigureAwait(false);
        if (result.Type != WireMessageType.AuthResult || result.Payload is not { Length: > 0 } || result.Payload[0] != 1)
            return false;
        if (telemetry.Type != WireMessageType.TelemetrySnapshot)
            return false;

        using var doc = JsonDocument.Parse(telemetry.Payload);
        var gameplay = doc.RootElement.GetProperty("client").GetProperty("gameplayBaseline");
        return gameplay.GetProperty("source").GetString() == "UNKNOWN"
               && gameplay.GetProperty("value").ValueKind == JsonValueKind.Null
               && server.IsAuthenticated;
    }

    private static async Task<bool> TestBootstrapWithoutClientIsDegradedAsync()
    {
        using var key = RSA.Create(2048);
        var options = new Gate1HostOptions
        {
            DashboardPort = 8765,
            GuardPort = 0,
            StartDashboard = false,
            TrustedGuardPublicKeyPem = key.ExportRSAPublicKeyPem()
        };
        await using var host = new Gate1BootstrapHost(options, probe: new ThrowingHardwareProbe());
        await host.StartAsync().ConfigureAwait(false);
        var snapshot = host.Capture();
        return host.Health == RuntimeHealthStatus.Degraded
               && snapshot.Client.GameplayBaseline.Source == DataSourceKind.Unknown
               && snapshot.Hardware.LogicalCores.Source == DataSourceKind.Live
               && !snapshot.Guard.Connected.Value
               && snapshot.Safety.ExecutionMode.Value == "disabled_in_gate1";
    }

    private static async Task WriteFrameAsync(NetworkStream stream, WireMessageType type, byte[] payload, uint sequence)
    {
        var packet = new byte[WireHeader.HeaderSize + payload.Length];
        new WireHeader(type, checked((ushort)payload.Length), sequence).WriteTo(packet);
        payload.CopyTo(packet.AsSpan(WireHeader.HeaderSize));
        await stream.WriteAsync(packet).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
    }

    private static async Task<(WireMessageType Type, byte[] Payload)> ReadFrameAsync(NetworkStream stream)
    {
        var headerBytes = new byte[WireHeader.HeaderSize];
        await stream.ReadExactlyAsync(headerBytes).ConfigureAwait(false);
        if (!WireHeader.TryRead(headerBytes, out var header, out var error))
            throw new InvalidDataException(error);
        var payload = new byte[header.PayloadLength];
        if (payload.Length > 0)
            await stream.ReadExactlyAsync(payload).ConfigureAwait(false);
        return (header.MessageType, payload);
    }

    private sealed class ThrowingHardwareProbe : IHardwareProbe
    {
        public HardwareFingerprint Detect() => throw new InvalidOperationException("probe_unavailable");
    }
}
