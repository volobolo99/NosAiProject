using System.Net;
using NosAi.LiveIntegration;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Observability;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Perception.Network;
using NosAi.Runtime.Security;

namespace NosAi.Runtime.Gate1;

/// <summary>
/// Opens a packet source for the world channel. Matches
/// <see cref="WinDivertPacketSource.TryOpen"/> so the live host and the tests
/// share one composition path.
/// </summary>
public delegate IPacketSource? TryOpenPacketSource(
    IPAddress serverAddress, int serverPort, out string? failureReason);

/// <summary>
/// The operator-facing state of the game-observation channel: whether it is
/// capturing, which endpoint it is bound to, and the counters of the last
/// polls. Unknown fields keep a reason; they are never zeroed to look quiet.
/// </summary>
public sealed record Gate1GameObservationView(
    ClassifiedValue<bool> Active,
    ClassifiedValue<string> Endpoint,
    ClassifiedValue<long> PacketsObserved,
    ClassifiedValue<long> PacketsDecoded,
    ClassifiedValue<long> PacketsUndecodable,
    ClassifiedValue<int> LastHp,
    ClassifiedValue<int> LastMaxHp,
    ClassifiedValue<int> LastMp,
    ClassifiedValue<DateTime> LastVitalsAtUtc)
{
    public object ToWire() => new
    {
        active = Active.ToWire(),
        endpoint = Endpoint.ToWire(),
        packetsObserved = PacketsObserved.ToWire(),
        packetsDecoded = PacketsDecoded.ToWire(),
        packetsUndecodable = PacketsUndecodable.ToWire(),
        lastHp = LastHp.ToWire(),
        lastMaxHp = LastMaxHp.ToWire(),
        lastMp = LastMp.ToWire(),
        lastVitalsAtUtc = LastVitalsAtUtc.ToWire()
    };

    /// <summary>No observation option was set. The channel is off on purpose.</summary>
    public static Gate1GameObservationView NotConfigured()
    {
        const string reason = "observation_not_configured";
        return new(
            ClassifiedValue<bool>.Derived(false),
            ClassifiedValue<string>.Unknown(reason),
            ClassifiedValue<long>.Unknown(reason),
            ClassifiedValue<long>.Unknown(reason),
            ClassifiedValue<long>.Unknown(reason),
            ClassifiedValue<int>.Unknown(reason),
            ClassifiedValue<int>.Unknown(reason),
            ClassifiedValue<int>.Unknown(reason),
            ClassifiedValue<DateTime>.Unknown(reason));
    }
}

/// <summary>
/// Owns the world-channel observation chain for one Gate 1 host, or records why
/// that chain is not running. Disposing it disposes the packet source.
/// </summary>
/// <remarks>
/// <para>
/// Without a driver the host still starts: this object stays in the failed
/// state, the snapshot keeps publishing gameplay as UNKNOWN, and nothing
/// synthetic is substituted. That is the same refusal
/// <see cref="UnavailableGameplayProvider"/> already enacted.
/// </para>
/// <para>
/// LIVE is used only when the packet source is a live driver capture. An
/// in-memory or recorded source is labelled by the caller, typically CACHED.
/// </para>
/// </remarks>
public sealed class Gate1ObservationChannel : IDisposable
{
    private readonly NetworkWorldFeed? _feed;
    private readonly ReassembledObservationSource? _source;
    private readonly DataSourceKind _countSource;
    private long _observed;
    private long _decoded;
    private long _undecodable;
    private bool _disposed;

    public GameEndpoint? Endpoint { get; }
    public string? FailureReason { get; }
    public IGameplayProvider? Provider { get; }
    public bool IsCapturing => Provider is not null && _feed is not null && !_disposed;

    private Gate1ObservationChannel(
        GameEndpoint? endpoint,
        string? failureReason,
        IGameplayProvider? provider,
        NetworkWorldFeed? feed,
        ReassembledObservationSource? source,
        DataSourceKind countSource)
    {
        Endpoint = endpoint;
        FailureReason = failureReason;
        Provider = provider;
        _feed = feed;
        _source = source;
        _countSource = countSource;
        if (_feed is not null)
            _feed.Subscribe(OnReport);
    }

    /// <summary>Observation was never requested.</summary>
    public static Gate1ObservationChannel None() =>
        new(null, "observation_not_configured", null, null, null, DataSourceKind.Unknown);

