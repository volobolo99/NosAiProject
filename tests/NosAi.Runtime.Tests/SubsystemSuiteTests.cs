using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The subsystem certification suites that were written and then never run.
/// </summary>
/// <remarks>
/// <para>
/// Each of these has its own <c>Program.Main</c>, made unreachable by the pinned
/// <c>StartupObject</c>, and until now no flag invoked any of them. So roughly
/// 2,400 lines of Storage, Navigation, Gateway, Raids, Miniland, local inference
/// and hardware autoscale shipped with certification suites that had never
/// executed once.
/// </para>
/// <para>
/// The named checks stay in the runners, so the operator flags and CI certify the
/// same suite. What these wrappers add is that a regression now fails the build
/// rather than waiting for somebody to remember a command nobody documented.
/// </para>
/// </remarks>
public sealed class SubsystemSuiteTests
{
    [Fact]
    public async Task StorageInfrastructureSuitePasses()
        => Assert.True(await NosAi.Storage.Infrastructure.StorageInfrastructureTestRunner.RunAllTestsAsync());

    [Fact]
    public async Task NavigationPathfindingSuitePasses()
        => Assert.True(await NosAi.Navigation.Pathfinding.NavigationPathfindingTestRunner.RunAllTestsAsync());

    [Fact]
    public async Task ControlPanelGatewaySuitePasses()
        => Assert.True(await NosAi.Network.Gateway.ControlPanelGatewayTestRunner.RunAllTestsAsync());

    [Fact]
    public async Task DodekatheonRaidSuitePasses()
        => Assert.True(await NosAi.Raids.Dodekatheon.DodekatheonRaidTestRunner.RunAllTestsAsync());

    [Fact]
    public async Task MinilandProductionSuitePasses()
        => Assert.True(await NosAi.Miniland.Production.MinilandProductionTestRunner.RunAllTestsAsync());

    [Fact]
    public async Task LocalAiInferenceSuitePasses()
        => Assert.True(await NosAi.AI.LocalInference.LocalAiInferenceTestRunner.RunAllTestsAsync());

    [Fact]
    public async Task HardwareAutoscaleSuitePasses()
        => Assert.True(await NosAi.Hardware.Autoscale.HardwareAutoscaleTestRunner.RunAllTestsAsync());
}
