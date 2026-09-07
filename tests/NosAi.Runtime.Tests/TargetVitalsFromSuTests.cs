using System.Globalization;
using System.Text;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Observability;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// AP-05/A2+A4: <c>su</c> publishes the target's vitals — current and max HP
/// (fields 16 and 17) — but only when field 11 is 1, never for the controlled
/// character, and never for a packet shorter than the observed 18 fields.
/// </summary>
/// <remarks>
/// <para>
/// The hand-built packets prove the decoder reads the shape and the gate the way
/// the catalogue wrote it. The <c>RecordedCaptureTheory</c> tests prove that
/// shape and that gate hold against the real bytes, which is the check a single
/// hand-built packet cannot make.
/// </para>
/// </remarks>
public sealed class TargetVitalsFromSuTests
{
    private static readonly DateTime At = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly GameEndpoint Endpoint = new("79.110.84.175", 4002);

    private const long OwnId = 3443217;
    private const long Monster = 313816;
    private const string Cond = "cond 1 3443217 0 0 11";
    private const string Enter = "in 3 36 313816 109 63 2 100 100";

    /// <summary>Player attacks the monster with the target's HP reported (flag 1, hp 200/maxHp 310).</summary>
    private const string PlayerHitsMonsterReported =
        "su 1 3443217 3 313816 226 250 12 522 0 0 1 0 698 5 0 200 310";

    /// <summary>The same hit with the target's HP not reported (flag 0).</summary>
    private const string PlayerHitsMonsterUnreported =
        "su 1 3443217 3 313816 226 250 12 522 0 0 0 0 698 5 0 0 310";

    private static ObservedPacket Packet(string line, DateTime? at = null, DataSourceKind source = DataSourceKind.Live)
        => new(at ?? At, NetworkDirection.Inbound, Endpoint.Host, Endpoint.Port, Encoding.ASCII.GetBytes(line), source);

    private static NosTaleWorldProtocolDecoder Identified()
    {
        var decoder = new NosTaleWorldProtocolDecoder();
        decoder.Decode(Packet(Cond));
        return decoder;
    }

    // ----------------------------------------------------- hand-built shapes

    [Fact]
    public void A_su_with_the_flag_one_places_the_target_vitals_on_a_sighting()
    {
        var decoder = Identified();
        decoder.Decode(Packet(Enter, At));

        DecodedObservations decoded = decoder.Decode(Packet(PlayerHitsMonsterReported, At.AddSeconds(2)));

        EntitySighting sighting = Assert.Single(decoded.Sightings);
        Assert.Equal(Monster, sighting.EntityId);
        Assert.Equal(new AbsoluteVitals(200, 310), sighting.Vitals);
        Assert.Equal(200d / 310d, sighting.HpRatio!.Value, 9);
        // Position from the in packet, health from the su: CACHED, both instants kept.
        Assert.Equal(DataSourceKind.Cached, sighting.Source);
        Assert.Equal(At, sighting.PositionObservedAtUtc);
        Assert.Equal(At.AddSeconds(2), sighting.HpObservedAtUtc);
        Assert.Equal(36, sighting.Vnum);
        // The hit event is unchanged.
        Assert.Equal(GameEventKind.CombatHit, Assert.Single(decoded.Events).Kind);
    }

    [Fact]
    public void A_su_with_the_flag_zero_publishes_no_vitals_and_does_not_zero_the_remembered_health()
    {
        var decoder = Identified();
        decoder.Decode(Packet(Enter)); // monster at full health, position known

        DecodedObservations decoded = decoder.Decode(Packet(PlayerHitsMonsterUnreported));

        Assert.Empty(decoded.Sightings); // the zero in field 16 is "not reported", not "dead"
        Assert.Equal(GameEventKind.CombatHit, Assert.Single(decoded.Events).Kind);

        // A later move still carries the remembered full health, not the zero.
        EntitySighting moved = Assert.Single(
            decoder.Decode(Packet("mv 3 313816 120 70 12", At.AddSeconds(3))).Sightings);
        Assert.Equal(1.00, moved.HpRatio!.Value, 9);
        Assert.Null(moved.Vitals);
    }

    /// <summary>
    /// The same plausibility check <c>DecodeOtherVitals</c> uses refuses an
    /// implausible pair, leaving the hit event and no sighting.
    /// </summary>
    [Theory]
    [InlineData("su 1 3443217 3 313816 226 250 12 522 0 0 1 0 698 5 0 400 310")] // hp > maxHp
    [InlineData("su 1 3443217 3 313816 226 250 12 522 0 0 1 0 698 5 0 -1 310")]  // hp < 0
    [InlineData("su 1 3443217 3 313816 226 250 12 522 0 0 1 0 698 5 0 200 0")]   // maxHp == 0
    public void An_implausible_pair_is_refused_like_st_does(string line)
    {
        var decoder = Identified();
        decoder.Decode(Packet(Enter));

        DecodedObservations decoded = decoder.Decode(Packet(line));

        Assert.Empty(decoded.Sightings);
        Assert.Equal(GameEventKind.CombatHit, Assert.Single(decoded.Events).Kind);
    }

