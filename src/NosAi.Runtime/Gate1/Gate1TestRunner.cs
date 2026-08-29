using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using NosAi.Runtime.Orchestration;
using NosAi.Runtime.WorldModel;

namespace NosAi.Runtime.Gate1;

public static class Gate1TestRunner
{
    public static async Task<bool> RunAllAsync()
    {
        var results = new[]
        {
            TestCanonicalWireHeader(),
            TestSequenceGuard(),
            TestRsaChallengeIsSingleUse(),
            await TestSafetyCompositionIsReadOnlyAsync().ConfigureAwait(false)
        };

        var heartbeat = await TestHeartbeatFailClosedAsync().ConfigureAwait(false);
        return results.All(x => x) && heartbeat;
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

    private static async Task<bool> TestSafetyCompositionIsReadOnlyAsync()
    {
        var runtime = RuntimeComposition.CreateSafe();
        var world = new WorldModel();
        using var key = RSA.Create(2048);
        using var auth = new SessionAuth(key.ExportRSAPublicKeyPem());
        await using var channel = new GuardAiNetworkChannel(0, auth);
        var provider = new Gate1RuntimeSnapshotProvider(runtime, world, channel);
        var snapshot = provider.GetSnapshot().ToString() ?? string.Empty;
        return !runtime.SafetyPolicy.LiveInputEnabled &&
               !runtime.SafetyPolicy.PacketInjectionEnabled &&
               snapshot.Contains("execution", StringComparison.OrdinalIgnoreCase);
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
}
