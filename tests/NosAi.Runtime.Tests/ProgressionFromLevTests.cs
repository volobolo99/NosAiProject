using System.Text;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// AP-08/A2+A4: the <c>lev</c> opcode publishes the progression the wire already
/// carries — level, experience, job level and job experience. Six fields are
/// read; fields 7-12 are deliberately absent from the contract.
/// </summary>
/// <remarks>
/// <para>
/// The hand-built packets below prove the decoder reads the shape the catalogue
/// wrote down. The <c>RecordedCaptureFact</c> tests prove that shape is on the
/// wire: they replay the real sessions and cross-check the decoded values
/// against what the recording actually carried, which is the check a single
/// hand-built packet cannot make.
/// </para>
/// </remarks>
public sealed class ProgressionFromLevTests
{
    private static readonly DateTime At = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly GameEndpoint Endpoint = new("79.110.84.175", 4002);

    /// <summary>The capture's own line, byte for byte.</summary>
    private const string CapturedLev = "lev 56 9688533 39 43226 18247900 185500 35106 7 0 0 1 0";

    private static ObservedPacket Packet(string line, DateTime? at = null, DataSourceKind source = DataSourceKind.Live)
        => new(at ?? At, NetworkDirection.Inbound, Endpoint.Host, Endpoint.Port, Encoding.ASCII.GetBytes(line), source);

    [Fact]
    public void The_captures_line_decodes_to_the_six_values_of_the_catalogue()
    {
        DecodedObservations decoded = new NosTaleWorldProtocolDecoder().Decode(Packet(CapturedLev));

        Assert.NotNull(decoded.Progression);
        PlayerProgression progression = decoded.Progression.Value;
        Assert.Equal(56, progression.Level);
        Assert.Equal(9688533, progression.Experience);
        Assert.Equal(18247900, progression.ExperienceForNextLevel);
        Assert.Equal(39, progression.JobLevel);
        Assert.Equal(43226, progression.JobExperience);
        Assert.Equal(185500, progression.JobExperienceForNextJobLevel);
        // Progression is state, not a sighting and not an event.
        Assert.Empty(decoded.Sightings);
        Assert.Empty(decoded.Events);
    }

    /// <summary>A packet shorter than the observed shape names no progression.</summary>
    [Fact]
    public void A_lev_with_five_fields_is_refused_whole()
    {
        DecodedObservations decoded = new NosTaleWorldProtocolDecoder()
            .Decode(Packet("lev 56 9688533 39 43226 18247900"));

        Assert.True(decoded.IsEmpty);
        Assert.Null(decoded.Progression);
    }

    /// <summary>
    /// A field that is not the number it should be, or outside what that number
    /// can hold, refuses the packet whole — a half-read progression would publish
    /// a level with a garbage XP beside it as a fact.
    /// </summary>
    [Theory]
    [InlineData("lev x 9688533 39 43226 18247900 185500")]     // level is not a number
    [InlineData("lev 56 -1 39 43226 18247900 185500")]        // negative experience
    [InlineData("lev 56 9688533 39 43226 0 185500")]          // zero experience for next level
    [InlineData("lev 56 9688533 0 43226 18247900 185500")]    // job level 0
    [InlineData("lev 56 9688533 39 43226 18247900 x")]        // jobXpMax is not a number
    public void A_malformed_lev_is_refused_whole(string line)
    {
        DecodedObservations decoded = new NosTaleWorldProtocolDecoder().Decode(Packet(line));

        Assert.True(decoded.IsEmpty);
        Assert.Null(decoded.Progression);
    }

    /// <summary>
    /// Fields 7-12 are unknown and not read, so changing them changes nothing.
    /// This is what "not read" has to mean.
    /// </summary>
    [Fact]
    public void Tokens_after_the_sixth_are_ignored()
    {
        PlayerProgression? asCaptured = new NosTaleWorldProtocolDecoder()
            .Decode(Packet(CapturedLev)).Progression;
        PlayerProgression? withOtherTail = new NosTaleWorldProtocolDecoder()
            .Decode(Packet("lev 56 9688533 39 43226 18247900 185500 1 2 3 4 5 6")).Progression;

        Assert.NotNull(asCaptured);
        Assert.NotNull(withOtherTail);
        Assert.Equal(asCaptured.Value, withOtherTail.Value);
    }

