using Xunit;

namespace NosAi.Core.Tests;

/// <summary>
/// A fact whose assertion is a wall-clock budget, and which therefore runs only
/// where the operator has declared the machine quiesced.
/// </summary>
/// <remarks>
/// <para>
/// A wall-clock percentile measures the machine, not only the code under test.
/// The default <c>dotnet test -c Release</c> runs the three test assemblies as
/// concurrent processes, so a handshake timed inside that run is timed against
/// whatever <c>NosAi.Runtime.Tests</c> is doing on the other cores at that
/// instant. That is contention, not transport latency, and the difference is
/// measurable: the handshake budget test read 53 ms and 88 ms against its 25 ms
/// budget in full runs, while passing eight consecutive runs of
/// <c>NosAi.Core.Tests</c> on its own. No xUnit parallelism switch reaches this,
/// because the competing work is in another process.
/// </para>
/// <para>
/// The budget is not the part that was wrong, so the budget does not move. What
/// moves is when the measurement is allowed to claim anything: the timing runs
/// only where <see cref="QuiescedVariable"/> is set, and an operator setting it
/// is asserting that this assembly runs alone on an otherwise idle machine.
/// Everywhere else the test is skipped with that reason attached, and a skip is
/// never evidence that the budget was met.
/// </para>
/// </remarks>
public sealed class QuiescedMachineFactAttribute : FactAttribute
{
    /// <summary>
    /// Set to <c>1</c> or <c>true</c> by the operator to declare that this
    /// assembly runs alone on an idle machine. It records a claim, not a
    /// measurement: nothing here can verify the machine really is idle, which is
    /// why the gated tests report the shape of their samples on failure rather
    /// than a bare number.
    /// </summary>
    public const string QuiescedVariable = "NOSAI_QUIESCED_MACHINE";

    public QuiescedMachineFactAttribute()
    {
        if (!IsDeclaredQuiesced)
            Skip = $"Wall-clock budget: measures machine contention unless this assembly runs alone. " +
                   $"Set {QuiescedVariable}=1, then: " +
                   "dotnet test tests/NosAi.Core.Tests -c Release --filter \"Category=PerfBudget\". " +
                   "See docs/CERTIFICAZIONI/gate1.md S:5.";
    }

    /// <summary>Whether the operator has declared this run isolated.</summary>
    public static bool IsDeclaredQuiesced
    {
        get
        {
            string? declared = Environment.GetEnvironmentVariable(QuiescedVariable);
            return string.Equals(declared, "1", StringComparison.Ordinal)
                || string.Equals(declared, "true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
