using System.IO;
using Xunit;

namespace NosAi.ControlPanel.Tests;

/// <summary>
/// T-14 / Q-146: the Rete section's pre-login recording card. The panel is the
/// surface the operator runs tests from, so the card has to exist there — with
/// the gesture in the exact order, with the honest limit stated before the
/// operator presses, and with the run's report (file, packets, wait before the
/// attach) shown when it ends. These are text assertions on the produced XAML
/// and code-behind, never on a private method.
/// </summary>
public sealed class PreLoginPanelTests
{
    private static readonly string XamlPath =
        Path.Combine(RepositoryRoot(), "src", "NosAi.ControlPanel", "MainWindow.xaml");

    private static readonly string CodePath =
        Path.Combine(RepositoryRoot(), "src", "NosAi.ControlPanel", "MainWindow.xaml.cs");

    [Fact]
    public void TheReteSectionDescribesThePreLoginRecordingWithItsLimitAndShowsTheReport()
    {
        string xaml = File.ReadAllText(XamlPath);
        string code = File.ReadAllText(CodePath);

        // The card lives in the Rete section, between the network view and the
        // next section.
        int networkStart = xaml.IndexOf("x:Name=\"ViewNetwork\"", StringComparison.Ordinal);
        int nextSection = xaml.IndexOf("x:Name=\"ViewDecision\"", StringComparison.Ordinal);
        Assert.True(networkStart >= 0 && nextSection > networkStart,
            "the Rete section (ViewNetwork) must exist and precede the next section");
        string network = xaml[networkStart..nextSection];

        Assert.Contains("REGISTRA DAL LOGIN", network, StringComparison.Ordinal);

        // The gesture in the exact order the operator performs it.
        int step1 = network.IndexOf("1) Chiudi il client NosTale", StringComparison.Ordinal);
        int step2 = network.IndexOf("2) Premi", StringComparison.Ordinal);
        int step3 = network.IndexOf("3) Apri il client, accedi ed entra in gioco", StringComparison.Ordinal);
        int step4 = network.IndexOf("4) Lascia correre qualche secondo", StringComparison.Ordinal);
        Assert.True(step1 >= 0 && step2 > step1 && step3 > step2 && step4 > step3,
            "the instructions must list, in order: close the client, press, open and log in, let it run");

        // The honest limit, stated before the operator presses: packets exchanged
        // before the attach are not in the capture.
        Assert.Contains("prima dell'aggancio non sono catturati", network, StringComparison.Ordinal);

        // One button in the card, wired to the handler that starts the wait mode.
        Assert.Contains("Registra dal login", network, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnRecordPreLoginWire\"", network, StringComparison.Ordinal);

        // The handler launches --record-wire in its await-client mode and reads
        // back the wait and the packet line for the end-of-run report.
        Assert.Contains("OnRecordPreLoginWire", code, StringComparison.Ordinal);
        Assert.Contains("--record-wire --await-client", code, StringComparison.Ordinal);
        Assert.Contains("waited before attach:", code, StringComparison.Ordinal);
        Assert.Contains(" packets -> ", code, StringComparison.Ordinal);
        Assert.Contains("Cattura scritta: data/{stem}.noscap", code, StringComparison.Ordinal);
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