    /// <summary>Progression never makes an otherwise-empty packet non-empty.</summary>
    [Fact]
    public void Progression_does_not_make_an_unknown_opcode_non_empty()
    {
        DecodedObservations decoded = new NosTaleWorldProtocolDecoder()
            .Decode(Packet("guri 2 1 3443217 0"));

        Assert.True(decoded.IsEmpty);
        Assert.Null(decoded.Progression);
    }

    // --------------------------------------------- against the real recordings

    /// <summary>
    /// The combat session carries 23 <c>lev</c> packets, level 56 throughout,
    /// and the experience rises in every one of them. This is the cross-check a
    /// decoded field is asked for: it holds against the real bytes, not against
    /// the document that describes them.
    /// </summary>
    [RecordedCaptureFact("nostale_combat.noscap")]
    public void The_combat_recording_yields_23_readings_strictly_rising()
    {
        List<PlayerProgression> readings = ReadProgressionReadings("nostale_combat.noscap");

        Assert.Equal(23, readings.Count);
        Assert.All(readings, r => Assert.Equal(56, r.Level));
        AssertStrictlyRising(readings.Select(r => r.Experience));
        AssertStrictlyRising(readings.Select(r => r.JobExperience));
        Assert.Equal(9688533, readings[0].Experience);
        Assert.Equal(9690657, readings[^1].Experience);
    }

    [RecordedCaptureFact("certificazione.noscap")]
    public void The_certificazione_recording_yields_11_readings_strictly_rising()
    {
        List<PlayerProgression> readings = ReadProgressionReadings("certificazione.noscap");

        Assert.Equal(11, readings.Count);
        AssertStrictlyRising(readings.Select(r => r.Experience));
        Assert.Equal(9728169, readings[0].Experience);
        Assert.Equal(9731949, readings[^1].Experience);
    }

    [RecordedCaptureFact("nostale_live.noscap")]
    public void The_live_recording_yields_4_readings_strictly_rising()
    {
        List<PlayerProgression> readings = ReadProgressionReadings("nostale_live.noscap");

        Assert.Equal(4, readings.Count);
        AssertStrictlyRising(readings.Select(r => r.Experience));
        Assert.Equal(9708129, readings[0].Experience);
        Assert.Equal(9709347, readings[^1].Experience);
    }

    /// <summary>
    /// The idle session carries no <c>lev</c> at all, and that is the other half
    /// of the evidence: the census is unchanged because there is nothing to read.
    /// </summary>
    [RecordedCaptureFact("nostale_01.noscap")]
    public void The_idle_recording_yields_no_progression_and_the_census_is_unchanged()
    {
        string path = RecordedCaptureFactAttribute.Resolve("nostale_01.noscap")!;

        Assert.Empty(ReadProgressionReadings(path));

        WorldChannelReplaySummary summary = WorldChannelReplay.ReplayFile(path);
        Assert.Equal(2490, summary.TotalPackets);
        Assert.Equal(2490, summary.ReadablePackets);
    }

    /// <summary>
    /// Replays one recording through the shipping chain and returns every
    /// progression reading, in wire order. One packet per report keeps the order.
    /// </summary>
    private static List<PlayerProgression> ReadProgressionReadings(string recording)
    {
        string path = RecordedCaptureFactAttribute.Resolve(recording)!;
        IPacketSource packets = CaptureFile.Open(path);
        var endpoint = new GameEndpoint(packets.ServerAddress.ToString(), packets.ServerPort);
        using var observationSource = ReassembledObservationSource.ForNosTaleWorld(packets, DataSourceKind.Cached);
        var observer = new GameTrafficObserver(
            observationSource, new ScopedGameTrafficFilter(endpoint), new NosTaleWorldProtocolDecoder());

        var readings = new List<PlayerProgression>();
        while (true)
        {
            NetworkObservationReport report = observer.ObservePending(1);
            if (report.ObservedPackets == 0)
                break;
            if (report.Progression is { } progression)
                readings.Add(progression);
        }
        return readings;
    }

    private static void AssertStrictlyRising(IEnumerable<long> values)
    {
        long? previous = null;
        foreach (long value in values)
        {
            if (previous is { } p)
                Assert.True(value > p, $"Expected strictly rising, found {value} after {p}.");
            previous = value;
        }
    }
}
