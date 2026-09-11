using System;
using System.IO;
using Xunit;

namespace NosAi.ControlPanel.Tests;

public class TargetRoiPanelTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        Assert.True(directory is not null, "Repository root not found: no NosAi.sln above the test assembly.");
        return directory!.FullName;
    }

    private static string XamlPath()
    {
        return Path.Combine(RepositoryRoot(), "src", "NosAi.ControlPanel", "MainWindow.xaml");
    }

    [Fact]
    public void The_card_lists_the_five_steps_in_order()
    {
        string xaml = File.ReadAllText(XamlPath());

        // Scoped to the card, not to the file. The markers are ordinary text: any
        // number followed by a bracket anywhere else in the window -- a range like
        // "1..254" ends in "4)" -- used to be picked up as this card's step 4 and
        // failed the assertion for a reason that had nothing to do with the card.
        int cardStart = xaml.IndexOf("T-09 \u2014 CALIBRA IL RIQUADRO BERSAGLIO", StringComparison.Ordinal);
        Assert.True(cardStart > 0, "The T-09 card is not in the window.");
        int cardEnd = xaml.IndexOf("</Border>", cardStart, StringComparison.Ordinal);
        Assert.True(cardEnd > cardStart, "The T-09 card is not closed.");
        string card = xaml[cardStart..cardEnd];

        string[] markers = { "1)", "2)", "3)", "4)", "5)" };
        int previousIndex = -1;
        foreach (string marker in markers)
        {
            int index = card.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(index > previousIndex, $"Step marker '{marker}' not found after index {previousIndex}.");
            previousIndex = index;
        }

        Assert.True(card.IndexOf("frazioni", StringComparison.Ordinal) >= 0,
            "The card does not mention 'frazioni'.");
    }

    [Fact]
    public void The_card_declares_every_control_the_handlers_use()
    {
        string xaml = File.ReadAllText(XamlPath());

        string[] required =
        {
            "TargetCropImage",
            "TargetCropInfo",
            "TargetRoiX",
            "TargetRoiY",
            "TargetRoiW",
            "TargetRoiH",
            "TargetRoiSummary",
            "OnCaptureTargetCrop",
            "OnRegisterTargetRoi",
            "OnRereadTargetState",
        };

        foreach (string name in required)
        {
            Assert.True(xaml.Contains(name, StringComparison.Ordinal),
                $"MainWindow.xaml does not contain '{name}'.");
        }
    }
}
