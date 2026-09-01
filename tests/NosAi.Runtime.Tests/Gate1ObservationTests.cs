using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using NosAi.LiveIntegration;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Configuration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate1;
using NosAi.Runtime.Observability;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Turning the world-channel observation path on: the option, the host
/// composition, and the snapshot fields the operator can actually see.
/// </summary>
public sealed class Gate1ObservationTests
{
    private static readonly IPAddress Server = IPAddress.Parse("79.110.84.175");
    private const int ServerPort = 4002;
    private const string Client = "192.168.0.4";
    private const int ClientPort = 56027;
    private static readonly GameEndpoint Endpoint = new("79.110.84.175", 4002);

    /// <summary>Golden world-channel bytes: an <c>mv</c> then a <c>stat</c> (HP 7305 / MP 1420).</summary>
    private const string GoldenHex =
        "0292899217175D81565155419EFF048C8B9E8B9C1B7491B749158641586414155C8EFF";

    private static Dictionary<string, string?> EmptyEnv() => new();

    private static Gate1HostOptions AbsentClientOptions() => new()
    {
        GuardPort = 0,
        DashboardPort = 0,
        StartDashboard = false,
        EnableDiscovery = false,
        ClientProcessName = $"nosai-absent-client-{Guid.NewGuid():N}"
    };

    private sealed class RecordingLogger : IRuntimeLogger
    {
        private readonly object _sync = new();
        public List<string> Lines { get; } = new();

        public void Info(string message, IReadOnlyDictionary<string, object?>? properties = null)
            => Record("INFO", message, properties);

        public void Warning(string message, IReadOnlyDictionary<string, object?>? properties = null)
            => Record("WARN", message, properties);

        public void Error(string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? properties = null)
            => Record("ERROR", message, properties);

        private void Record(string level, string message, IReadOnlyDictionary<string, object?>? properties)
        {
            var suffix = properties is null || properties.Count == 0
                ? string.Empty
                : " " + string.Join(" ", properties.Select(p => $"{p.Key}={p.Value}"));
            lock (_sync)
                Lines.Add($"{level} {message}{suffix}");
        }
    }

