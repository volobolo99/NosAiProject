using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// S1, contro le registrazioni reali: il vnum che <c>icon</c> porta è quello del
/// <c>drop</c> che lo precede e del <c>get</c> che lo segue — il riscontro
/// indipendente che rende il campo decodificabile, misurato sui byte e non
/// assunto.
/// </summary>
public sealed class IconItemVnumRecordedCaptureTests
{
    [RecordedCaptureFact("messaggi.noscap")]
    public void Messaggi_icon_is_the_vnum_of_the_drop_it_follows()
    {
        string path = RecordedCaptureFactAttribute.Resolve("messaggi.noscap")!;
        (List<int> icons, List<int> drops) = Replay(path);

        Assert.Equal(new[] { 8 }, icons);
        Assert.Contains(8, drops);
    }

    [RecordedCaptureTheory("nostale_combat.noscap", "certificazione.noscap")]
    [InlineData("nostale_combat.noscap", 2)]
    [InlineData("certificazione.noscap", 1)]
    public void Every_icon_vnum_is_a_drop_vnum_in_the_same_capture(string recording, int expectedCount)
    {
        string path = RecordedCaptureFactAttribute.Resolve(recording)!;
        (List<int> icons, List<int> drops) = Replay(path);

        // The count is asserted first: an Assert.All over an empty set passes
        // vacuously and would hide a decoder that stopped reading icon.
        Assert.Equal(expectedCount, icons.Count);
        Assert.All(icons, vnum => Assert.Contains(vnum, drops));
    }

    // ---------------------------------------------------------------- helpers

    private static (List<int> Icons, List<int> Drops) Replay(string path)
    {
        var icons = new List<int>();
        var drops = new List<int>();
        using IPacketSource packets = CaptureFile.Open(path);
        using var source = ReassembledObservationSource.ForNosTaleWorld(packets, DataSourceKind.Cached);
        var decoder = new NosTaleWorldProtocolDecoder();
        while (source.TryObserve(out ObservedPacket packet))
        {
            DecodedObservations decoded = decoder.Decode(packet);
            if (decoded.ItemIcon is { } icon)
                icons.Add(icon.ItemVnum);
            if (decoded.GroundItem is { } drop)
                drops.Add(drop.Vnum);
        }
        return (icons, drops);
    }
}
