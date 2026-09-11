using NosAi.Runtime.Testing;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Certification suite for the wire header fuzz harness (C-402). The named checks
/// live in <see cref="WireProtocolFuzzTestRunner"/>, so this Fact and the operator
/// flag (<c>--wire-fuzz-test</c>) certify the same suite.
/// </summary>
public sealed class WireProtocolFuzzTests
{
    [Fact]
    public void WireProtocolFuzzSuitePasses()
        => Assert.True(WireProtocolFuzzTestRunner.RunAll());
}
