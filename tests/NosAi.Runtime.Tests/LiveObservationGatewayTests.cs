using System.ComponentModel;
using System.Diagnostics;
using NosAi.LiveIntegration;
using Xunit;

namespace NosAi.Runtime.Tests;

public sealed class LiveObservationGatewayTests
{
    [Fact]
    public void Capture_ComposesClientAndGameplayObservationsIntoOneSnapshot()
    {
        using var client = new RealClientConnector();
        var gameplay = new StubGameplayProvider(
            GameplayObservation.Unobserved("test_gameplay_unavailable", DateTime.UtcNow));
        var gateway = new LiveObservationGateway(client, gameplay);

        LiveObservationSnapshot snapshot = gateway.Capture();

        Assert.NotNull(snapshot.Client);
        Assert.NotNull(snapshot.Gameplay);
        Assert.Equal("test_gameplay_unavailable", snapshot.Gameplay.FailureReason);
        Assert.True(snapshot.ObservedAtUtc >= snapshot.Client.ObservedAtUtc);
        Assert.True(snapshot.ObservedAtUtc >= snapshot.Gameplay.ObservedAtUtc);
    }

    [Fact]
    public void Capture_ConvertsSupportedProviderFailuresToUnknownObservation()
    {
        using var client = new RealClientConnector();
        var gateway = new LiveObservationGateway(client, new ThrowingGameplayProvider());

        LiveObservationSnapshot snapshot = gateway.Capture();

        Assert.False(snapshot.HasLiveGameplayObservation);
        Assert.StartsWith("provider_observation_failed:", snapshot.Gameplay.FailureReason);
    }

    private sealed class StubGameplayProvider : IGameplayProvider
    {
        private readonly GameplayObservation _observation;

        public StubGameplayProvider(GameplayObservation observation)
            => _observation = observation;

        public GameplayObservation Observe() => _observation;
    }

    private sealed class ThrowingGameplayProvider : IGameplayProvider
    {
        public GameplayObservation Observe()
            => throw new InvalidOperationException("test provider failure");
    }
}
