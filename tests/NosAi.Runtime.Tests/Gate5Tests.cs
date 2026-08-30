using NosAi.Runtime.Gate5;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Gate 5 — provider router, hardware/storage baselines and the Eye AI view.
/// </summary>
/// <remarks>
/// The named checks live in <see cref="Gate5TestRunner"/> so the operator command
/// (<c>--gate5-test</c>) and CI certify the same suite.
/// </remarks>
public sealed class Gate5Tests
{
    [Fact]
    public async Task Gate5SuitePasses()
    {
        Assert.True(await Gate5TestRunner.RunAllTestsAsync());
    }
}
