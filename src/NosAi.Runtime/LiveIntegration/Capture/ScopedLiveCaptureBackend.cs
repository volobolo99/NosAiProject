using System.Net;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;
using NosAi.Runtime.Security;

namespace NosAi.LiveIntegration.Capture;

/// <summary>
/// A real, scoped capture backend for the perception channel's observation source.
/// </summary>
/// <remarks>
/// <para>
/// The perception channel declares <see cref="IRawScopedCaptureBackend"/> and ships
/// no implementation, because a real capture needs a driver and a live endpoint.
/// This is that implementation: it binds WinDivert to <b>one</b> connection and
/// hands the packets to the decoder as <see cref="DataSourceKind.Live"/>.
/// </para>
/// <para>
/// <b>Superseded as a gameplay source.</b> The live gameplay path is
/// <see cref="ReassembledObservationSource.ForNosTaleWorld"/> composed in
/// <c>Gate1BootstrapHost</c>. This class is not that path. <see cref="TryObserve"/>
/// still returns the payload of one TCP segment in arrival order, labelled LIVE —
/// exactly the failure the reassembled source was written to close (a field read
/// at a wrong offset, wearing LIVE). Changing that behaviour here would break the
/// perception-channel contract this type still satisfies, so the class is kept
/// for that contract and is not wired into Gate 1 observation. Two live roads
/// of which one is wrong is worse than one unused backend with this comment.
/// </para>
/// <para>
/// <b>Scoped, not promiscuous.</b> The filter names the game's own address and port,
/// so nothing else on the machine is read. That is a property of the filter handed
/// to the driver, not a check applied afterwards to traffic already collected.
/// </para>
/// <para>
/// <b>Observation only.</b> There is no send, inject or modify path here. Reading
/// the traffic and putting something on the wire are different capabilities with
/// different decisions behind them (ADR-0014), and this class holds only the first.
/// </para>
/// <para>
/// <b>No substitute when it cannot capture.</b> Without the driver the backend
/// reports <see cref="IsCapturing"/> false with a named reason and observes nothing.
/// Falling back to synthetic packets here would feed the world model invented bytes
/// wearing a LIVE label, which is the one thing this channel exists to prevent.
/// </para>
/// </remarks>
public sealed class ScopedLiveCaptureBackend : IRawScopedCaptureBackend
{
    private readonly IPacketSource _packets;
    private readonly IPAddress _serverAddress;
    private readonly int _serverPort;
    private bool _disposed;

    /// <inheritdoc />
    public GameEndpoint Endpoint { get; }

    /// <inheritdoc />
    public bool IsCapturing => !_disposed;

    /// <summary>Why the backend could not bind, when it could not.</summary>
    public string? FailureReason { get; private init; }

    /// <summary>Packets seen that carried no payload, and so were not observations.</summary>
    public long EmptySegments { get; private set; }

    /// <summary>Packets that could not be parsed as IPv4/TCP.</summary>
    public long UnparsedPackets { get; private set; }

