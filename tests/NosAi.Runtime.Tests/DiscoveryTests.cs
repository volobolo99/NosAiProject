using NosAi.GuardClient;
using NosAi.Runtime.Gate1;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// LAN discovery: the responder in the runtime and the client in the app.
/// </summary>
/// <remarks>
/// Discovery decides nothing. These tests hold it to that: it must answer with a
/// reachable port, refuse to be confused with the authenticated session, and
/// survive arbitrary traffic on an open UDP port without throwing.
/// </remarks>
public sealed class DiscoveryTests
{
    [Fact]
    public void RequestAndResponseRoundTrip()
    {
        var request = DiscoveryProtocol.CreateRequest();
        Assert.True(DiscoveryProtocol.IsRequest(request));

        var response = DiscoveryProtocol.CreateResponse(17471, "NOSAI-PC");
        Assert.True(DiscoveryProtocol.TryReadResponse(response, out var port, out var host));
        Assert.Equal(17471, port);
        Assert.Equal("NOSAI-PC", host);
    }

    [Fact]
    public void ADiscoveryFrameIsNotASessionFrame()
    {
        // "NOSD" and "NOSA" differ in one byte. If a discovery datagram could be
        // read as a session frame, an unauthenticated announcement would be
        // arriving on a path that carries authorisation.
        //
        // The host name is long enough to push the frame past the 12-byte session
        // header, so the rejection comes from the magic rather than from the length:
        // a short frame would be refused either way and would prove less.
        var response = DiscoveryProtocol.CreateResponse(17471, "NOSAI-WORKSTATION");
        Assert.True(response.Length > WireHeader.HeaderSize);
        Assert.False(WireHeader.TryRead(response, out _, out var error));
        Assert.Equal("invalid_magic", error);

        // And a short one is still refused, just for a different reason.
        Assert.False(WireHeader.TryRead(DiscoveryProtocol.CreateResponse(17471, "PC"), out _, out var shortError));
        Assert.Equal("incomplete_header", shortError);
    }

    [Fact]
    public void AResponseIsNotMistakenForARequest()
    {
        var response = DiscoveryProtocol.CreateResponse(17471, "PC");
        Assert.False(DiscoveryProtocol.IsRequest(response));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x00 })]
    [InlineData(new byte[] { (byte)'N', (byte)'O', (byte)'S', (byte)'A', 1, 2, 0, 0, 0 })] // session magic
    [InlineData(new byte[] { (byte)'N', (byte)'O', (byte)'S', (byte)'D', 99, 2, 0, 1, 0 })] // wrong version
    public void GarbageOnTheDiscoveryPortIsRejectedQuietly(byte[] frame)
    {
        // Anything at all can arrive on an open UDP port, so parsing must return
        // false rather than throw: an exception here would end the responder.
        Assert.False(DiscoveryProtocol.TryReadResponse(frame, out _, out _));
        Assert.False(DiscoveryProtocol.IsRequest(frame));
    }

    [Fact]
    public void AZeroPortIsRefused()
    {
        // Port 0 is not reachable. Accepting it would hand the phone an endpoint it
        // can only fail to dial.
        Assert.Throws<ArgumentOutOfRangeException>(() => DiscoveryProtocol.CreateResponse(0, "PC"));

        var frame = DiscoveryProtocol.CreateResponse(17471, "PC");
        frame[6] = 0;
        frame[7] = 0;
        Assert.False(DiscoveryProtocol.TryReadResponse(frame, out _, out _));
    }

    [Fact]
    public void AnOverlongHostNameIsTruncatedNotRejected()
    {
        // The name is a label for the operator, never an authorisation, so a long
        // one is trimmed rather than made to fail a discovery that would otherwise work.
        var response = DiscoveryProtocol.CreateResponse(17471, new string('x', 500));
        Assert.True(DiscoveryProtocol.TryReadResponse(response, out var port, out var host));
        Assert.Equal(17471, port);
        Assert.Equal(DiscoveryProtocol.MaxHostNameBytes, host.Length);
    }

    [Fact]
    public async Task TheResponderAnswersARealProbeOnLoopback()
    {
        // Uses a private port so the test does not depend on, or disturb, a runtime
        // that may be listening on the real one.
        const int probePort = 47472;
        await using var responder = new DiscoveryResponder(guardPort: 17471, hostName: "TEST-PC", listenPort: probePort);
        Assert.True(responder.TryStart(out var failure), $"responder did not start: {failure}");

        var found = await RuntimeDiscovery.FindAllAsync(TimeSpan.FromSeconds(3), probePort);

        var match = found.FirstOrDefault(r => r.HostName == "TEST-PC");
        Assert.NotNull(match);
        Assert.Equal(17471, match!.GuardPort);
    }

    [Fact]
    public async Task ABusyDiscoveryPortDegradesDiscoveryOnly()
    {
        // Discovery is a convenience: losing it must not stop a runtime, because the
        // phone can always be reached over USB instead.
        const int probePort = 47473;
        await using var first = new DiscoveryResponder(17471, "FIRST", probePort);
        Assert.True(first.TryStart(out _));

        await using var second = new DiscoveryResponder(17471, "SECOND", probePort);
        Assert.False(second.TryStart(out var failure));
        Assert.StartsWith("discovery_port_in_use:", failure);
        Assert.False(second.IsListening);
    }

    [Fact]
    public async Task ScanningAnEmptyNetworkReturnsNothingRatherThanFailing()
    {
        var found = await RuntimeDiscovery.FindAllAsync(TimeSpan.FromMilliseconds(600), 47474);
        Assert.Empty(found);
    }
}
