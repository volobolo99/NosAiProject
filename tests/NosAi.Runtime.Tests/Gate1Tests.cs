using NosAi.Host;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate1;
using NosAi.Runtime.Hardware;
using Xunit;

namespace NosAi.Runtime.Tests;

public sealed class Gate1Tests
{
    [Fact]
    public async Task Gate1SuitePasses()
    {
        Assert.True(await Gate1TestRunner.RunAllAsync());
    }

    [Fact]
    public void UnknownIsNotZero()
    {
        var unknown = ClassifiedValue<long>.Unknown("missing");
        Assert.Equal(DataSourceKind.Unknown, unknown.Source);
        Assert.False(unknown.HasValue);
        var json = System.Text.Json.JsonSerializer.Serialize(unknown.ToWire());
        Assert.Contains("\"source\":\"UNKNOWN\"", json);
        Assert.Contains("\"value\":null", json);
        Assert.DoesNotContain("\"value\":0", json);
    }

    [Fact]
    public void FallbackHardwareDoesNotPublishZeroRamAsLive()
    {
        var snapshot = new LiveHardwareTelemetry(new FallbackHardwareProbe()).Capture();
        Assert.Equal(DataSourceKind.Unknown, snapshot.View.SystemRamMb.Source);
        Assert.False(snapshot.View.SystemRamMb.HasValue);
        Assert.Equal(DataSourceKind.Unknown, snapshot.View.Cpu.Source);
    }

    [Fact]
    public async Task MasterHostTelemetryDoesNotClaimLiveGameplay()
    {
        await using var host = new NosAiMasterRuntimeHost(dashboardPort: 8799);
        var telemetry = host.CaptureCurrentTelemetry();
        Assert.Equal("UNKNOWN", telemetry.GameClientSource);
        Assert.Equal("UNKNOWN", telemetry.GuardPhoneSource);
        Assert.Equal("UNKNOWN", telemetry.ActiveMonstersSource);
        Assert.Equal("UNKNOWN", telemetry.TotalGoldSource);
        Assert.Equal("UNKNOWN", telemetry.GpuTemperatureSource);
        Assert.Null(telemetry.ActiveMonstersTracked);
        Assert.Null(telemetry.TotalGoldTracked);
        Assert.Null(telemetry.IsGameClientHooked);
        Assert.Equal("LIVE", telemetry.RamWorkingSetSource);
        Assert.True(telemetry.RamWorkingSetMb is > 0);
    }
}
