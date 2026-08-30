using NosAi.Runtime.LowLevel;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Keyboard and mouse actuation: the authorization boundary, key resolution and
/// trajectory planning.
/// </summary>
/// <remarks>
/// The named checks live in <see cref="InputControlTestRunner"/> so the operator
/// command (<c>--input-test</c>) and CI certify the same suite. Every check runs
/// against a recording backend, so CI never moves the real mouse.
/// </remarks>
public sealed class InputControlTests
{
    [Fact]
    public async Task InputControlSuitePasses()
    {
        Assert.True(await InputControlTestRunner.RunAllTestsAsync());
    }
}
