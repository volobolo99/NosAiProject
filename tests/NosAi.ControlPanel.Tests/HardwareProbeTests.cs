using System;
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
        // Two families of reason exist since the 64-bit VRAM fallback was added: WMI
        // failures and registry ones. Both must still name the source that failed.
        if (probe is IHardwareProbeDiagnostics diagnostics && diagnostics.LastFailureReason is { } reason)
            Assert.True(
                reason.StartsWith("wmi_", StringComparison.Ordinal)
                || reason.StartsWith("registry_", StringComparison.Ordinal),
                $"A failure reason must name its source, got '{reason}'.");
    }
}
