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

    [Fact]
    public void OsSessionBaselineIsLiveAndGameplayStaysUnknown()
    {
        var client = new NosAi.LiveIntegration.ClientBaselineSnapshot(
            ProcessDetected: true,
            WindowDetected: true,
            ClientAttached: true,
            ProcessId: 4242,
            WindowHandle: (nint)0xABC,
            Source: "live_process_attach",
            ObservedAtUtc: DateTime.UtcNow,
            Availability: NosAi.LiveIntegration.ClientBaselineAvailability.BaselineReady,
            Status: "attached_os_session",
            Warning: "Gameplay fields remain UNKNOWN: no gameplay provider is bound.",
            FailureReason: null,
            ProcessName: "NostaleClientX",
            WindowTitle: "NosTale",
            ProcessResponding: true,
            WindowVisible: true);
        var hardware = new LiveHardwareTelemetry(new FallbackHardwareProbe()).Capture().View;
        var snapshot = Gate1SnapshotFactory.Create(
            RuntimeHealthStatus.Healthy,
            "test",
            hardware,
            client,
            new Gate1ConnectionSnapshot(string.Empty, false, false, default, null),
            NosAi.Runtime.Safety.RuntimeSafetyPolicy.SafeDefault);

        Assert.Equal(DataSourceKind.Live, snapshot.Client.ProcessName.Source);
        Assert.Equal("NostaleClientX", snapshot.Client.ProcessName.Value);
        Assert.Equal("NosTale", snapshot.Client.WindowTitle.Value);
        Assert.Equal("0xABC", snapshot.Client.WindowHandle.Value);
        Assert.Equal(DataSourceKind.Unknown, snapshot.Client.GameplayBaseline.Source);
        Assert.False(snapshot.Client.GameplayBaseline.HasValue);

        using var document = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(snapshot.ToWire()));
        AssertUnknownFieldsHaveNullValue(document.RootElement);
    }

    private static void AssertUnknownFieldsHaveNullValue(System.Text.Json.JsonElement element)
    {
        if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            if (element.TryGetProperty("source", out var source)
                && element.TryGetProperty("value", out var value)
                && source.GetString() == "UNKNOWN")
            {
                Assert.True(
                    value.ValueKind is System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined,
                    $"UNKNOWN field published {value}");
            }

            foreach (var property in element.EnumerateObject())
                AssertUnknownFieldsHaveNullValue(property.Value);
        }
        else if (element.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                AssertUnknownFieldsHaveNullValue(item);
        }
    }
}
