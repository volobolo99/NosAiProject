using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using NosAi.LiveIntegration;
using NosAi.Runtime.Gate1;
using Xunit;

namespace NosAi.Runtime.Tests;

public sealed class LiveObservationGatewayTests
{
    /// <summary>
    /// <see cref="RealClientConnector"/> is built directly on the canonical Gate 1
    /// channel, so a test that only exercises composition still needs a real
    /// (unstarted) channel rather than a fake: nothing about the class allows one.
    /// </summary>
    private static (SessionAuth Auth, GuardAiNetworkChannel Channel) CreateUnstartedChannel()
    {
        using var key = RSA.Create(2048);
        var auth = new SessionAuth(key.ExportRSAPublicKeyPem());
        var channel = new GuardAiNetworkChannel(0, auth);
        return (auth, channel);
    }

    [Fact]
    public async Task Capture_ComposesClientAndGameplayObservationsIntoOneSnapshot()
    {
        var (auth, channel) = CreateUnstartedChannel();
        using var _ = auth;
        await using var channelDisposable = channel;
        await using var client = new RealClientConnector(channel);
        var gameplay = new StubGameplayProvider(
            GameplayObservation.Unobserved("test_gameplay_unavailable", DateTime.UtcNow));
        var gateway = new LiveObservationGateway(client, gameplay);

        LiveObservationSnapshot snapshot = gateway.Capture();

        Assert.NotNull(snapshot.Client);
        Assert.NotNull(snapshot.Gameplay);
        Assert.Equal("test_gameplay_unavailable", snapshot.Gameplay.UnusableReason);
        Assert.True(snapshot.ObservedAtUtc >= snapshot.Client.ObservedAtUtc);
        Assert.True(snapshot.ObservedAtUtc >= snapshot.Gameplay.ObservedAtUtc);
    }

    [Fact]
    public async Task Capture_ConvertsSupportedProviderFailuresToUnknownObservation()
    {
        var (auth, channel) = CreateUnstartedChannel();
        using var _ = auth;
        await using var channelDisposable = channel;
        await using var client = new RealClientConnector(channel);
        var gateway = new LiveObservationGateway(client, new ThrowingGameplayProvider());

        LiveObservationSnapshot snapshot = gateway.Capture();

        Assert.False(snapshot.HasLiveGameplayObservation);
        Assert.StartsWith("provider_observation_failed:", snapshot.Gameplay.UnusableReason);
    }

    private sealed class StubGameplayProvider : IGameplayProvider
    {
        private readonly GameplayObservation _observation;

        public StubGameplayProvider(GameplayObservation observation)
            => _observation = observation;

        public string Name => "stub";

        public GameplayObservation Observe() => _observation;
    }

    private sealed class ThrowingGameplayProvider : IGameplayProvider
    {
        public string Name => "throwing_stub";

        public GameplayObservation Observe()
            => throw new InvalidOperationException("test provider failure");
    }
}
