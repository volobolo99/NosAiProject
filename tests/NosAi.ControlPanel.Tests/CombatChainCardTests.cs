using System;
using Xunit;

namespace NosAi.ControlPanel.Tests;

public class CombatChainCardTests
{
    [Fact]
    public void A_report_with_mobs_and_both_kinds_of_line_is_counted()
    {
        const string output =
            "mobs: 12 observed, 8 known hostile, 6 known alive, 5 positioned\n" +
            "engage: target=101 would_act=True violations=none\n" +
            "engage: target=102 would_act=True violations=none\n" +
            "engage: target=103 would_act=False violations=fuori portata\n" +
            "candidate: Basic target=101 allowed=True violations=none\n";

        var summary = CombatChainCard.SummariseCombatReport(output);

        Assert.Contains("mobs: 12 observed, 8 known hostile, 6 known alive, 5 positioned", summary);
        Assert.Contains("righe engage: 3", summary);
        Assert.Contains("righe candidate: 1", summary);
        Assert.Contains("engage would_act=True: 2", summary);
    }

    [Fact]
    public void A_report_without_the_mobs_line_says_so()
    {
        const string output =
            "engage: target=101 would_act=True violations=none\n" +
            "candidate: Basic target=101 allowed=True violations=none\n";

        var summary = CombatChainCard.SummariseCombatReport(output);

        Assert.Contains("mobs: non stampata", summary);
        Assert.DoesNotContain("mobs: 0", summary);
    }

    [Theory]
    [InlineData("   ", "skill.100", "10", "target")]
    [InlineData("42", "", "10", "skill")]
    [InlineData("42", "skill.100", "abc", "round")]
    [InlineData("42", "skill.100", "0", "round")]
    [InlineData("42", "skill.100", "61", "round")]
    public void Engage_validation_names_the_guilty_field(string target, string skill, string rounds, string guiltyField)
    {
        var valid = CombatChainCard.TryValidateEngage(target, skill, rounds, out _, out string? refusal);

        Assert.False(valid);
        Assert.NotNull(refusal);
        Assert.Contains(guiltyField.ToLowerInvariant(), refusal!.ToLowerInvariant());
    }

    [Fact]
    public void Valid_engage_arguments_pass()
    {
        var valid = CombatChainCard.TryValidateEngage("42", "skill.100", "30", out int rounds, out string? refusal);

        Assert.True(valid);
        Assert.Equal(30, rounds);
        Assert.Null(refusal);
    }

    [Fact]
    public void A_refusal_and_a_press_are_not_the_same_case()
    {
        var refusal = CombatChainCard.ClassifyEngageLine("[REFUSED] engage: target=42 skill=100 would_act=False violations=fuori portata");
        var action = CombatChainCard.ClassifyEngageLine("engage: target=42 skill=100 would_act=True violations=none");

        Assert.Equal(CombatChainCard.EngageLineKind.Refused, refusal);
        Assert.Equal(CombatChainCard.EngageLineKind.Action, action);
        Assert.NotEqual(refusal, action);
    }
}
