using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate3;
using Xunit;

namespace NosAi.Runtime.Tests;

public sealed class NetworkWorldStateObserverTests
{
    [Fact]
    public async Task Observed_vitals_keep_their_values_and_classification()
    {
        var at = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
        var observation = new GameplayObservation(
            ClassifiedValue<int>.Live(7305, at),
            ClassifiedValue<int>.Live(7305, at),
            ClassifiedValue<int>.Cached(1420, at),
            ClassifiedValue<bool>.Unknown("target_flag_not_mapped"),
            ClassifiedValue<bool>.Unknown("combat_flag_not_mapped"),
            ClassifiedValue<int>.Unknown("no_entities_reported"),
            at);
        var observer = new NetworkWorldStateObserver(new StubProvider(() => observation));

        ObservedState state = await observer.ObserveAsync();

        Assert.True(observer.CanObserve);
        Assert.Equal(observation.Hp, state.Hp);
        Assert.Equal(observation.Mp, state.Mp);
        Assert.Equal(7305, state.Hp.Value);
        Assert.Equal(DataSourceKind.Live, state.Hp.Source);
        Assert.Equal(1420, state.Mp.Value);
        Assert.Equal(DataSourceKind.Cached, state.Mp.Source);
    }

    [Fact]
    public async Task Unobserved_provider_keeps_the_reason_and_does_not_invent_zero()
    {
        const string reason = "gameplay_provider_not_available";
        var observer = new NetworkWorldStateObserver(
            new StubProvider(() => GameplayObservation.Unobserved(reason)));

        ObservedState state = await observer.ObserveAsync();

        Assert.False(state.Hp.HasValue);
        Assert.False(state.Mp.HasValue);
        Assert.Equal(DataSourceKind.Unknown, state.Hp.Source);
        Assert.Equal(DataSourceKind.Unknown, state.Mp.Source);
        Assert.Equal(reason, state.Hp.FailureReason);
        Assert.Equal(reason, state.Mp.FailureReason);
        Assert.NotEqual(0, state.Hp.HasValue ? state.Hp.Value : -1);
    }

    [Fact]
    public async Task Provider_exception_is_not_swallowed()
    {
        var observer = new NetworkWorldStateObserver(
            new StubProvider(() => throw new InvalidOperationException("feed_fault")));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => observer.ObserveAsync());
        Assert.Equal("feed_fault", ex.Message);
    }

    [Fact]
    public void Null_provider_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => new NetworkWorldStateObserver(null!));
    }

    private sealed class StubProvider : IGameplayProvider
    {
        private readonly Func<GameplayObservation> _read;

        public StubProvider(Func<GameplayObservation> read) => _read = read;

        public string Name => "stub";

        public GameplayObservation Observe() => _read();
    }
}
