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
using NosAi.Runtime.Safety;

namespace NosAi.Runtime.Gate1;

public static class Gate1TestRunner
{
    /// <summary>
    /// Runs every Gate 1 check and reports each one by name.
    /// </summary>
    /// <remarks>
    /// Results are accumulated with <c>&amp;=</c> rather than short-circuited, so a
    /// failing check never hides the ones after it. A test that throws is reported
    /// as a failure carrying the exception message instead of tearing down the run:
    /// the whole point is to say which invariant broke, not merely that one did.
    /// </remarks>
    public static async Task<bool> RunAllAsync()
    {
        Console.WriteLine("=== Gate 1 checks ===");

        var allPassed = true;
        allPassed &= Run("Canonical NOSA wire header round-trips", TestCanonicalWireHeader);
        allPassed &= Run("Sequence guard rejects replayed frames", TestSequenceGuard);
        allPassed &= Run("RSA challenge is single use", TestRsaChallengeIsSingleUse);
        allPassed &= Run("Missing client does not invent gameplay", TestMissingClientDoesNotInventGameplay);
        allPassed &= Run("OS session baseline is LIVE when observed", TestOsSessionBaselineIsLiveWhenObserved);
        allPassed &= Run("UNKNOWN client fields are not published as values", TestUnknownClientFieldsAreNotPublishedAsValues);
        allPassed &= Run("Failed hardware probe is UNKNOWN, not zero", TestFailedHardwareProbeIsUnknownNotZero);
        allPassed &= Run("Recovered probe keeps its failure reason", TestRecoveredProbeKeepsItsFailureReason);
        allPassed &= Run("Absent hardware needs no sentinel string", TestAbsentHardwareLabelsNeedNoSentinelString);
        allPassed &= Run("SIMULATED is not a trusted production source", TestSimulatedIsNotATrustedProductionSource);
        allPassed &= Run("Configuration rejects an invalid timeout", TestConfigurationRejectsInvalidTimeout);

        allPassed &= await RunAsync("Safety composition stays read-only", TestSafetyCompositionIsReadOnlyAsync).ConfigureAwait(false);
        allPassed &= await RunAsync("Heartbeat timeout fails closed", TestHeartbeatFailClosedAsync).ConfigureAwait(false);
        allPassed &= await RunAsync("Reconnect accepted after heartbeat timeout", TestReconnectAfterHeartbeatTimeoutAsync).ConfigureAwait(false);
        allPassed &= await RunAsync("Authenticated session receives classified telemetry", TestAuthenticatedSessionReceivesClassifiedTelemetryAsync).ConfigureAwait(false);
        allPassed &= await RunAsync("Bootstrap without a client reports DEGRADED", TestBootstrapWithoutClientIsDegradedAsync).ConfigureAwait(false);
        allPassed &= await RunAsync("Busy dashboard port degrades the dashboard, not the runtime", TestBusyDashboardPortDoesNotKillTheRuntimeAsync).ConfigureAwait(false);
        allPassed &= await RunAsync("Ephemeral dashboard port binds and serves the snapshot", TestEphemeralDashboardPortServesSnapshotAsync).ConfigureAwait(false);
        allPassed &= await RunAsync("Busy Guard port fails closed with a named reason", TestBusyGuardPortFailsClosedWithAReasonAsync).ConfigureAwait(false);

        Console.WriteLine(allPassed
            ? "=== Gate 1 checks passed. Local only: this is not real-environment verification. ==="
            : "=== Gate 1 checks FAILED. See the lines marked FAIL above. ===");
        return allPassed;
    }