    /// <summary>The operator asked, and the driver (or opener) said no.</summary>
    public static Gate1ObservationChannel Failed(GameEndpoint endpoint, string reason) =>
        new(endpoint, reason, null, null, null, DataSourceKind.Unknown);

    /// <summary>
    /// Builds the real chain over a caller-supplied packet source. Used by the
    /// live host after WinDivert opens, and by tests over an in-memory list.
    /// </summary>
    /// <param name="targetFrames">
    /// The screen's reading of the target frame, or null when this runtime has no
    /// capture of the client. Supplying it is what gives <c>HasTarget</c> a source
    /// (ADR-0018): the wire has <c>ct</c> and <c>su</c> and no observed
    /// counterpart that clears a target, so a flag derived from it alone would go
    /// true once and stay true. Without it the fact stays UNKNOWN, as it is today.
    /// </param>
    /// <param name="targetRoi">
    /// Where the target frame sits on this client. Null loads the operator's
    /// calibration from <see cref="TargetRoiCalibration.RelativePath"/>; an
    /// uncalibrated one keeps <c>HasTarget</c> UNKNOWN rather than reading a
    /// region nobody aimed.
    /// </param>
    public static Gate1ObservationChannel FromPackets(
        IPacketSource packets,
        GameEndpoint endpoint,
        DataSourceKind streamSource,
        ITargetFrameSource? targetFrames = null,
        TargetRoiCalibration? targetRoi = null)
    {
        ArgumentNullException.ThrowIfNull(packets);
        ArgumentNullException.ThrowIfNull(endpoint);

        var source = ReassembledObservationSource.ForNosTaleWorld(packets, streamSource);
        var observer = new GameTrafficObserver(
            source,
            new ScopedGameTrafficFilter(endpoint),
            new NosTaleWorldProtocolDecoder());
        var feed = new NetworkWorldFeed(observer);
        IGameplayProvider provider = new NetworkGameplayProvider(feed);

        if (targetFrames is not null)
        {
            provider = new TargetAwareGameplayProvider(
                provider,
                targetFrames,
                targetRoi ?? TargetRoiCalibration.Load(
                    Path.Combine(Directory.GetCurrentDirectory(), TargetRoiCalibration.RelativePath), out _),
                feed);
        }

        return new Gate1ObservationChannel(endpoint, null, provider, feed, source, streamSource);
    }

    /// <summary>
    /// Attempts the live driver. A missing driver or a lack of elevation is a
    /// named failure, not a crash and not a synthetic substitute.
    /// </summary>
    public static Gate1ObservationChannel TryOpenLive(
        GameEndpoint endpoint,
        IRuntimeLogger logger,
        TryOpenPacketSource? opener = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(logger);

        var policy = new Gate1AuthorizationPolicy();
        AuthorizationDecision decision = policy.Evaluate(
            SecurityPrincipal.Operator, RuntimeCapability.ReadGameTraffic, TrustTier.Tier1, TrustTier.Tier4);
        if (!decision.Allowed)
        {
            string denied = $"not_authorized:{decision.Reason}";
            LogFailure(logger, endpoint, denied);
            return Failed(endpoint, denied);
        }

        if (!IPAddress.TryParse(endpoint.Host, out IPAddress? address))
        {
            const string notIp = "observe_game_host_not_an_ip_address";
            LogFailure(logger, endpoint, notIp);
            return Failed(endpoint, notIp);
        }

        TryOpenPacketSource open = opener ?? WinDivertPacketSource.TryOpen;
        IPacketSource? packets = open(address, endpoint.Port, out string? failureReason);
        if (packets is null)
        {
            string reason = failureReason ?? "capture_backend_unavailable";
            LogFailure(logger, endpoint, reason);
            return Failed(endpoint, reason);
        }

        logger.Info("Game observation attached.", new Dictionary<string, object?>
        {
            ["endpoint"] = $"{endpoint.Host}:{endpoint.Port}",
            ["source"] = DataSourceKind.Live.ToWire()
        });
        return FromPackets(packets, endpoint, DataSourceKind.Live);
    }