    private static byte[] TcpInbound(uint seq, ReadOnlySpan<byte> body)
    {
        var packet = new byte[20 + 20 + body.Length];
        packet[0] = 0x45;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), (ushort)packet.Length);
        packet[9] = 6;
        Server.GetAddressBytes().CopyTo(packet, 12);
        IPAddress.Parse(Client).GetAddressBytes().CopyTo(packet, 16);
        var tcp = packet.AsSpan(20);
        BinaryPrimitives.WriteUInt16BigEndian(tcp[..2], ServerPort);
        BinaryPrimitives.WriteUInt16BigEndian(tcp.Slice(2, 2), ClientPort);
        BinaryPrimitives.WriteUInt32BigEndian(tcp.Slice(4, 4), seq);
        tcp[12] = 5 << 4;
        body.CopyTo(tcp[20..]);
        return packet;
    }

    [Fact]
    public void Absent_option_leaves_observation_off()
    {
        Gate1HostOptions options = Gate1HostOptionsLoader.Load(EmptyEnv(), ["--no-dashboard", "--guard-port", "0"]);
        Assert.Null(options.ObserveGame);
    }

    [Fact]
    public void Env_and_flag_set_the_endpoint()
    {
        Gate1HostOptions fromFlag = Gate1HostOptionsLoader.Load(
            EmptyEnv(), ["--observe-game", "79.110.84.175:4002", "--no-dashboard", "--guard-port", "0"]);
        Assert.Equal("79.110.84.175", fromFlag.ObserveGame!.Host);
        Assert.Equal(4002, fromFlag.ObserveGame.Port);

        Gate1HostOptions fromEnv = Gate1HostOptionsLoader.Load(
            new Dictionary<string, string?> { ["NOSAI_OBSERVE_GAME"] = "127.0.0.1:1" },
            ["--no-dashboard", "--guard-port", "0"]);
        Assert.Equal("127.0.0.1", fromEnv.ObserveGame!.Host);
        Assert.Equal(1, fromEnv.ObserveGame.Port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_env_is_absence_not_malformed(string blank)
    {
        Gate1HostOptions options = Gate1HostOptionsLoader.Load(
            new Dictionary<string, string?> { ["NOSAI_OBSERVE_GAME"] = blank },
            ["--no-dashboard", "--guard-port", "0"]);
        Assert.Null(options.ObserveGame);
    }

    [Theory]
    [InlineData("noport")]
    [InlineData(":4002")]
    [InlineData("79.110.84.175:")]
    [InlineData("79.110.84.175:0")]
    [InlineData("79.110.84.175:65536")]
    [InlineData("not-an-ip:4002")]
    [InlineData("localhost:4002")]
    public void Malformed_option_is_refused(string raw)
    {
        Assert.Throws<InvalidOperationException>(() =>
            Gate1HostOptionsLoader.Load(EmptyEnv(), ["--observe-game", raw, "--no-dashboard", "--guard-port", "0"]));
    }

    [Fact]
    public void Flag_without_value_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Gate1HostOptionsLoader.Load(EmptyEnv(), ["--observe-game"]));
    }

    [Fact]
    public async Task Absent_option_keeps_gameplay_unknown_with_the_historical_reason()
    {
        var logger = new RecordingLogger();
        await using var host = new Gate1BootstrapHost(AbsentClientOptions(), logger);
        Gate1CanonicalSnapshot snapshot = host.Capture();

        Assert.Equal(DataSourceKind.Unknown, snapshot.Client.GameplayBaseline.Source);
        Assert.Equal("gameplay_provider_not_available", snapshot.Client.GameplayBaseline.FailureReason);
        Assert.False(snapshot.GameObservation.Active.Value);
        Assert.Equal("observation_not_configured", snapshot.GameObservation.Endpoint.FailureReason);
        Assert.Equal("observation_not_configured", snapshot.GameObservation.PacketsObserved.FailureReason);
    }

    [Fact]
    public void Driver_unavailable_is_a_named_failure_not_a_synthetic_provider()
    {
        var logger = new RecordingLogger();
        const string reason = "windivert_dll_not_found";
        using var channel = Gate1ObservationChannel.TryOpenLive(
            Endpoint,
            logger,
            opener: (IPAddress _, int _, out string? failure) =>
            {
                failure = reason;
                return null;
            });

        Assert.Null(channel.Provider);
        Assert.Equal(reason, channel.FailureReason);
        Assert.Contains(logger.Lines, line => line.Contains(reason, StringComparison.Ordinal));

        GameplayObservation gameplay = UnavailableGameplayProvider.Instance.Observe();
        Gate1GameObservationView view = channel.Describe(gameplay);
        Assert.False(view.Active.Value);
        Assert.Equal(reason, view.PacketsObserved.FailureReason);
        Assert.Equal("gameplay_provider_not_available", view.LastHp.FailureReason);
        Assert.False(string.IsNullOrWhiteSpace(view.Endpoint.Value));
    }

    [Fact]
    public async Task Option_present_and_driver_unavailable_still_starts_the_host()
    {
        var logger = new RecordingLogger();
        var options = new Gate1HostOptions
        {
            GuardPort = 0,
            DashboardPort = 0,
            StartDashboard = false,
            EnableDiscovery = false,
            ClientProcessName = $"nosai-observe-{Guid.NewGuid():N}",
            ObserveGame = new GameEndpoint("127.0.0.1", 4002)
        };

        await using var host = new Gate1BootstrapHost(options, logger);
        Gate1CanonicalSnapshot snapshot = host.Capture();

        Assert.Equal(DataSourceKind.Unknown, snapshot.Client.GameplayBaseline.Source);
        string blob = string.Join("\n", logger.Lines);
        bool opened = snapshot.Client.GameplayBaseline.FailureReason != "gameplay_provider_not_available";
        if (opened)
        {
            // The driver was actually present on this machine. Gameplay stays
            // UNKNOWN until a packet is seen; it is not a fake reading.
            Assert.False(snapshot.Client.Gameplay!.HasVitals);
        }
        else
        {
            Assert.Equal("gameplay_provider_not_available", snapshot.Client.GameplayBaseline.FailureReason);
            Assert.True(
                blob.Contains("windivert_", StringComparison.Ordinal)
                || blob.Contains("access_denied", StringComparison.Ordinal)
                || blob.Contains("driver_", StringComparison.Ordinal)
                || blob.Contains("capture_backend_unavailable", StringComparison.Ordinal),
                $"Expected a named open failure in the log, got:\n{blob}");
            Assert.False(snapshot.GameObservation.Active.HasValue && snapshot.GameObservation.Active.Value);
            Assert.False(snapshot.GameObservation.PacketsObserved.HasValue);
            Assert.False(string.IsNullOrWhiteSpace(snapshot.GameObservation.PacketsObserved.FailureReason));
        }
    }

    [Fact]
    public async Task Snapshot_publishes_provider_fields_from_an_in_memory_source()
    {
        // CACHED, not LIVE: these bytes did not come from a running client.
        byte[] golden = Convert.FromHexString(GoldenHex);
        var packets = new InMemoryPacketSource(
            Server,
            ServerPort,
            [new CapturedPacket(new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc), TcpInbound(1000, golden))]);

        using var observation = Gate1ObservationChannel.FromPackets(packets, Endpoint, DataSourceKind.Cached);
        var runtime = NosAi.Runtime.Orchestration.RuntimeComposition.CreateSafe();
        var world = new NosAi.Runtime.WorldModel.WorldModel();
        using var key = RSA.Create(2048);
        using var auth = new SessionAuth(key.ExportRSAPublicKeyPem());
        await using var channel = new GuardAiNetworkChannel(0, auth);
        var provider = new Gate1RuntimeSnapshotProvider(
            runtime, world, channel,
            gameplay: observation.Provider,
            observation: observation);

        Gate1CanonicalSnapshot snapshot = provider.Capture();

        Assert.NotNull(snapshot.Client.Gameplay);
        Assert.True(snapshot.Client.Gameplay!.HasVitals);
        Assert.Equal(7305, snapshot.Client.Gameplay.Hp.Value);
        Assert.Equal(7305, snapshot.Client.Gameplay.MaxHp.Value);
        Assert.Equal(1420, snapshot.Client.Gameplay.Mp.Value);
        Assert.Equal(DataSourceKind.Cached, snapshot.Client.Gameplay.Hp.Source);
        Assert.Equal(DataSourceKind.Derived, snapshot.Client.GameplayBaseline.Source);

        Assert.True(snapshot.GameObservation.Active.Value);
        Assert.Equal("79.110.84.175:4002", snapshot.GameObservation.Endpoint.Value);
        Assert.True(snapshot.GameObservation.PacketsObserved.HasValue);
        Assert.True(snapshot.GameObservation.PacketsObserved.Value > 0);
        Assert.True(snapshot.GameObservation.PacketsDecoded.Value > 0);
        Assert.Equal(7305, snapshot.GameObservation.LastHp.Value);
        Assert.Equal(DataSourceKind.Cached, snapshot.GameObservation.LastHp.Source);
        Assert.True(snapshot.GameObservation.LastVitalsAtUtc.HasValue);
    }
}