    private static bool Run(string name, Func<bool> check)
    {
        try
        {
            return Report(name, check(), null);
        }
        catch (Exception ex)
        {
            return Report(name, false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task<bool> RunAsync(string name, Func<Task<bool>> check)
    {
        try
        {
            return Report(name, await check().ConfigureAwait(false), null);
        }
        catch (Exception ex)
        {
            return Report(name, false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool Report(string name, bool passed, string? error)
    {
        var detail = error is null ? string.Empty : $" [{error}]";
        Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] {name}{detail}");
        return passed;
    }

    private static bool TestCanonicalWireHeader()
    {
        var header = new WireHeader(WireMessageType.SessionHello, 32, 7);
        Span<byte> bytes = stackalloc byte[WireHeader.HeaderSize];
        header.WriteTo(bytes);
        if (Encoding.ASCII.GetString(bytes[..4]) != "NOSA") return false;
        if (bytes[4] != WireHeader.CurrentVersion) return false;
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

        byte[] clientNonce = SessionTranscript.CreateNonce();
        using var exchange = EphemeralKeyExchange.Create();
        byte[] clientHello = Concat(clientNonce, exchange.PublicKey);
        if (!auth.TryBeginHandshake(clientHello, out byte[] serverHello))
            return false;

        byte[] serverNonce = serverHello[..SessionTranscript.NonceLength];
        byte[] serverEphemeral = serverHello[SessionTranscript.NonceLength..];

        byte[] signature = key.SignHash(
            SessionTranscript.Compute(HandshakeRole.Client, clientNonce, serverNonce, exchange.PublicKey, serverEphemeral),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        // Accepted once, then the transcript is consumed: replaying the same valid
        // signature must not open a second session, and must not hand out a second
        // copy of the session key material.
        if (!auth.VerifyAndConsume(signature, out byte[] first) || first.Length != EphemeralKeyExchange.SessionMaterialLength)
            return false;
        return !auth.VerifyAndConsume(signature, out byte[] second) && second.Length == 0;
    }

    private static byte[] Concat(byte[] first, byte[] second)
    {
        var joined = new byte[first.Length + second.Length];
        first.CopyTo(joined, 0);
        second.CopyTo(joined, first.Length);
        return joined;
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
               && snapshot.Client.ProcessName.Source == DataSourceKind.Unknown
               && !snapshot.Client.ProcessName.HasValue
               && snapshot.Client.WindowTitle.Source == DataSourceKind.Unknown
               && !snapshot.Client.WindowTitle.HasValue
               && snapshot.Client.GameplayBaseline.Source == DataSourceKind.Unknown
               && !snapshot.Client.GameplayBaseline.HasValue
               && snapshot.Safety.LiveInputEnabled.Value == false;
    }

    private static bool TestOsSessionBaselineIsLiveWhenObserved()
    {
        var observed = DateTime.UtcNow;
        var client = new ClientBaselineSnapshot(
            ProcessDetected: true,
            WindowDetected: true,
            ClientAttached: true,
            ProcessId: 4242,
            WindowHandle: (nint)0xABC,
            Source: "live_process_attach",
            ObservedAtUtc: observed,
            Availability: ClientBaselineAvailability.BaselineReady,
            Status: "attached_os_session",
            Warning: "Gameplay fields remain UNKNOWN: no gameplay provider is bound.",
            FailureReason: null,
            ProcessName: "NostaleClientX",
            WindowTitle: "NosTale",
            ProcessResponding: true,
            WindowVisible: true);
        var snapshot = SnapshotFromClient(client);
        return snapshot.Client.ProcessName.Source == DataSourceKind.Live
               && snapshot.Client.ProcessName.Value == "NostaleClientX"
               && snapshot.Client.ProcessId.Value == 4242
               && snapshot.Client.WindowTitle.Value == "NosTale"
               && snapshot.Client.WindowHandle.Value == "0xABC"
               && snapshot.Client.ProcessResponding.Value
               && snapshot.Client.WindowVisible.Value
               && snapshot.Client.Availability.Value == nameof(ClientBaselineAvailability.BaselineReady)
               && snapshot.Client.GameplayBaseline.Source == DataSourceKind.Unknown
               && !snapshot.Client.GameplayBaseline.HasValue;
    }

    private static bool TestUnknownClientFieldsAreNotPublishedAsValues()
    {
        var snapshot = SnapshotFromClient(new ClientBaselineSnapshot(
            ProcessDetected: false,
            WindowDetected: false,
            ClientAttached: false,
            ProcessId: null,
            WindowHandle: IntPtr.Zero,
            Source: "live_process_attach",
            ObservedAtUtc: DateTime.UtcNow,
            Availability: ClientBaselineAvailability.Unavailable,
            Status: "client_unavailable",
            Warning: null,
            FailureReason: "connector_not_bound"));
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(snapshot.ToWire()));
        return UnknownFieldsHaveNullValues(document.RootElement, "$");
    }

    private static Gate1CanonicalSnapshot SnapshotFromClient(ClientBaselineSnapshot client)
    {
        var hardware = new LiveHardwareTelemetry(new FallbackHardwareProbe()).Capture().View;
        return Gate1SnapshotFactory.Create(
            RuntimeHealthStatus.Degraded,
            "test",
            hardware,
            client,
            new Gate1ConnectionSnapshot(string.Empty, false, false, default, null),
            RuntimeSafetyPolicy.SafeDefault);
    }

    private static bool UnknownFieldsHaveNullValues(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("source", out var source) && element.TryGetProperty("value", out var value)
                && source.GetString() == "UNKNOWN"
                && value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            {
                return false;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (!UnknownFieldsHaveNullValues(property.Value, path + "." + property.Name))
                    return false;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                if (!UnknownFieldsHaveNullValues(item, $"{path}[{index++}]"))
                    return false;
            }
        }

        return true;
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

    private static bool TestRecoveredProbeKeepsItsFailureReason()
    {
        // SafeHardwareProbe recovers internally and never throws, so the reason
        // used to be lost and the snapshot looked like a probe that simply found
        // nothing rather than one that failed.
        var telemetry = new LiveHardwareTelemetry(new SafeHardwareProbe(new ThrowingHardwareProbe()));
        var snapshot = telemetry.Capture();

        return snapshot.FailureReason is not null
               && snapshot.FailureReason.Contains("hardware_probe_failed", StringComparison.Ordinal)
               && snapshot.View.Cpu.Source == DataSourceKind.Unknown
               && snapshot.View.Cpu.FailureReason == snapshot.FailureReason
               && snapshot.View.SystemRamMb.FailureReason == snapshot.FailureReason;
    }

    private static bool TestAbsentHardwareLabelsNeedNoSentinelString()
    {
        // The fallback reports absence as an empty value, so classification no
        // longer depends on matching the word "Unknown" inside a device name.
        var fingerprint = new FallbackHardwareProbe().Detect();
        if (fingerprint.Cpu.Length != 0 || fingerprint.Gpu.Length != 0)
            return false;

        var snapshot = new LiveHardwareTelemetry(new FallbackHardwareProbe()).Capture();
        return snapshot.View.Cpu.Source == DataSourceKind.Unknown
               && snapshot.View.Gpu.Source == DataSourceKind.Unknown
               && snapshot.View.LogicalCores.Source == DataSourceKind.Live;
    }

    private static bool TestSimulatedIsNotATrustedProductionSource()
    {
        var simulated = ClassifiedValue<double>.Simulated(68.5);
        var live = ClassifiedValue<double>.Live(68.5);

        return simulated.Source == DataSourceKind.Simulated
               && !simulated.Source.IsTrustedProductionSource()
               && live.Source.IsTrustedProductionSource()
               && simulated.HasValue;
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
        using var exchange = EphemeralKeyExchange.Create();
        var hello = Concat(SessionTranscript.CreateNonce(), exchange.PublicKey);
        await WriteFrameAsync(second.GetStream(), WireMessageType.SessionHello, hello, seq++).ConfigureAwait(false);
        var (type, _, _) = await ReadFrameAsync(second.GetStream()).ConfigureAwait(false);
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
        var clientNonce = SessionTranscript.CreateNonce();
        using var exchange = EphemeralKeyExchange.Create();
        await WriteFrameAsync(stream, WireMessageType.SessionHello, Concat(clientNonce, exchange.PublicKey), seq++).ConfigureAwait(false);
        var capabilities = await ReadFrameAsync(stream).ConfigureAwait(false);
        var challenge = await ReadFrameAsync(stream).ConfigureAwait(false);
        var proof = await ReadFrameAsync(stream).ConfigureAwait(false);
        if (capabilities.Type != WireMessageType.Capabilities
            || challenge.Type != WireMessageType.AuthChallenge
            || proof.Type != WireMessageType.ServerAuthProof
            || challenge.Payload.Length != SessionAuth.HandshakeHelloLength)
            return false;

        var serverNonce = challenge.Payload[..SessionTranscript.NonceLength];
        var serverEphemeral = challenge.Payload[SessionTranscript.NonceLength..];

        using var runtimeKey = RSA.Create();
        runtimeKey.ImportFromPem(auth.RuntimePublicKeyPem);
        if (!SessionTranscript.Verify(runtimeKey, HandshakeRole.Server, clientNonce, serverNonce, exchange.PublicKey, serverEphemeral, proof.Payload))
            return false;

        var binding = SessionTranscript.ComputeBinding(clientNonce, serverNonce, exchange.PublicKey, serverEphemeral);
        using var cipher = SessionCipher.ForPhone(exchange.DeriveSessionMaterial(serverEphemeral, binding));

        var signature = SessionTranscript.Sign(key, HandshakeRole.Client, clientNonce, serverNonce, exchange.PublicKey, serverEphemeral);
        await WriteFrameAsync(stream, WireMessageType.AuthResponse, signature, seq++).ConfigureAwait(false);
        var result = await ReadFrameAsync(stream).ConfigureAwait(false);
        var telemetry = await ReadFrameAsync(stream).ConfigureAwait(false);
        if (result.Type != WireMessageType.AuthResult || result.Payload is not { Length: > 0 } || result.Payload[0] != 1)
            return false;
        if (telemetry.Type != WireMessageType.TelemetrySnapshot)
            return false;

        // The snapshot arrives sealed (ADR-0009). Reading it here is what proves the
        // runtime encrypted it rather than merely claiming to.
        if (!cipher.TryOpenFrame(telemetry.Header, telemetry.Payload, out byte[] snapshotJson, out _))
            return false;

        using var doc = JsonDocument.Parse(snapshotJson);
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
            DashboardPort = 0,
            GuardPort = 0,
            StartDashboard = false,
            TrustedGuardPublicKeyPem = key.ExportRSAPublicKeyPem(),
            // A name no process can have. Without it this check asserted on
            // ambient machine state and passed only while client detection was
            // broken: with NosTale actually running the host reports Healthy.
            ClientProcessName = "nosai-absent-client-4f1c9a2e"
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

    /// <summary>
    /// Reads one frame, keeping the raw header: it is the AEAD associated data for
    /// an encrypted frame, so a caller that discarded it could not open one.
    /// </summary>
    private static async Task<(WireMessageType Type, byte[] Payload, byte[] Header)> ReadFrameAsync(NetworkStream stream)
    {
        var headerBytes = new byte[WireHeader.HeaderSize];
        await stream.ReadExactlyAsync(headerBytes).ConfigureAwait(false);
        if (!WireHeader.TryRead(headerBytes, out var header, out var error))
            throw new InvalidDataException(error);
        var payload = new byte[header.PayloadLength];
        if (payload.Length > 0)
            await stream.ReadExactlyAsync(payload).ConfigureAwait(false);
        return (header.MessageType, payload, headerBytes);
    }

    /// <summary>
    /// Regression: a dashboard port already held by another process (the Python
    /// operator UI, historically on the same default port) threw out of
    /// <c>StartAsync</c> and killed the runtime, taking the Guard channel and the
    /// attached client with it. The dashboard is observability, not a safety gate.
    /// </summary>
    private static async Task<bool> TestBusyDashboardPortDoesNotKillTheRuntimeAsync()
    {
        using var key = RSA.Create(2048);
        var squatter = new TcpListener(IPAddress.Loopback, 0);
        squatter.Start();
        var busyPort = ((IPEndPoint)squatter.LocalEndpoint).Port;
        try
        {
            var options = new Gate1HostOptions
            {
                DashboardPort = busyPort,
                GuardPort = 0,
                StartDashboard = true,
                TrustedGuardPublicKeyPem = key.ExportRSAPublicKeyPem(),
                ClientProcessName = "nosai-absent-client-4f1c9a2e"
            };
            await using var host = new Gate1BootstrapHost(options, probe: new ThrowingHardwareProbe());
            await host.StartAsync().ConfigureAwait(false);

            return host.Health == RuntimeHealthStatus.Degraded
                   && host.DashboardPort is null
                   && host.DashboardFailureReason is not null
                   && host.DashboardFailureReason.StartsWith("dashboard_port_", StringComparison.Ordinal)
                   && host.GuardPort > 0;
        }
        finally
        {
            squatter.Stop();
        }
    }

    /// <summary>
    /// Port 0 means "any free loopback port", not "silently use 8765". The reported
    /// port must be the one actually bound, and it must serve the classified snapshot.
    /// </summary>
    private static async Task<bool> TestEphemeralDashboardPortServesSnapshotAsync()
    {
        using var key = RSA.Create(2048);
        var options = new Gate1HostOptions
        {
            DashboardPort = 0,
            GuardPort = 0,
            StartDashboard = true,
            TrustedGuardPublicKeyPem = key.ExportRSAPublicKeyPem(),
            ClientProcessName = "nosai-absent-client-4f1c9a2e"
        };
        await using var host = new Gate1BootstrapHost(options, probe: new ThrowingHardwareProbe());
        await host.StartAsync().ConfigureAwait(false);

        if (host.DashboardPort is not int port || port == Gate1HostOptions.DefaultDashboardPort || host.DashboardFailureReason is not null)
            return false;

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var body = await http.GetStringAsync($"http://127.0.0.1:{port}/api/gate1").ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("contractVersion").GetString() == Gate1SnapshotContract.Version
               && document.RootElement.GetProperty("client").GetProperty("attached").GetProperty("value").GetBoolean() == false;
    }

    /// <summary>
    /// Regression: a Guard port already held by another runtime instance surfaced as
    /// "SocketException (10048)" plus a stack trace naming neither the port nor the
    /// remedy. Unlike the dashboard the channel must still fail closed — it is the
    /// authenticated PC-phone link — but the reason has to be actionable.
    /// </summary>
    private static async Task<bool> TestBusyGuardPortFailsClosedWithAReasonAsync()
    {
        using var key = RSA.Create(2048);
        var squatter = new TcpListener(IPAddress.Loopback, 0);
        squatter.Start();
        var busyPort = ((IPEndPoint)squatter.LocalEndpoint).Port;
        try
        {
            var options = new Gate1HostOptions
            {
                DashboardPort = 0,
                GuardPort = busyPort,
                StartDashboard = false,
                TrustedGuardPublicKeyPem = key.ExportRSAPublicKeyPem(),
                ClientProcessName = "nosai-absent-client-4f1c9a2e"
            };
            await using var host = new Gate1BootstrapHost(options, probe: new ThrowingHardwareProbe());
            try
            {
                await host.StartAsync().ConfigureAwait(false);
                return false; // A busy Guard port must not look like a successful start.
            }
            catch (GuardChannelBindException ex)
            {
                return ex.Reason == $"guard_port_in_use:{busyPort}"
                       && ex.Message.Contains("--guard-port", StringComparison.Ordinal);
            }
        }
        finally
        {
            squatter.Stop();
        }
    }

    private sealed class ThrowingHardwareProbe : IHardwareProbe
    {
        public HardwareFingerprint Detect() => throw new InvalidOperationException("probe_unavailable");
    }
}
