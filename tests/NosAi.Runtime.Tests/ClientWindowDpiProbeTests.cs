using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The runtime process has to declare per-monitor v2, otherwise
/// <c>GetClientRect</c> virtualises the client area on any display not at 100%.
/// </summary>
public sealed class ClientWindowDpiProbeTests
{
    [Fact]
    public void RuntimeManifestDeclaresPerMonitorV2()
    {
        string root = RepositoryRoot();
        string manifest = File.ReadAllText(Path.Combine(root, "src", "NosAi.Runtime", "app.manifest"));
        string csproj = File.ReadAllText(Path.Combine(root, "src", "NosAi.Runtime", "NosAi.Runtime.csproj"));

        Assert.Contains("true/pm", manifest, StringComparison.Ordinal);
        Assert.Contains(">PerMonitorV2</dpiAwareness>", manifest, StringComparison.Ordinal);
        Assert.Contains("<ApplicationManifest>app.manifest</ApplicationManifest>", csproj, StringComparison.Ordinal);
    }

    /// <summary>
    /// Unknown is not unaware. Kept from the probe's own naming table, which moved
    /// into <see cref="DpiAwareness"/> when the calibration started recording the
    /// regime: two copies of that reading could disagree about the thing a refusal
    /// now depends on. The property being pinned is the same one — a regime that
    /// could not be identified must not read as any regime in particular, least of
    /// all as the one that would let a calibration through.
    /// </summary>
    [Theory]
    [InlineData("unaware", DpiAwarenessRegime.Unaware)]
    [InlineData("system", DpiAwarenessRegime.System)]
    [InlineData("permonitor", DpiAwarenessRegime.PerMonitor)]
    [InlineData("permonitorv2", DpiAwarenessRegime.PerMonitorV2)]
    [InlineData("unaware-gdi-scaled", DpiAwarenessRegime.UnawareGdiScaled)]
    [InlineData("something-a-later-build-writes", DpiAwarenessRegime.Unknown)]
    [InlineData("", DpiAwarenessRegime.Unknown)]
    [InlineData(null, DpiAwarenessRegime.Unknown)]
    public void AnUnrecognisedRegimeTokenReadsAsUnknownAndNeverAsUnaware(string? token, DpiAwarenessRegime expected)
        => Assert.Equal(expected, DpiAwareness.FromWire(token));

    [Fact]
    public void EveryRegimeSurvivesTheRoundTripToItsWireForm()
    {
        foreach (DpiAwarenessRegime regime in Enum.GetValues<DpiAwarenessRegime>())
            Assert.Equal(regime, DpiAwareness.FromWire(regime.ToWire()));
    }

    /// <summary>
    /// The regime is read, never assumed. On the operator's machine the same build
    /// answers PerMonitor under <c>dotnet exec</c> and PerMonitorV2 from the apphost,
    /// so whatever this returns here, it has to be a reading.
    /// </summary>
    [Fact]
    public void TheCurrentRegimeIsReadableAndIsNotDefaultedToUnaware()
    {
        DpiAwarenessRegime regime = DpiAwareness.Current();

        if (OperatingSystem.IsWindows())
            Assert.NotEqual(DpiAwarenessRegime.Unknown, regime);

        Assert.Equal(regime, DpiAwareness.FromWire(regime.ToWire()));
    }

    [Fact]
    public void ProbeRefusesOffWindows()
    {
        if (OperatingSystem.IsWindows())
            return;

        Assert.Equal(2, NosAi.Runtime.Perception.ClientWindowDpiProbe.Run());
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        Assert.True(directory is not null, "Repository root not found: no NosAi.sln above the test assembly.");
        return directory!.FullName;
    }
}
