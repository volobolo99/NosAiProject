using Xunit;

namespace NosAi.ControlPanel.Tests;

public sealed class OperatorSettingsTests
{
    [Fact]
    public void Valid_defaults_pass()
    {
        Assert.True(OperatorSettings.TryValidate(8766, 17471, 5000, "NostaleClientX", out var error));
        Assert.Equal("", error);
    }

    [Theory]
    [InlineData(-1, 17471, 5000, "x")]
    [InlineData(8766, 70000, 5000, "x")]
    [InlineData(8766, 17471, 50, "x")]
    [InlineData(8766, 17471, 5000, "")]
    public void Out_of_range_is_rejected(int dashboard, int guard, int timeout, string process)
    {
        Assert.False(OperatorSettings.TryValidate(dashboard, guard, timeout, process, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Decision_loop_settings_reach_host_options()
    {
        var settings = new OperatorSettings
        {
            RunDecisionLoop = true,
            DecisionIntervalMs = 750
        };

        var options = settings.ToHostOptions();
        Assert.True(options.RunDecisionLoop);
        Assert.Equal(750, options.DecisionIntervalMs);
    }

    [Fact]
    public void Decision_interval_out_of_range_is_rejected()
    {
        Assert.False(OperatorSettings.TryValidate(8766, 17471, 5000, "NostaleClientX", null, 10, out var error));
        Assert.Contains("50", error);
        Assert.Contains("60000", error);
    }
}
