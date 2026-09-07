using NosAi.LiveIntegration;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The <c>st</c> packet's absolute HP pair, kept instead of divided away.
/// </summary>
/// <remarks>
/// <para>
/// <c>DecodeOtherVitals</c> read a monster's current and maximum hit points off
/// fields 7 and 9, validated them, and then collapsed them into a ratio because
/// <see cref="EntitySighting"/> had nowhere to put the pair. Everything
/// downstream saw <c>0.64</c> where the wire had said <c>198/310</c>.
/// </para>
/// <para>
/// The capture line every test here feeds is the one this repository already
/// replays elsewhere (<c>NosTaleWorldObservationTests</c> uses the same bytes):
/// monster <c>313816</c> at 198/310. Two corroborations, both already in the
/// repository: 198/310 is 63.87% while the packet's own percentage field says
/// <c>66</c> -- which is why the decoder prefers the absolutes and ignores that
/// field -- and <c>docs/PROTOCOLLO_NOSTALE.md</c> records a monster with max HP
/// <c>310</c> in the same capture, from the unrelated <c>su</c> packet.
/// </para>
/// </remarks>
public sealed class EntitySightingAbsoluteVitalsTests
{
    private static readonly DateTime At = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly GameEndpoint Endpoint = new("79.110.84.175", 4002);

    /// <summary>The real capture line: monster 313816 at 198/310, percentage field 66.</summary>
    private const string RealVitals = "st 3 313816 8 0 66 100 198 52 310 52 0";

    /// <summary>The real capture line that puts the same monster in view first.</summary>
    private const string RealEnter = "in 3 36 313816 109 63 2 100 100";

    private static ObservedPacket Ascii(string packet, DateTime? at = null)
        => new(
            at ?? At,
            NetworkDirection.Inbound,
            Endpoint.Host,
            Endpoint.Port,
            System.Text.Encoding.ASCII.GetBytes(packet),
            DataSourceKind.Live);

    /// <summary>
    /// The pair is added and the ratio is untouched. Both halves are asserted
    /// together on purpose: a change that produced the pair by altering how the
    /// ratio is computed would be a behaviour change to every existing consumer.
    /// </summary>
    [Fact]
    public void AVitalsPacket_CarriesTheAbsolutePair_AndLeavesTheRatioExactlyAsItWas()
    {
        var decoder = new NosTaleWorldProtocolDecoder();
        decoder.Decode(Ascii(RealEnter));

        EntitySighting sighting = Assert.Single(decoder.Decode(Ascii(RealVitals)).Sightings);

        Assert.Equal(new AbsoluteVitals(198, 310), sighting.Vitals);
        Assert.Equal(198d / 310d, sighting.HpRatio!.Value, 9);
    }

    /// <summary>
    /// The assertion that proves the decoder reads the absolutes rather than the
    /// percentage: the maximum is 310, the number at field 9 -- not 100 (field
    /// 6, the percentage's own scale) and not 66 (field 5, the percentage the
    /// decoder is documented to ignore because it disagrees with the pair).
    /// </summary>
    [Fact]
    public void TheMaximum_IsTheWiresOwnNumber_NotThePercentageScale()
    {
        var decoder = new NosTaleWorldProtocolDecoder();
        decoder.Decode(Ascii(RealEnter));

        EntitySighting sighting = Assert.Single(decoder.Decode(Ascii(RealVitals)).Sightings);

        Assert.Equal(310, sighting.Vitals!.Value.Maximum);
        Assert.Equal(198, sighting.Vitals!.Value.Current);
        Assert.NotEqual(100, sighting.Vitals!.Value.Maximum);
        Assert.NotEqual(66, sighting.Vitals!.Value.Current);
    }

    /// <summary>
    /// An entity seen only entering view has a real fraction and no absolutes.
    /// That is the wire, not a defect: <c>in</c> states <c>hp%</c> and nothing
    /// else, and a maximum reconstructed from a percentage would be a number
    /// nobody observed.
    /// </summary>
    [Fact]
    public void AnEnterOnlyEntity_HasAFractionAndNoAbsolutes()
    {
        var decoder = new NosTaleWorldProtocolDecoder();

        EntitySighting sighting = Assert.Single(decoder.Decode(Ascii(RealEnter)).Sightings);

        Assert.Null(sighting.Vitals);
        Assert.Equal(1.00, sighting.HpRatio!.Value, 9);
    }

    /// <summary>
    /// The pair ages exactly like the ratio: a later move reuses the remembered
    /// health, and carries the <c>st</c>'s own instant with it rather than the
    /// move's.
    /// </summary>
    [Fact]
    public void AMoveReusingRememberedHealth_CarriesTheRememberedPairAndItsOriginalInstant()
    {
        DateTime vitalsAt = At.AddSeconds(5);
        DateTime moveAt = At.AddSeconds(9);
        var decoder = new NosTaleWorldProtocolDecoder();
        decoder.Decode(Ascii(RealEnter));
        decoder.Decode(Ascii(RealVitals, vitalsAt));

        EntitySighting moved = Assert.Single(
            decoder.Decode(Ascii("mv 3 313816 120 70 12", moveAt)).Sightings);

        Assert.Equal(new AbsoluteVitals(198, 310), moved.Vitals);
        Assert.Equal(vitalsAt, moved.HpObservedAtUtc);
        Assert.Equal(moveAt, moved.PositionObservedAtUtc);
    }

    /// <summary>
    /// A later <c>in</c> restates the same health as a percentage, so it drops
    /// the pair rather than keeping a stale one beside a fresher ratio. Two
    /// statements of one fact must not disagree about how precise it is.
    /// </summary>
    [Fact]
    public void AnEnterAfterAVitalsPacket_DropsThePairRatherThanKeepingAStaleOne()
    {
        var decoder = new NosTaleWorldProtocolDecoder();
        decoder.Decode(Ascii(RealEnter));
        decoder.Decode(Ascii(RealVitals));

        EntitySighting reentered = Assert.Single(
            decoder.Decode(Ascii("in 3 36 313816 109 63 2 50 100", At.AddSeconds(3))).Sightings);

        Assert.Null(reentered.Vitals);
        Assert.Equal(0.50, reentered.HpRatio!.Value, 9);
    }

    /// <summary>
    /// The guard that was already there still refuses a malformed packet whole:
    /// no sighting, and therefore no pair. Asserted rather than added -- a
    /// non-positive maximum was refused before this change and must stay
    /// refused, because a pair with a zero maximum is the exact shape this
    /// record exists to make impossible.
    /// </summary>
    [Theory]
    [InlineData("st 3 313816 8 0 66 100 198 52 0 52 0")]
    [InlineData("st 3 313816 8 0 66 100 400 52 310 52 0")]
    [InlineData("st 3 313816 8 0 66 100 -1 52 310 52 0")]
    public void AMalformedVitalsPacket_ProducesNoSightingAndThereforeNoPair(string malformed)
    {
        var decoder = new NosTaleWorldProtocolDecoder();
        decoder.Decode(Ascii(RealEnter));

        Assert.Empty(decoder.Decode(Ascii(malformed)).Sightings);
    }
}
