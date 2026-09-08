using System.Globalization;
using System.Text;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.GameData;
using NosAi.Runtime.Perception.Network;
using Xunit;
using Xunit.Abstractions;

namespace NosAi.Runtime.Tests;

/// <summary>
/// AP-05/A2+A4, contro le registrazioni reali: <c>out</c> è uscita e non morte,
/// e i valori di skill che <c>su</c> pubblica esistono nel catalogo del client.
/// </summary>
/// <remarks>
/// I conteggi e gli insiemi sono misurati sui byte reali, non attesi: un test che
/// ne vedesse zero passerebbe a vuoto, quindi il numero è asserito.
/// </remarks>
public sealed class OutEntityRecordedCaptureTests
{
    private static readonly string[] SuCaptures =
    {
        "nostale_combat.noscap", "nostale_live.noscap", "equip_test.noscap",
        "certificazione.noscap", "messaggi.noscap",
        "calibrate_20260903_091408Z_round1.noscap", "calibrate_20260903_091428Z_round2.noscap",
    };

    private readonly ITestOutputHelper _output;

    public OutEntityRecordedCaptureTests(ITestOutputHelper output) => _output = output;

    // -------------------------------------------------- exits vs deaths

    [RecordedCaptureFact("messaggi.noscap")]
    public void The_recorded_capture_has_exactly_18_exits_and_2_deaths()
    {
        string path = RecordedCaptureFactAttribute.Resolve("messaggi.noscap")!;
        int exits = 0, deaths = 0;

        foreach (GameEvent gameEvent in ReplayEvents(path))
        {
            if (gameEvent.Kind == GameEventKind.EntityLeft) exits++;
            else if (gameEvent.Kind == GameEventKind.EntityDeath) deaths++;
        }

        // The number that separates this task from the defect it avoids: had `out`
        // been decoded as a death, this capture would report 20 kills instead of 2.
        Assert.Equal(18, exits);
        Assert.Equal(2, deaths);
    }

    // ------------------------------------------- su skill values per capture

    [RecordedCaptureTheory(
        "nostale_combat.noscap", "nostale_live.noscap", "equip_test.noscap",
        "certificazione.noscap", "messaggi.noscap",
        "calibrate_20260903_091408Z_round1.noscap", "calibrate_20260903_091428Z_round2.noscap")]
    [InlineData("nostale_combat.noscap", 27, 90)]
    [InlineData("nostale_live.noscap", 9, 4)]
    [InlineData("equip_test.noscap", 0, 0)]
    [InlineData("certificazione.noscap", 36, 44)]
    [InlineData("messaggi.noscap", 34, 9)]
    [InlineData("calibrate_20260903_091408Z_round1.noscap", 7, 1)]
    [InlineData("calibrate_20260903_091428Z_round2.noscap", 5, 11)]
    public void Every_player_attack_publishes_a_skill_and_every_monster_attack_publishes_none(
        string recording, int expectedPlayer, int expectedMonster)
    {
        int player = 0, monster = 0;
        foreach (string line in SuLines(recording))
        {
            string[] f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int? skill = DecodeSkill(line);
            if (f[1] == "1")
            {
                player++;
                // A player attack always names a real skill: 200, 220, 222, 223,
                // 224, 226 or 228 — never absent, never zero.
                Assert.True(skill is > 0, $"{recording}: {line}");
            }
            else if (f[1] == "3")
            {
                monster++;
                // A monster's basic attack carries 0, which is absent, not a skill.
                Assert.Null(skill);
            }
        }

        Assert.Equal(expectedPlayer, player);
        Assert.Equal(expectedMonster, monster);
    }

    [RecordedCaptureTheory(
        "nostale_combat.noscap", "nostale_live.noscap", "equip_test.noscap",
        "certificazione.noscap", "messaggi.noscap",
        "calibrate_20260903_091408Z_round1.noscap", "calibrate_20260903_091428Z_round2.noscap")]
    [InlineData(true)]
    public void The_distinct_player_skill_values_are_exactly_the_seven_catalogued(bool _)
    {
        var skills = new HashSet<int>();
        int playerAttacks = 0;
        foreach (string recording in SuCaptures)
        {
            foreach (string line in SuLines(recording))
            {
                string[] f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (f[1] != "1")
                    continue;
                int? skill = DecodeSkill(line);
                Assert.True(skill is > 0);
                skills.Add(skill.Value);
                playerAttacks++;
            }
        }

        Assert.Equal(new[] { 200, 220, 222, 223, 224, 226, 228 }, skills.OrderBy(x => x).ToArray());
        // The aggregate across the captures: 118 player attacks in total.
        Assert.Equal(118, playerAttacks);
    }

    // -------------------------------------------------- catalogue cross-check

    [NosTaleClientFact]
    public void The_seven_skill_values_exist_in_the_catalogue_and_zero_does_not()
    {
        string directory = NosTaleClientFactAttribute.ResolveDirectory()!;
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        var importer = new ReferenceImporter(directory);

        ImportOutcome outcome = importer.ImportOne(
            db, ReferenceImporter.Tables.Single(t => t.Kind == "skill"));
        Assert.True(outcome.Ok, outcome.FailureReason);
        LanguageImportReport language = importer.ImportLanguage(db, "IT");
        Assert.True(language.Ok, language.FailureReason);

        foreach (int vnum in new[] { 200, 220, 222, 223, 224, 226, 228 })
        {
            Assert.True(db.Exists("skill", vnum), $"skill {vnum} non nel catalogo");
            Evidence.Live(_output, $"skill{vnum}", db.DisplayName("skill", vnum, "IT") ?? "(senza nome)");
        }

        // 0 is the basic-attack marker and is not a skill in the catalogue.
        Assert.False(db.Exists("skill", 0));
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Replays one recording, feeding one decoder, and returns its events.</summary>
    private static IReadOnlyList<GameEvent> ReplayEvents(string path)
    {
        using IPacketSource packets = CaptureFile.Open(path);
        using var source = ReassembledObservationSource.ForNosTaleWorld(packets, DataSourceKind.Cached);
        var decoder = new NosTaleWorldProtocolDecoder();
        var events = new List<GameEvent>();
        while (source.TryObserve(out ObservedPacket packet))
            events.AddRange(decoder.Decode(packet).Events);
        return events;
    }

    private static List<string> SuLines(string recording)
    {
        string path = RecordedCaptureFactAttribute.Resolve(recording)!;
        using IPacketSource source = CaptureFile.Open(path);
        var lines = new List<string>();
        var engine = new GameTrafficCaptureEngine(source, NosTaleWorldFramer.Factory(DataSourceKind.Cached));
        engine.FrameProduced += frame =>
        {
            if (frame.Frame.Source == DataSourceKind.Unknown)
                return;
            foreach (string line in NosTaleWorldDecoder.Decode(frame.Frame.Body.Span))
            {
                string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length > 0 && tokens[0] == "su")
                    lines.Add(line);
            }
        };
        engine.Run();
        return lines;
    }

    /// <summary>The skill vnum the decoder publishes for one <c>su</c> line.</summary>
    private static int? DecodeSkill(string line)
    {
        // A fresh decoder per line suffices: the skill vnum is read straight from
        // field 5 and needs no accumulated state.
        var decoder = new NosTaleWorldProtocolDecoder();
        DecodedObservations decoded = decoder.Decode(new ObservedPacket(
            new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc),
            NetworkDirection.Inbound, "79.110.84.175", 4002,
            Encoding.ASCII.GetBytes(line), DataSourceKind.Cached));
        return Assert.Single(decoded.Events).SkillVnum;
    }
}
