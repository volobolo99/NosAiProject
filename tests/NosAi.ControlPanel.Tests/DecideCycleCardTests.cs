using System;
using Xunit;

namespace NosAi.ControlPanel.Tests;

public class DecideCycleCardTests
{
    [Fact]
    public void The_expectation_names_execution_disabled_as_expected_not_as_a_fault()
    {
        var expectation = DecideCycleCard.DescribeExpectation();

        Assert.Contains("ExecutionDisabled", expectation);
        Assert.Contains("NoWorldState", expectation);
        Assert.Contains("atteso", expectation);
        Assert.Contains("guasto", expectation);
    }

    [Theory]
    [InlineData("", "5000", "endpoint")]
    [InlineData("localhost", "5000", "endpoint")]
    [InlineData("localhost:70000", "5000", "endpoint")]
    [InlineData("localhost:1234", "abc", "intervallo")]
    [InlineData("localhost:1234", "499", "intervallo")]
    [InlineData("localhost:1234", "60001", "intervallo")]
    public void Start_validation_names_the_guilty_field(string endpoint, string intervalMs, string guiltyField)
    {
        var valid = DecideCycleCard.TryValidateStart(endpoint, intervalMs, out _, out string? refusal);

        Assert.False(valid);
        Assert.NotNull(refusal);
        Assert.Contains(guiltyField.ToLowerInvariant(), refusal!.ToLowerInvariant());
    }

    [Fact]
    public void Valid_start_arguments_pass()
    {
        var valid = DecideCycleCard.TryValidateStart("127.0.0.1:1234", "5000", out int interval, out string? refusal);

        Assert.True(valid);
        Assert.Equal(5000, interval);
        Assert.Null(refusal);
    }

    [Fact]
    public void The_four_line_kinds_are_not_collapsed()
    {
        var noWorldState = DecideCycleCard.ClassifyDecideLine("state=NoWorldState waiting for observation");
        var attached = DecideCycleCard.ClassifyDecideLine("Game observation attached. endpoint=127.0.0.1:1234 source=LIVE");
        var executionDisabled = DecideCycleCard.ClassifyDecideLine("SafetyGate decision=ExecutionDisabled effector=DisabledActionEffector");
        var other = DecideCycleCard.ClassifyDecideLine("Gate 1 bootstrap starting.");

        Assert.NotEqual(noWorldState, attached);
        Assert.NotEqual(noWorldState, executionDisabled);
        Assert.NotEqual(noWorldState, other);
        Assert.NotEqual(attached, executionDisabled);
        Assert.NotEqual(attached, other);
        Assert.NotEqual(executionDisabled, other);
    }

    [Fact]
    public void A_run_with_no_lines_says_so()
    {
        var summary = DecideCycleCard.SummariseRun(0, 0, 0, 0, stoppedByOperator: false);

        Assert.Contains("Nessuna riga ricevuta", summary);
        Assert.DoesNotContain("NoWorldState", summary);
        Assert.DoesNotContain("ExecutionDisabled", summary);
    }
}