    private ScopedLiveCaptureBackend(
        IPacketSource packets, IPAddress serverAddress, int serverPort, GameEndpoint endpoint)
    {
        _packets = packets;
        _serverAddress = serverAddress;
        _serverPort = serverPort;
        Endpoint = endpoint;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="DataSourceKind.Live"/> and nothing else. This backend either
    /// reports what crossed the wire or reports nothing.
    /// </remarks>
    public DataSourceKind Source => DataSourceKind.Live;

    /// <summary>
    /// Binds to the connection a client process is actually using.
    /// </summary>
    /// <remarks>
    /// The endpoint is read from the OS connection table rather than configured:
    /// a hard-coded server address would keep capturing a host the client had
    /// already stopped talking to, and would report that silence as calm.
    /// </remarks>
    public static ScopedLiveCaptureBackend? TryOpenForProcess(
        int processId,
        SecurityPrincipal principal,
        out string? failureReason,
        IRuntimeAuthorizationPolicy? authorization = null)
    {
        failureReason = null;

        // Authorised before a driver handle exists, like every other privileged
        // read in this runtime. The phone may ask; it may not capture.
        var policy = authorization ?? new Gate1AuthorizationPolicy();
        AuthorizationDecision decision = policy.Evaluate(
            principal, RuntimeCapability.ReadGameTraffic, TrustTier.Tier1_Assisted, TrustTier.Tier4_FullAutonomous);
        if (!decision.Allowed)
        {
            failureReason = $"not_authorized:{decision.Reason}";
            return null;
        }

        ClientNetworkObservation observation = ClientNetworkObserver.Observe(processId);
        if (!observation.Observed)
        {
            failureReason = $"connection_table:{observation.FailureReason}";
            return null;
        }

        if (observation.Primary is not ClientTcpConnection primary)
        {
            // Either nothing or more than one candidate. Picking one would be a
            // guess about which conversation is the game.
            failureReason = $"no_single_game_connection:{observation.Connections.Count}_candidates";
            return null;
        }

        return TryOpen(primary.Remote.Address, primary.Remote.Port, principal,
            out failureReason, authorization);
    }

    /// <summary>Binds to a named endpoint.</summary>
    public static ScopedLiveCaptureBackend? TryOpen(
        IPAddress serverAddress,
        int serverPort,
        SecurityPrincipal principal,
        out string? failureReason,
        IRuntimeAuthorizationPolicy? authorization = null)
    {
        ArgumentNullException.ThrowIfNull(serverAddress);
        failureReason = null;

        var policy = authorization ?? new Gate1AuthorizationPolicy();
        AuthorizationDecision decision = policy.Evaluate(
            principal, RuntimeCapability.ReadGameTraffic, TrustTier.Tier1_Assisted, TrustTier.Tier4_FullAutonomous);
        if (!decision.Allowed)
        {
            failureReason = $"not_authorized:{decision.Reason}";
            return null;
        }

        if (serverPort is <= 0 or > 65535)
        {
            failureReason = $"invalid_port:{serverPort}";
            return null;
        }

        WinDivertPacketSource? source = WinDivertPacketSource.TryOpen(
            serverAddress, serverPort, out string? openFailure);
        if (source is null)
        {
            failureReason = openFailure ?? "capture_backend_unavailable";
            return null;
        }

        return new ScopedLiveCaptureBackend(
            source, serverAddress, serverPort, new GameEndpoint(serverAddress.ToString(), serverPort));
    }

    /// <summary>
    /// Wraps an arbitrary packet source. Used to exercise this class without a driver.
    /// </summary>
    /// <remarks>
    /// The source decides what the packets are; this only labels direction and
    /// classification. A caller passing recorded packets is responsible for saying
    /// so — see <see cref="ScopedCaptureBackendOverSource"/>, which exists precisely
    /// so a replay cannot be handed out as LIVE through this constructor.
    /// </remarks>
    internal static ScopedLiveCaptureBackend OverSource(
        IPacketSource source, IPAddress serverAddress, int serverPort) =>
        new(source, serverAddress, serverPort, new GameEndpoint(serverAddress.ToString(), serverPort));

    /// <inheritdoc />
    public bool TryObserve(out ObservedPacket packet)
    {
        packet = null!;
        if (_disposed)
            return false;

        // One packet per call, with a short window: the caller drives the pace and a
        // quiet wire is not an error.
        if (!_packets.TryRead(TimeSpan.FromMilliseconds(250), out CapturedPacket captured))
            return false;

        ParsedPacket parsed = Ipv4TcpParser.Parse(captured.Raw.Span);
        if (!parsed.Ok)
        {
            UnparsedPackets++;
            return false;
        }

        if (parsed.Payload.Length == 0)
        {
            // An acknowledgement carries no observation. Counting it as one would
            // make an idle connection look busy.
            EmptySegments++;
            return false;
        }

        bool inbound = parsed.Source is not null && parsed.Source.Equals(_serverAddress);

        packet = new ObservedPacket(
            captured.TimestampUtc,
            inbound ? NetworkDirection.Inbound : NetworkDirection.Outbound,
            _serverAddress.ToString(),
            _serverPort,
            parsed.Payload,
            DataSourceKind.Live);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _packets.Dispose();
    }
}

/// <summary>
/// A capture backend over a source whose provenance the caller states.
/// </summary>
/// <remarks>
/// Kept separate from <see cref="ScopedLiveCaptureBackend"/> so replayed or
/// synthetic packets can never leave through a class whose whole contract is that
/// its packets are live. The classification travels from the caller, and a caller
/// that lies about it is making a claim in its own name rather than borrowing this
/// one's.
/// </remarks>
public sealed class ScopedCaptureBackendOverSource : IRawScopedCaptureBackend
{
    private readonly ScopedLiveCaptureBackend _inner;
    private bool _disposed;

    public ScopedCaptureBackendOverSource(
        IPacketSource source, IPAddress serverAddress, int serverPort, DataSourceKind source_)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(serverAddress);
        if (source_ == DataSourceKind.Live)
        {
            throw new ArgumentException(
                "Solo ScopedLiveCaptureBackend può dichiarare LIVE: una sorgente riprodotta o " +
                "sintetica etichettata come live è esattamente ciò che questo canale impedisce.",
                nameof(source_));
        }

        _inner = ScopedLiveCaptureBackend.OverSource(source, serverAddress, serverPort);
        Source = source_;
    }

    public DataSourceKind Source { get; }

    public GameEndpoint Endpoint => _inner.Endpoint;

    public bool IsCapturing => !_disposed && _inner.IsCapturing;

    public bool TryObserve(out ObservedPacket packet)
    {
        packet = null!;
        if (!_inner.TryObserve(out ObservedPacket observed))
            return false;

        // Re-labelled with the provenance the caller declared, never left as LIVE.
        packet = observed with { Source = Source };
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _inner.Dispose();
    }
}
