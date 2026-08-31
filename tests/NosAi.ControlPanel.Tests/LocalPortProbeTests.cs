using Xunit;

namespace NosAi.ControlPanel.Tests;

public sealed class LocalPortProbeTests
{
    [Fact]
    public void Closed_or_invalid_port_is_not_open()
    {
        Assert.False(LocalPortProbe.CanConnect(0));
        Assert.False(LocalPortProbe.CanConnect(65536));
        Assert.False(LocalPortProbe.CanConnect(1, timeoutMs: 50));
    }
}