    [Fact]
    public void A_hit_on_the_controlled_character_publishes_no_target_vitals()
    {
        DecodedObservations decoded = Identified().Decode(Packet(
            "su 3 313816 1 3443217 0 12 11 200 0 0 1 99 0 1 0 7289 7305"));

        Assert.Empty(decoded.Sightings); // stat is the source for the own life
        Assert.NotNull(decoded.PlayerHit); // the aggressor is still named
    }

    [Fact]
    public void A_short_su_still_decodes_attacker_and_target()
    {
        DecodedObservations decoded = Identified().Decode(Packet("su 3 313816 1 3443217 0"));

        Assert.Empty(decoded.Sightings); // fewer than 18 fields: no vitals
        GameEvent combat = Assert.Single(decoded.Events);
        Assert.Equal(GameEventKind.CombatHit, combat.Kind);
        Assert.Equal(OwnId, combat.EntityId);
        Assert.NotNull(decoded.PlayerHit); // attacker and target still decoded
    }

    // ------------------------------------------ against the real recordings

    [RecordedCaptureTheory("nostale_live.noscap", "equip_test.noscap", "nostale_combat.noscap", "certificazione.noscap")]
    [InlineData("nostale_live.noscap", 13, 9)]
    [InlineData("equip_test.noscap", 2, 0)]
    [InlineData("nostale_combat.noscap", 117, 94)]
    [InlineData("certificazione.noscap", 80, 69)]
    public void Every_su_with_the_flag_one_names_a_positive_hp_at_most_the_max(string recording, int expectedSu, int expectedFlagOne)
    {
        List<string> lines = ReadSuLines(recording);

        Assert.Equal(expectedSu, lines.Count);

        int flagOne = 0;
        foreach (string line in lines)
        {
            string[] f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(18, f.Length);
            if (int.Parse(f[11], CultureInfo.InvariantCulture) != 1)
                continue;
            flagOne++;
            int hp = int.Parse(f[16], CultureInfo.InvariantCulture);
            int maxHp = int.Parse(f[17], CultureInfo.InvariantCulture);
            Assert.True(hp > 0 && hp <= maxHp, $"{recording}: {line}");
        }
        Assert.Equal(expectedFlagOne, flagOne);
    }

    [RecordedCaptureTheory("nostale_live.noscap", "equip_test.noscap", "nostale_combat.noscap", "certificazione.noscap")]
    [InlineData("nostale_live.noscap")]
    [InlineData("equip_test.noscap")]
    [InlineData("nostale_combat.noscap")]
    [InlineData("certificazione.noscap")]
    public void The_max_hp_is_constant_per_target_id_within_a_recording(string recording)
    {
        var byTarget = new Dictionary<long, int>();
        foreach (string line in ReadSuLines(recording))
        {
            string[] f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            long target = long.Parse(f[4], CultureInfo.InvariantCulture);
            int maxHp = int.Parse(f[17], CultureInfo.InvariantCulture);
            if (byTarget.TryGetValue(target, out int seen))
                Assert.Equal(seen, maxHp);
            else
                byTarget[target] = maxHp;
        }
    }

    [RecordedCaptureTheory("nostale_live.noscap", "equip_test.noscap", "nostale_combat.noscap", "certificazione.noscap")]
    [InlineData("nostale_live.noscap", 4)]
    [InlineData("equip_test.noscap", 2)]
    [InlineData("nostale_combat.noscap", 23)]
    [InlineData("certificazione.noscap", 11)]
    public void Every_su_with_the_flag_zero_publishes_no_target_vitals(string recording, int expectedFlagZero)
    {
        int flagZero = 0;
        foreach ((string line, DecodedObservations decoded) in ReplaySuPackets(recording))
        {
            string[] f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (int.Parse(f[11], CultureInfo.InvariantCulture) != 0)
                continue;
            flagZero++;
            Assert.Empty(decoded.Sightings);
        }
        Assert.Equal(expectedFlagZero, flagZero);
    }

    // ------------------------------------------------------------- helpers

    private static List<string> ReadSuLines(string recording)
    {
        string path = RecordedCaptureFactAttribute.Resolve(recording)!;
        using IPacketSource source = CaptureFile.Open(path);
        return WireInspectCommand.RawLines(source, DataSourceKind.Cached, "su", maxLines: 0).ToList();
    }

    /// <summary>
    /// Replays one recording through the shipping chain, feeding every packet to
    /// one decoder so <c>cond</c>/<c>in</c>/<c>st</c>/<c>mv</c> state builds up, and
    /// returns each <c>su</c> packet's decoded result in wire order.
    /// </summary>
    private static List<(string Line, DecodedObservations Decoded)> ReplaySuPackets(string recording)
    {
        string path = RecordedCaptureFactAttribute.Resolve(recording)!;
        IPacketSource packets = CaptureFile.Open(path);
        using var source = ReassembledObservationSource.ForNosTaleWorld(packets, DataSourceKind.Cached);
        var decoder = new NosTaleWorldProtocolDecoder();
        var results = new List<(string, DecodedObservations)>();
        while (source.TryObserve(out ObservedPacket packet))
        {
            DecodedObservations decoded = decoder.Decode(packet);
            IReadOnlyList<string> lines = NosTaleWorldDecoder.Decode(packet.Payload.Span);
            if (lines.Count != 1)
                continue;
            string[] tokens = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 0 && tokens[0] == "su")
                results.Add((lines[0], decoded));
        }
        return results;
    }
}
