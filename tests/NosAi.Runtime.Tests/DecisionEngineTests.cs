using NosAi.Runtime.AI.Decision;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The utility decision engine: unknown handling, ordering, provenance folding
/// and rule-file loading.
/// </summary>
/// <remarks>
/// The named checks live in <see cref="DecisionEngineTestRunner"/> so the
/// operator command (<c>--decision-test</c>) and CI certify the same suite.
/// </remarks>
public sealed class DecisionEngineTests
{
    [Fact]
    public async Task DecisionEngineSuitePasses()
        => Assert.True(await DecisionEngineTestRunner.RunAllTestsAsync());
}
