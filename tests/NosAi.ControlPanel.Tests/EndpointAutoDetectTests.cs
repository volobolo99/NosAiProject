using System;
using Xunit;

namespace NosAi.ControlPanel.Tests;

public sealed class EndpointAutoDetectTests
{
    [Fact]
    public void Detection_runs_when_the_client_appears_and_the_box_is_empty()
    {
        Assert.True(EndpointAutoDetect.ShouldDetect(
            true,
            currentPid: 1234,
            lastDetectedForPid: null,
            currentText: "",
            origin: EndpointOrigin.None));
    }

    [Fact]
    public void A_value_typed_by_the_operator_is_never_overwritten()
    {
        // Even a brand new process id must not let the automatism write over a
        // hand-typed endpoint: the typed value stays until the operator edits it.
        Assert.False(EndpointAutoDetect.ShouldDetect(
            true,
            currentPid: 9999,
            lastDetectedForPid: 1234,
            currentText: "203.0.113.10:4002",
            origin: EndpointOrigin.TypedByOperator));
        Assert.False(EndpointAutoDetect.ShouldDetect(
            true,
            currentPid: 9999,
            lastDetectedForPid: 1234,
            currentText: "",
            origin: EndpointOrigin.TypedByOperator));
    }

    [Fact]
    public void Detection_does_not_repeat_for_the_same_client()
    {
        Assert.False(EndpointAutoDetect.ShouldDetect(
            true,
            currentPid: 1234,
            lastDetectedForPid: 1234,
            currentText: "203.0.113.10:4002",
            origin: EndpointOrigin.Detected));
    }

    [Fact]
    public void A_new_client_pid_triggers_a_fresh_detection()
    {
        Assert.True(EndpointAutoDetect.ShouldDetect(
            true,
            currentPid: 9999,
            lastDetectedForPid: 1234,
            currentText: "203.0.113.10:4002",
            origin: EndpointOrigin.Detected));
    }

    [Fact]
    public void Without_a_client_nothing_is_detected()
    {
        Assert.False(EndpointAutoDetect.ShouldDetect(
            false,
            currentPid: null,
            lastDetectedForPid: null,
            currentText: "",
            origin: EndpointOrigin.None));
        Assert.False(EndpointAutoDetect.ShouldDetect(
            false,
            currentPid: null,
            lastDetectedForPid: 1234,
            currentText: "203.0.113.10:4002",
            origin: EndpointOrigin.Detected));
    }

    [Fact]
    public void The_provenance_line_names_the_reason_when_the_client_is_absent()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        string line = EndpointAutoDetect.DescribeOrigin(
            EndpointOrigin.None,
            endpoint: "",
            whenUtc: null,
            nowUtc: now,
            clientFailureReason: "process_not_attached");

        Assert.False(string.IsNullOrWhiteSpace(line));
        Assert.Contains("process_not_attached", line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(5, "5 secondi fa")]
    [InlineData(180, "3 minuti fa")]
    [InlineData(7200, "2 ore fa")]
    public void A_detected_endpoint_says_how_long_ago(int ageSeconds, string expectedAge)
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        string line = EndpointAutoDetect.DescribeOrigin(
            EndpointOrigin.Detected,
            endpoint: "203.0.113.10:4002",
            whenUtc: now.AddSeconds(-ageSeconds),
            nowUtc: now,
            clientFailureReason: null);

        Assert.Contains("203.0.113.10:4002", line, StringComparison.Ordinal);
        Assert.Contains(expectedAge, line, StringComparison.Ordinal);
    }
}
