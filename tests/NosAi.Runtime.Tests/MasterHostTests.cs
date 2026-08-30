using NosAi.Host;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Master Host — bootstrap, telemetry surface and supervisor checks.
/// </summary>
/// <remarks>
/// The named checks live in <see cref="MasterHostTestRunner"/> so the operator
/// command (<c>--host-test</c>) and CI certify the same suite.
/// </remarks>
public sealed class MasterHostTests
{
    [Fact]
    public async Task MasterHostSuitePasses()
    {
        Assert.True(await MasterHostTestRunner.RunAllTestsAsync());
    }
}
