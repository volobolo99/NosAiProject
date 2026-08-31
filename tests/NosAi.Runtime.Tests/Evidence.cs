using NosAi.Runtime.Contracts;
using NosAi.Runtime.Testing;
using Xunit.Abstractions;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Lets an xUnit test publish what it observed to the operator's test page.
/// </summary>
/// <remarks>
/// <para>
/// xUnit does not capture <c>Console</c>, so <see cref="TestEvidenceProtocol.Emit"/>
/// writes into a void from here. What the VSTest adapter does put in the TRX
/// report is whatever a test writes through <see cref="ITestOutputHelper"/>, so
/// that is the channel used.
/// </para>
/// <para>
/// To publish from a test, take an <see cref="ITestOutputHelper"/> in the class
/// constructor and call <c>Evidence.Live(output, "key", value)</c>. Tests that
/// publish nothing are shown as having published nothing — never as having
/// observed something they did not.
/// </para>
/// </remarks>
public static class Evidence
{
    /// <summary>A value the test genuinely observed.</summary>
    public static void Live(ITestOutputHelper output, string key, object? value, string? note = null) =>
        output.WriteLine(TestEvidenceProtocol.Format(key, value, DataSourceKind.Live, note));

    /// <summary>A value computed from observed values.</summary>
    public static void Derived(ITestOutputHelper output, string key, object? value, string? note = null) =>
        output.WriteLine(TestEvidenceProtocol.Format(key, value, DataSourceKind.Derived, note));

    /// <summary>A value from a fixture or simulation, never to be read as live.</summary>
    public static void Simulated(ITestOutputHelper output, string key, object? value, string? note = null) =>
        output.WriteLine(TestEvidenceProtocol.Format(key, value, DataSourceKind.Simulated, note));

    /// <summary>Something the test could not determine, with why.</summary>
    public static void Unknown(ITestOutputHelper output, string key, string reason) =>
        output.WriteLine(TestEvidenceProtocol.Format(key, "UNKNOWN", DataSourceKind.Unknown, reason));
}
