using System.Globalization;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Observability;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// AP-05/A2+A4, le misure della Parte A che hanno concluso, asserite sui byte
/// reali delle registrazioni. I numeri sono misurati, non attesi: ogni conteggio è
/// asserito perché un test che itera zero elementi passerebbe a vuoto.
/// </summary>
public sealed class SkillWireMeasureTests
{
    // The catalogue's own values, guarded by SkillCatalogueTests against a client
    // update. The wire measures below rest on these; they are repeated here, not
    // re-decoded, so a change to the table shows up in that test first.
    private static readonly Dictionary<int, int> Type1 = new()
    {
        [200] = 0, [220] = 0, [222] = 2, [223] = 3, [224] = 4, [226] = 6, [228] = 8,
    };
    private static readonly Dictionary<int, int> Type2 = new()
    {
        [200] = 0, [220] = 1, [222] = 1, [223] = 1, [224] = 1, [226] = 1, [228] = 1,
    };
    private static readonly Dictionary<int, int> Data5 = new()
    {
        [200] = 6, [220] = 7, [222] = 50, [223] = 100, [224] = 250, [226] = 250, [228] = 100,
    };

    // A2: `sr <n>` names the position TYPE[1] of a skill used in the same capture.

    [RecordedCaptureFact("nostale_combat.noscap")]
    public void A2_sr_slots_match_the_used_skills_positions_in_combat()
    {
        List<WireTimelineEntry> entries = Timeline("nostale_combat.noscap");

        var srSlots = new HashSet<int>();
        var usedSkills = new HashSet<int>();
        foreach (WireTimelineEntry e in entries)
        {
            string[] f = e.Line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (f[0] == "sr" && f.Length > 1)
                srSlots.Add(int.Parse(f[1], CultureInfo.InvariantCulture));
            else if (PlayerSkill(f, out int skill))
                usedSkills.Add(skill);
        }

        Assert.Equal(new[] { 0, 2, 6 }, srSlots.OrderBy(x => x).ToArray());
        Assert.Equal(new[] { 220, 222, 226 }, usedSkills.OrderBy(x => x).ToArray());
        // Every sr slot is a used skill's position, and vice versa.
        Assert.Equal(
            srSlots.OrderBy(x => x),
            usedSkills.Select(s => Type1[s]).Distinct().OrderBy(x => x));
    }

    // A3: DATA[5] is the cooldown in tenths of a second, measured on two skills
    // with different DATA[5] (222 -> 50, 226 -> 250).

    [RecordedCaptureFact("nostale_combat.noscap")]
    public void A3_cooldown_is_data5_in_tenths_of_a_second()
    {
        List<WireTimelineEntry> entries = Timeline("nostale_combat.noscap");

        List<long> twoTwoTwo = Cooldowns(entries, 222);
        List<long> twoTwoSix = Cooldowns(entries, 226);

        Assert.NotEmpty(twoTwoTwo);
        Assert.NotEmpty(twoTwoSix);

        // The unit is a tenth of a second: 50 -> ~5 s, 250 -> ~25 s.
        Assert.All(twoTwoTwo, ms => Assert.InRange(ms / 50, 96, 104));
        Assert.All(twoTwoSix, ms => Assert.InRange(ms / 250, 96, 104));
    }

    // A5: TARGET[3] separates "one hit" (0/1) from "many" (3): 226 hits 11 per cast.

    [RecordedCaptureFact("nostale_combat.noscap")]
    public void A5_the_area_skill_produces_many_hits_per_cast_and_the_single_target_ones_do_not()
    {
        List<WireTimelineEntry> entries = Timeline("nostale_combat.noscap");

        List<int> twoTwoZero = SuPerCast(entries, 220);
        List<int> twoTwoSix = SuPerCast(entries, 226);

        Assert.NotEmpty(twoTwoZero);
        Assert.NotEmpty(twoTwoSix);
        // 220 is single-target (TARGET[3]=0): exactly one hit per cast.
        Assert.All(twoTwoZero, count => Assert.Equal(1, count));
        // 226 is the area skill (TARGET[3]=3): more than one hit per cast.
        Assert.All(twoTwoSix, count => Assert.True(count > 1));
        Assert.Contains(11, twoTwoSix);
    }

