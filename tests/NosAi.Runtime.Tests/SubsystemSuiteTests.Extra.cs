using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Certification suites for the Perception, crypto and Economy subsystems.
/// </summary>
/// <remarks>
/// Kept in their own file (not in <see cref="SubsystemSuiteTests"/>) so the two
/// authoring streams do not collide. Each runner is synchronous, so these adapt it
/// to a fact directly; the named checks live in the runners, so the operator flags
/// (<c>--perception-test</c>, <c>--crypto-test</c>, <c>--economy-test</c>) and CI
/// certify the same suite.
/// </remarks>
public sealed class PerceptionCryptoEconomyTests
{
    [Fact]
    public void PerceptionPipelineSuitePasses()
        => Assert.True(NosAi.Runtime.Perception.PerceptionPipelineTestRunner.RunAll());

    [Fact]
    public void EphemeralSessionCryptoSuitePasses()
        => Assert.True(NosAi.Runtime.Security.EphemeralSessionTestRunner.RunAll());

    [Fact]
    public void InventoryEconomySuitePasses()
        => Assert.True(NosAi.Economy.Inventory.InventoryEconomyTestRunner.RunAll());
}