    public Gate1GameObservationView Describe(GameplayObservation gameplay)
    {
        ArgumentNullException.ThrowIfNull(gameplay);

        if (Provider is null)
        {
            if (Endpoint is null)
                return Gate1GameObservationView.NotConfigured();

            string reason = FailureReason ?? "capture_backend_unavailable";
            return new Gate1GameObservationView(
                ClassifiedValue<bool>.Derived(false),
                ClassifiedValue<string>.Derived($"{Endpoint.Host}:{Endpoint.Port}"),
                ClassifiedValue<long>.Unknown(reason),
                ClassifiedValue<long>.Unknown(reason),
                ClassifiedValue<long>.Unknown(reason),
                ClassifiedValue<int>.Unknown("gameplay_provider_not_available"),
                ClassifiedValue<int>.Unknown("gameplay_provider_not_available"),
                ClassifiedValue<int>.Unknown("gameplay_provider_not_available"),
                ClassifiedValue<DateTime>.Unknown("gameplay_provider_not_available"));
        }

        return new Gate1GameObservationView(
            ClassifyActive(true),
            ClassifyEndpoint($"{Endpoint!.Host}:{Endpoint.Port}"),
            ClassifyCount(Interlocked.Read(ref _observed)),
            ClassifyCount(Interlocked.Read(ref _decoded)),
            ClassifyCount(Interlocked.Read(ref _undecodable)),
            gameplay.Hp,
            gameplay.MaxHp,
            gameplay.Mp,
            ClassifyVitalsTime(gameplay));
    }

    private void OnReport(NetworkObservationReport report)
    {
        Interlocked.Add(ref _observed, report.ObservedPackets);
        Interlocked.Add(ref _decoded, report.DecodedPackets);
        Interlocked.Add(ref _undecodable, report.UndecodablePackets);
    }

    private ClassifiedValue<bool> ClassifyActive(bool active) => _countSource switch
    {
        DataSourceKind.Live => ClassifiedValue<bool>.Live(active),
        DataSourceKind.Cached => ClassifiedValue<bool>.Cached(active, DateTime.UtcNow),
        DataSourceKind.Derived => ClassifiedValue<bool>.Derived(active),
        DataSourceKind.Simulated => ClassifiedValue<bool>.Simulated(active),
        _ => ClassifiedValue<bool>.Unknown("source_unknown")
    };

    private ClassifiedValue<string> ClassifyEndpoint(string endpoint) => _countSource switch
    {
        DataSourceKind.Live => ClassifiedValue<string>.Live(endpoint),
        DataSourceKind.Cached => ClassifiedValue<string>.Cached(endpoint, DateTime.UtcNow),
        DataSourceKind.Derived => ClassifiedValue<string>.Derived(endpoint),
        DataSourceKind.Simulated => ClassifiedValue<string>.Simulated(endpoint),
        _ => ClassifiedValue<string>.Unknown("source_unknown")
    };

    private ClassifiedValue<long> ClassifyCount(long count) => _countSource switch
    {
        DataSourceKind.Live => ClassifiedValue<long>.Live(count),
        DataSourceKind.Cached => ClassifiedValue<long>.Cached(count, DateTime.UtcNow),
        DataSourceKind.Derived => ClassifiedValue<long>.Derived(count),
        DataSourceKind.Simulated => ClassifiedValue<long>.Simulated(count),
        _ => ClassifiedValue<long>.Unknown("source_unknown")
    };

    private static ClassifiedValue<DateTime> ClassifyVitalsTime(GameplayObservation gameplay)
    {
        if (!gameplay.Hp.HasValue)
            return ClassifiedValue<DateTime>.Unknown(
                gameplay.Hp.FailureReason ?? gameplay.UnusableReason ?? "gameplay_incomplete");

        DateTime at = gameplay.Hp.ObservedAtUtc;
        return gameplay.Hp.Source switch
        {
            DataSourceKind.Live => ClassifiedValue<DateTime>.Live(at, at),
            DataSourceKind.Cached => ClassifiedValue<DateTime>.Cached(at, at),
            DataSourceKind.Derived => ClassifiedValue<DateTime>.Derived(at, at),
            DataSourceKind.Simulated => ClassifiedValue<DateTime>.Simulated(at, at),
            _ => ClassifiedValue<DateTime>.Unknown(gameplay.Hp.FailureReason ?? "source_unknown")
        };
    }

    private static void LogFailure(IRuntimeLogger logger, GameEndpoint endpoint, string reason) =>
        logger.Warning(
            "Game observation did not start; the runtime continues without a gameplay provider.",
            new Dictionary<string, object?>
            {
                ["reason"] = reason,
                ["endpoint"] = $"{endpoint.Host}:{endpoint.Port}"
            });

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _source?.Dispose();
    }
}
