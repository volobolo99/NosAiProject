using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Gate6;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Gate 6 — end-to-end integration checks over the canonical components.
/// </summary>
/// <remarks>
/// The named checks live in <see cref="Gate6ReleaseCertifier"/> so the operator
/// command (<c>--gate6-test</c>) and CI certify the same suite.
/// </remarks>
public sealed class Gate6Tests
{
    [Fact]
    public async Task Gate6SuitePasses()
    {
        Assert.True(await Gate6ReleaseCertifier.RunFullReleaseCertificationAsync());
    }
}
