using System.Net;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;
using NosAi.Runtime.Security;
using Xunit;
using Xunit.Abstractions;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The real capture backend behind the perception channel's observation source.
/// </summary>
/// <remarks>
/// The perception channel declares <c>IRawScopedCaptureBackend</c> and ships no
/// implementation because a real capture needs a driver. These tests cover the
/// implementation that fills it: who may open one, what it labels its packets, and
/// what it refuses to do when it cannot capture.
/// </remarks>
public sealed class ScopedLiveCaptureBackendTests
{
    private readonly ITestOutputHelper _output;

    public ScopedLiveCaptureBackendTests(ITestOutputHelper output) => _output = output;

    private static readonly IPAddress Server = IPAddress.Parse("203.0.113.10");
    private const int ServerPort = 4012;

    /// <summary>Builds an IPv4/TCP packet carrying a payload, for the parser to read.</summary>
    private static byte[] Packet(IPAddress from, IPAddress to, int fromPort, int toPort, byte[] payload)
    {
        var buffer = new byte[20 + 20 + payload.Length];
        buffer[0] = 0x45;                                     // IPv4, 5-word header
        buffer[9] = 6;                                        // TCP
        BitConverter.GetBytes((ushort)buffer.Length).CopyTo(buffer, 2);
        from.GetAddressBytes().CopyTo(buffer, 12);
        to.GetAddressBytes().CopyTo(buffer, 16);

        buffer[20] = (byte)(fromPort >> 8);
        buffer[21] = (byte)fromPort;
        buffer[22] = (byte)(toPort >> 8);
        buffer[23] = (byte)toPort;
        buffer[32] = 0x50;                                    // data offset 5 words
        buffer[33] = 0x18;                                    // PSH | ACK
        payload.CopyTo(buffer, 40);
        return buffer;
    }

    private static InMemoryPacketSource SourceOf(params byte[][] packets) =>
        new(Server, ServerPort, packets.Select(p => new CapturedPacket(DateTime.UtcNow, p)).ToArray());

    // --------------------------------------------------------- who may capture

    [Theory]
    [InlineData(SecurityPrincipal.GuardDevice)]
    [InlineData(SecurityPrincipal.AutonomousAgent)]
    [InlineData(SecurityPrincipal.Unknown)]
    public void APrincipalWithoutTheCapabilityCannotOpenACapture(SecurityPrincipal principal)
    {
        // The phone asks; it does not capture. Reading the game's traffic is a
        // privileged path, gated like reading its memory.
        using ScopedLiveCaptureBackend? backend =
            ScopedLiveCaptureBackend.TryOpen(Server, ServerPort, principal, out string? reason);

        Assert.Null(backend);
        Assert.StartsWith("not_authorized:", reason);
    }

    [Theory]
    [InlineData(SecurityPrincipal.Operator)]
    [InlineData(SecurityPrincipal.Subsystem)]
    public void TheOperatorAndTheCaptureSubsystemHoldTheCapability(SecurityPrincipal principal)
    {
        // Subsystem is deliberately allowed: the capture engine is itself a
        // subsystem, and refusing it here would make the channel unusable by the
        // very component that exists to run it. This test states that on purpose --
        // an earlier version of it assumed the opposite and was wrong.
        AuthorizationDecision decision = new Gate1AuthorizationPolicy().Evaluate(
            principal, RuntimeCapability.ReadGameTraffic, TrustTier.Tier1_Assisted, TrustTier.Tier4_FullAutonomous);

        Assert.True(decision.Allowed);

        // Authorised, so the refusal that follows can only be about the driver.
        using ScopedLiveCaptureBackend? backend =
            ScopedLiveCaptureBackend.TryOpen(Server, ServerPort, principal, out string? reason);
        if (backend is null)
            Assert.DoesNotContain("not_authorized", reason);
    }

