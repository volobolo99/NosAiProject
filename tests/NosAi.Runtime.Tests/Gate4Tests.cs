using NosAi.Runtime.Gate4;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Gate 4 — Progression Engine V2, quest DAG and knowledge base.
/// </summary>
/// <remarks>
/// The named checks live in <see cref="Gate4TestRunner"/> so the operator command
/// (<c>--gate4-test</c>) and CI certify the same suite.
/// </remarks>
public sealed class Gate4Tests
{
    [Fact]
    public async Task Gate4SuitePasses()
    {
        Assert.True(await Gate4TestRunner.RunAllTestsAsync());
    }
}
