using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The game-traffic observation channel: scope enforcement, read-only invariant,
/// provenance and convergence into the world model.
/// </summary>
/// <remarks>
/// The named checks live in <see cref="NetworkObservationTestRunner"/> so the
/// operator command (<c>--netobserve-test</c>) and CI certify the same suite. No
/// check touches a real network: every packet is synthetic or replayed.
/// </remarks>
public sealed class NetworkObservationTests
{
    [Fact]
    public void NetworkObservationSuitePasses()
        => Assert.True(NetworkObservationTestRunner.RunAll());
}