    [Fact]
    public void AuthorizationIsCheckedBeforeAnyDriverHandleIsSought()
    {
        // An unauthorised caller must not even learn whether the driver is present.
        using ScopedLiveCaptureBackend? backend = ScopedLiveCaptureBackend.TryOpen(
            Server, ServerPort, SecurityPrincipal.GuardDevice, out string? reason);

        Assert.Null(backend);
        Assert.DoesNotContain("windivert", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnImpossiblePortIsRefused()
    {
        using ScopedLiveCaptureBackend? backend = ScopedLiveCaptureBackend.TryOpen(
            Server, 0, SecurityPrincipal.Operator, out string? reason);

        Assert.Null(backend);
        Assert.Equal("invalid_port:0", reason);
    }

    [Fact]
    public void WithoutTheDriverItSaysSoRatherThanInventingTraffic()
    {
        // The driver is not installed on this machine yet. The honest outcome is a
        // named refusal; a synthetic fallback here would feed the world model
        // invented bytes wearing a LIVE label.
        using ScopedLiveCaptureBackend? backend = ScopedLiveCaptureBackend.TryOpen(
            Server, ServerPort, SecurityPrincipal.Operator, out string? reason);

        if (backend is null)
        {
            Evidence.Unknown(_output, "catturaReale", reason ?? "senza motivo");
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }
        else
        {
            Evidence.Live(_output, "catturaReale", $"aperta su {backend.Endpoint.Host}:{backend.Endpoint.Port}");
            Assert.True(backend.IsCapturing);
        }
    }

    // ------------------------------------------------------- what it observes

    [Fact]
    public void APacketFromTheServerIsObservedAsInboundAndLive()
    {
        byte[] payload = { 1, 2, 3, 4 };
        using var backend = ScopedLiveCaptureBackend.OverSource(
            SourceOf(Packet(Server, IPAddress.Parse("192.168.1.5"), ServerPort, 51000, payload)),
            Server, ServerPort);

        Assert.True(backend.TryObserve(out ObservedPacket packet));
        Assert.Equal(NetworkDirection.Inbound, packet.Direction);
        Assert.Equal(DataSourceKind.Live, packet.Source);
        Assert.Equal(payload, packet.Payload.ToArray());
    }

    [Fact]
    public void APacketToTheServerIsObservedAsOutbound()
    {
        using var backend = ScopedLiveCaptureBackend.OverSource(
            SourceOf(Packet(IPAddress.Parse("192.168.1.5"), Server, 51000, ServerPort, new byte[] { 9 })),
            Server, ServerPort);

        Assert.True(backend.TryObserve(out ObservedPacket packet));
        Assert.Equal(NetworkDirection.Outbound, packet.Direction);
    }

    [Fact]
    public void AnAcknowledgementCarryingNothingIsNotAnObservation()
    {
        // Counting empty segments would make an idle connection look busy, and a
        // world model fed on that would believe something was happening.
        using var backend = ScopedLiveCaptureBackend.OverSource(
            SourceOf(Packet(Server, IPAddress.Parse("192.168.1.5"), ServerPort, 51000, Array.Empty<byte>())),
            Server, ServerPort);

        Assert.False(backend.TryObserve(out _));
        Assert.Equal(1, backend.EmptySegments);
    }

    [Fact]
    public void APacketThatCannotBeParsedIsCountedNotGuessedAt()
    {
        using var backend = ScopedLiveCaptureBackend.OverSource(
            SourceOf(new byte[] { 0xFF, 0xFF, 0xFF }), Server, ServerPort);

        Assert.False(backend.TryObserve(out _));
        Assert.Equal(1, backend.UnparsedPackets);
    }

    [Fact]
    public void AQuietWireIsNotAnError()
    {
        using var backend = ScopedLiveCaptureBackend.OverSource(SourceOf(), Server, ServerPort);

        Assert.False(backend.TryObserve(out _));
        Assert.True(backend.IsCapturing);
    }

    [Fact]
    public void ADisposedBackendObservesNothing()
    {
        var backend = ScopedLiveCaptureBackend.OverSource(
            SourceOf(Packet(Server, IPAddress.Parse("192.168.1.5"), ServerPort, 51000, new byte[] { 1 })),
            Server, ServerPort);
        backend.Dispose();

        Assert.False(backend.TryObserve(out _));
        Assert.False(backend.IsCapturing);
    }

    // ------------------------------------------------ provenance cannot be faked

    [Fact]
    public void AReplayedSourceCannotBeHandedOutAsLive()
    {
        // The one rule this pair of classes exists to enforce. A recording is
        // evidence about the past; labelling it LIVE would let it teach the world
        // model and the prediction ledger as though it were happening now.
        Assert.Throws<ArgumentException>(() => new ScopedCaptureBackendOverSource(
            SourceOf(), Server, ServerPort, DataSourceKind.Live));
    }

    [Theory]
    [InlineData(DataSourceKind.Cached)]
    [InlineData(DataSourceKind.Simulated)]
    [InlineData(DataSourceKind.Unknown)]
    public void ADeclaredProvenanceTravelsWithEveryPacket(DataSourceKind declared)
    {
        using var backend = new ScopedCaptureBackendOverSource(
            SourceOf(Packet(Server, IPAddress.Parse("192.168.1.5"), ServerPort, 51000, new byte[] { 7 })),
            Server, ServerPort, declared);

        Assert.True(backend.TryObserve(out ObservedPacket packet));
        Assert.Equal(declared, packet.Source);
        Assert.Equal(declared, backend.Source);
    }

    [Fact]
    public void TheBackendNamesTheOneConnectionItIsBoundTo()
    {
        using var backend = ScopedLiveCaptureBackend.OverSource(SourceOf(), Server, ServerPort);

        Assert.Equal(Server.ToString(), backend.Endpoint.Host);
        Assert.Equal(ServerPort, backend.Endpoint.Port);
        Assert.True(backend.Endpoint.IsSpecified);
    }
}
