using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate1;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Perception.Network;

namespace NosAi.LiveIntegration;

/// <summary>
/// A real, scoped WinDivert capture of one client process's own game
/// connection, with the live gameplay provider composed over it.
/// </summary>
/// <remarks>
/// <para>
/// The composition mirrors <c>Gate1ObservationChannel.FromPackets</c>:
/// <c>ReassembledObservationSource.ForNosTaleWorld</c> over a packet source,
/// decoded by <see cref="NosTaleWorldProtocolDecoder"/> into a
/// <see cref="NetworkWorldFeed"/> and published by
/// <see cref="NetworkGameplayProvider"/>. The packet source is
/// <see cref="WinDivertPacketSource"/> opened on the client's single TCP game
/// connection, whose address and port are read from the OS connection table by
/// <see cref="ClientNetworkObserver"/> -- the same discipline
/// <c>WireRecorder</c> and <c>Gate1ObservationChannel.TryOpenLive</c> use (a
/// hard-coded server address would keep capturing a host the client had
/// already stopped talking to). The <see cref="RealClientConnector"/> half is
/// built over an unstarted <see cref="GuardAiNetworkChannel"/> with an
/// ephemeral key: only its process/window observation is used by
/// <see cref="LiveObservationGateway.Capture"/>, never its transport (the same
/// construction <c>LiveObservationGatewayTests</c> uses). Disposal order: the
/// capture is released first through the observation source that owns it, then
/// the connector (which disposes its channel), then the auth key.
/// </para>
/// <para>
/// Extracted from <c>CollectCommand</c> and <c>LoadoutReportCommand</c>, which
/// had grown private copies of it: identical in composition, disposal order
/// and failure-reason strings, though not line for line -- one carried 26
/// lines of doc comment the other did not. Two further commands needing the
/// same live entity feed (<c>ScoutCommand</c> and <c>AutoplayCommand</c>, both
/// added by the same commit) made one shared type the only sane answer.
/// Behaviour is unchanged from those copies, so the tests that pin either
/// command's refusals keep passing untouched.
/// </para>
/// <para>
/// Opening one needs the capture backend, and therefore Administrator rights
/// on Windows. What a caller does with a failed open is the caller's choice,
/// and the two answers are both deliberate: <c>CollectCommand</c>,
/// <c>LoadoutReportCommand</c>, <c>ScoutCommand</c> and <c>AutoplayCommand</c>
/// treat it as a named, non-fatal condition and carry on -- a command that
/// could run without an entity feed before this type existed must still run
/// without one -- while <c>CombatReportCommand</c> and <c>EngageCommand</c>
/// refuse, because a combat report with no entities would say nothing and an
/// engagement with no entities could not verify its target. What this type
/// guarantees to all six is narrower: a failed open <b>names</b> why, and
/// never throws.
/// </para>
/// </remarks>
public sealed class LiveObservationScope : IDisposable
{
    private readonly SessionAuth _auth;
    private readonly RealClientConnector _client;
    private readonly ReassembledObservationSource _observationSource;

    /// <summary>The composed live gateway: capture it to read one <see cref="LiveObservationSnapshot"/>.</summary>
    public LiveObservationGateway Gateway { get; }

    private LiveObservationScope(
        SessionAuth auth,
        RealClientConnector client,
        ReassembledObservationSource observationSource,
        IGameplayProvider provider)
    {
        _auth = auth;
        _client = client;
        _observationSource = observationSource;
        Gateway = new LiveObservationGateway(client, provider);
    }

    /// <summary>Opens the capture for <paramref name="processId"/>, or names why it could not.</summary>
    public static LiveObservationScope? TryOpen(int processId, out string? failureReason)
    {
        failureReason = null;

        // The client's own game connection, read from the OS connection table
        // (the same discipline ClientNetworkObserver documents: a hard-coded
        // server address would keep capturing a host the client had already
        // stopped talking to).
        ClientNetworkObservation observation = ClientNetworkObserver.Observe(processId);
        if (!observation.Observed)
        {
            failureReason = $"connection_table:{observation.FailureReason}";
            return null;
        }

        if (observation.Primary is not ClientTcpConnection primary)
        {
            failureReason = $"no_single_game_connection:{observation.Connections.Count}_candidates";
            return null;
        }

        WinDivertPacketSource? packets = WinDivertPacketSource.TryOpen(
            primary.Remote.Address, primary.Remote.Port, out string? openFailure);
        if (packets is null)
        {
            failureReason = openFailure ?? "capture_backend_unavailable";
            return null;
        }

        var endpoint = new GameEndpoint(primary.Remote.Address.ToString(), primary.Remote.Port);
        ReassembledObservationSource observationSource =
            ReassembledObservationSource.ForNosTaleWorld(packets, DataSourceKind.Live);
        var observer = new GameTrafficObserver(
            observationSource,
            new ScopedGameTrafficFilter(endpoint),
            new NosTaleWorldProtocolDecoder());
        var feed = new NetworkWorldFeed(observer);
        IGameplayProvider provider = new NetworkGameplayProvider(feed);

        var auth = new SessionAuth(NewPublicKeyPem());
        var channel = new GuardAiNetworkChannel(0, auth);
        var client = new RealClientConnector(channel);

        return new LiveObservationScope(auth, client, observationSource, provider);
    }

    private static string NewPublicKeyPem()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        return rsa.ExportRSAPublicKeyPem();
    }

    public void Dispose()
    {
        // The observation source's Dispose disposes the inner capture
        // (ScopedLiveCaptureBackend -> WinDivertPacketSource): releasing the
        // capture first stops the wire reads.
        _observationSource.Dispose();
        // RealClientConnector.DisposeAsync disposes its GuardAiNetworkChannel
        // and the held process handle; the channel is never started here.
        _client.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _auth.Dispose();
    }
}
