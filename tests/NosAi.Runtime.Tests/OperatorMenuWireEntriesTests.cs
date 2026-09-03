using NosAi.Runtime.Operator;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The two entries that record the wire and calibrate HP/MP from it.
/// </summary>
/// <remarks>
/// The switch in <c>Run</c> and the list in <c>Draw</c> are two separate blocks,
/// not generated from one table. An entry that exists in only one of them is
/// either invisible or unreachable, and that is the shape a missing command has
/// when the operator needs it most.
/// </remarks>
public sealed class OperatorMenuWireEntriesTests
{
    /// <summary>
    /// Silence is the one case that may take a rest value. Treating a blank line
    /// as a number would hide that the operator did not choose a duration.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyDurationLineTakesTheCommandRestValue(string? typed)
    {
        Assert.True(OperatorMenu.TryReadOptionalDurationSeconds(typed, out int? seconds));
        Assert.Null(seconds);
    }

    /// <summary>
    /// A typed integer is the duration they asked for, including zero: the
    /// recorder's own rest is zero (until Ctrl+C), and inventing a floor here
    /// would refuse the value the command already accepts.
    /// </summary>
    [Theory]
    [InlineData("0", 0)]
    [InlineData("20", 20)]
    [InlineData(" 45 ", 45)]
    public void ATypedDurationIsTheNumberTheOperatorWrote(string typed, int expected)
    {
        Assert.True(OperatorMenu.TryReadOptionalDurationSeconds(typed, out int? seconds));
        Assert.Equal(expected, seconds);
    }

    /// <summary>
    /// A typo is not silence. Replacing it with the rest value would run a
    /// capture the operator never asked for, under a duration they did not type.
    /// </summary>
    [Theory]
    [InlineData("x")]
    [InlineData("10s")]
    [InlineData("1.5")]
    public void ALineThatIsNotAnIntegerIsNotSilentlyReplacedByTheRest(string typed)
    {
        Assert.False(OperatorMenu.TryReadOptionalDurationSeconds(typed, out int? seconds));
        Assert.Null(seconds);
    }

    /// <summary>
    /// Both halves of the menu must name both entries. The switch without the
    /// list is a command nobody can see; the list without the switch is a
    /// caption that does nothing.
    /// </summary>
    [Fact]
    public void BothEntriesExistInTheSwitchAndInTheDrawnList()
    {
        string source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "NosAi.Runtime", "Operator", "OperatorMenu.cs"));

        int runAt = source.IndexOf("public static int Run()", StringComparison.Ordinal);
        int drawAt = source.IndexOf("private static void Draw()", StringComparison.Ordinal);
        int drawEnd = source.IndexOf("private static string DescribeHunt(", StringComparison.Ordinal);
        Assert.True(runAt >= 0 && drawAt > runAt && drawEnd > drawAt,
            "Run() must precede Draw(), and Draw() must be a closed block.");

        string runBlock = source[runAt..drawAt];
        string drawBlock = source[drawAt..drawEnd];

        Assert.Contains("case \"20\":", runBlock, StringComparison.Ordinal);
        Assert.Contains("case \"21\":", runBlock, StringComparison.Ordinal);
        Assert.Contains("RunWireRecord", runBlock, StringComparison.Ordinal);
        Assert.Contains("RunCalibrateVitals", runBlock, StringComparison.Ordinal);
        Assert.Contains("Registra il filo", runBlock, StringComparison.Ordinal);
        Assert.Contains("Calibra HP e MP dal filo", runBlock, StringComparison.Ordinal);
        Assert.Contains("WireRecorder.Run", source, StringComparison.Ordinal);
        Assert.Contains("PlayerVitalsCalibrator.Run", source, StringComparison.Ordinal);

        Assert.Contains(" 20  Registra il filo", drawBlock, StringComparison.Ordinal);
        Assert.Contains(" 21  Calibra HP e MP dal filo", drawBlock, StringComparison.Ordinal);

        Assert.DoesNotContain("case \"20\":", drawBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("case \"21\":", drawBlock, StringComparison.Ordinal);
        Assert.DoesNotContain(" 20  Registra il filo", runBlock, StringComparison.Ordinal);
        Assert.DoesNotContain(" 21  Calibra HP e MP dal filo", runBlock, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("NosAi.sln not found above the test output.");
    }
}
