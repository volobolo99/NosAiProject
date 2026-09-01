using System.Net;
using NosAi.ControlPanel;
using NosAi.LiveIntegration;
using Xunit;

namespace NosAi.ControlPanel.Tests;

public sealed class ObserveGameDetectorTests
{
    [Fact]
    public void Zero_remote_sessions_do_not_fill_the_box()
    {
        var observation = new ClientNetworkObservation(Array.Empty<ClientTcpConnection>(), Primary: null, FailureReason: null);

        bool filled = ObserveGameDetector.TrySuggest(observation, out var endpoint, out var status);

        Assert.False(filled);
        Assert.Equal("", endpoint);
        Assert.Contains("0 sessioni", status, StringComparison.Ordinal);
        Assert.Contains("invariata", status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Two_remote_sessions_do_not_pick_one()
    {
        ClientTcpConnection first = Remote("203.0.113.10", 4002);
        ClientTcpConnection second = Remote("203.0.113.20", 443);
        var observation = new ClientNetworkObservation([first, second], Primary: null, FailureReason: null);

        bool filled = ObserveGameDetector.TrySuggest(observation, out var endpoint, out var status);

        Assert.False(filled);
        Assert.Equal("", endpoint);
        Assert.Contains("2 sessioni", status, StringComparison.Ordinal);
        Assert.Contains("indovinare", status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("203.0.113.10", endpoint);
        Assert.DoesNotContain("203.0.113.20", endpoint);
    }

    private static ClientTcpConnection Remote(string host, int port) =>
        new(
            new IPEndPoint(IPAddress.Parse("192.0.2.10"), 50000),
            new IPEndPoint(IPAddress.Parse(host), port),
            ClientTcpState.Established);
}
