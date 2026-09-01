using NosAi.LiveIntegration.Capture;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The WinDivert open flags, held to the values in the driver's own header.
/// </summary>
/// <remarks>
/// <para>
/// T-04 ran the capture layer against a real driver for the first time and it
/// read nothing: 0 packets from the game, and 0 from a control endpoint being
/// deliberately hammered with HTTPS requests. The driver opened, the filter was
/// correct, and every packet was unreachable, because the constant named
/// <c>FlagRecvOnly</c> held 0x0008 -- which is <c>WINDIVERT_FLAG_SEND_ONLY</c>.
/// The handle was write-only.
/// </para>
/// <para>
/// A wrong constant with a right name cannot be caught by testing the layer's
/// behaviour, because everything below it is exercised against synthetic sources
/// and recorded files where the driver never runs. The only thing that catches it
/// is asserting the number, so that is what this does. The values are from
/// <c>windivert.h</c>, WinDivert 2.2:
/// </para>
/// <code>
/// #define WINDIVERT_FLAG_SNIFF        0x0001
/// #define WINDIVERT_FLAG_DROP         0x0002
/// #define WINDIVERT_FLAG_RECV_ONLY    0x0004
/// #define WINDIVERT_FLAG_SEND_ONLY    0x0008
/// </code>
/// </remarks>
public sealed class WinDivertFlagTests
{
    [Fact]
    public void SniffIsOne() => Assert.Equal(0x0001UL, WinDivertPacketSource.FlagSniff);

    [Fact]
    public void DropIsTwo() => Assert.Equal(0x0002UL, WinDivertPacketSource.FlagDrop);

    [Fact]
    public void RecvOnlyIsFourNotEight()
    {
        // The whole of T-04. Eight is SEND_ONLY, and a capture opened with it
        // receives nothing while looking entirely healthy.
        Assert.Equal(0x0004UL, WinDivertPacketSource.FlagRecvOnly);
    }

    [Fact]
    public void SendOnlyIsEight() => Assert.Equal(0x0008UL, WinDivertPacketSource.FlagSendOnly);

    [Fact]
    public void ACaptureHandleNeverAsksForSendOnly()
    {
        // This layer observes and does not put anything on the wire (ADR-0014
        // opened reading the client, not writing to it). The two flags are
        // distinct values, and the capture path must use the reading one.
        Assert.NotEqual(WinDivertPacketSource.FlagRecvOnly, WinDivertPacketSource.FlagSendOnly);
    }
}
