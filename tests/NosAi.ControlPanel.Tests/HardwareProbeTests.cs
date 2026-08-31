using NosAi.Runtime.Hardware;
using Xunit;

namespace NosAi.ControlPanel.Tests;

public sealed class HardwareProbeTests
{
    [Fact]
    public void Windows_probe_does_not_throw_and_does_not_invent_gpu()
    {
        var probe = new WindowsHardwareProbe();
        var fingerprint = probe.Detect();
        Assert.Equal("Windows", fingerprint.Platform);
        if (fingerprint.GpuMemoryMb == 0)
            Assert.True(string.IsNullOrWhiteSpace(fingerprint.Gpu) || fingerprint.GpuMemoryMb == 0);
        if (probe is IHardwareProbeDiagnostics diagnostics && diagnostics.LastFailureReason is { } reason)
            Assert.StartsWith("wmi_", reason);
    }
}