    // A6: TYPE[2] is the class; each observed player uses exactly one.

    [RecordedCaptureTheory(
        "nostale_combat.noscap", "nostale_live.noscap", "certificazione.noscap",
        "messaggi.noscap", "calibrate_20260903_091408Z_round1.noscap",
        "calibrate_20260903_091428Z_round2.noscap")]
    [InlineData(true)]
    public void A6_each_player_uses_skills_of_one_class_only(bool _)
    {
        var perPlayer = new Dictionary<long, HashSet<int>>();
        foreach (string recording in new[]
        {
            "nostale_combat.noscap", "nostale_live.noscap", "certificazione.noscap",
            "messaggi.noscap", "calibrate_20260903_091408Z_round1.noscap",
            "calibrate_20260903_091428Z_round2.noscap",
        })
        {
            foreach (WireTimelineEntry e in Timeline(recording))
            {
                string[] f = e.Line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (f[0] == "su" && f.Length > 5 && f[1] == "1"
                    && int.TryParse(f[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int skill)
                    && Type2.ContainsKey(skill))
                {
                    long player = long.Parse(f[2], CultureInfo.InvariantCulture);
                    if (!perPlayer.TryGetValue(player, out var set))
                        perPlayer[player] = set = new HashSet<int>();
                    set.Add(Type2[skill]);
                }
            }
        }

        // Two distinct players observed, each with exactly one class.
        Assert.Equal(2, perPlayer.Count);
        Assert.All(perPlayer.Values, set => Assert.Single(set));
        Assert.Equal(new long[] { 3443217, 3548294 }, perPlayer.Keys.OrderBy(k => k).ToArray());
        Assert.Equal(1, Assert.Single(perPlayer[3443217]));
        Assert.Equal(0, Assert.Single(perPlayer[3548294]));
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Cooldown in ms from each use of <paramref name="skill"/> to its next <c>sr</c>.</summary>
    private static List<long> Cooldowns(List<WireTimelineEntry> entries, int skill)
    {
        int slot = Type1[skill];
        var deltas = new List<long>();
        for (int i = 0; i < entries.Count; i++)
        {
            string[] f = entries[i].Line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (!PlayerSkill(f, out int s) || s != skill)
                continue;
            for (int j = i + 1; j < entries.Count; j++)
            {
                string[] g = entries[j].Line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (g[0] == "sr" && g.Length > 1
                    && int.Parse(g[1], CultureInfo.InvariantCulture) == slot)
                {
                    deltas.Add((long)(entries[j].TimestampUtc - entries[i].TimestampUtc).TotalMilliseconds);
                    break;
                }
            }
        }
        return deltas;
    }

    /// <summary>Hits (<c>su</c>) with the same vnum following each player <c>ct</c> of it.</summary>
    private static List<int> SuPerCast(List<WireTimelineEntry> entries, int skill)
    {
        var counts = new List<int>();
        for (int i = 0; i < entries.Count; i++)
        {
            string[] f = entries[i].Line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (f[0] != "ct" || f.Length <= 7 || f[1] != "1") continue;
            if (!int.TryParse(f[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out int castSkill)
                || castSkill != skill)
                continue;

            int count = 0;
            for (int j = i + 1; j < entries.Count; j++)
            {
                string[] g = entries[j].Line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (g[0] == "ct" && g[1] == "1") break;
                if (g[0] == "su" && g.Length > 5 && g[1] == "1"
                    && int.TryParse(g[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int hitSkill)
                    && hitSkill == skill)
                    count++;
            }
            counts.Add(count);
        }
        return counts;
    }

    private static bool PlayerSkill(string[] f, out int skill)
    {
        skill = 0;
        if (f[0] == "su" && f.Length > 5 && f[1] == "1")
            return int.TryParse(f[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out skill) && skill > 0;
        if (f[0] == "ct" && f.Length > 7 && f[1] == "1")
            return int.TryParse(f[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out skill) && skill > 0;
        return false;
    }

    private static List<WireTimelineEntry> Timeline(string recording)
    {
        string path = RecordedCaptureFactAttribute.Resolve(recording)!;
        using IPacketSource source = CaptureFile.Open(path);
        return WireInspectCommand.Timeline(source, DataSourceKind.Cached, opcodes: null, maxLines: 0).ToList();
    }
}
