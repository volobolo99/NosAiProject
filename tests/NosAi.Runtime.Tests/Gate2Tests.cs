using NosAi.Runtime.Gate2;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Gate 2 — canonical world model, bounded bus and the WAL SQLite store.
/// </summary>
/// <remarks>
/// The named checks live in <see cref="Gate2TestRunner"/> so the operator command
/// (<c>--gate2-test</c>) and CI certify the same suite; this hook makes a Gate 2
/// regression fail the build instead of only the operator run.
/// </remarks>
public sealed class Gate2Tests
{
    [Fact]
    public async Task Gate2SuitePasses()
    {
        Assert.True(await Gate2TestRunner.RunAllTestsAsync());
    }
}
