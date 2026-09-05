using NosAi.Core.WorldModel;
using Xunit;
using static NosAi.Core.Tests.WorldModel.WorldModelFixtures;

namespace NosAi.Core.Tests.WorldModel;

public sealed class WorldModelCanonicalTextTests
{
    [Fact]
    public void EveryFactAppearsExactlyOnce()
    {
        var snapshot = Populated();
        var text = WorldModelCanonicalText.Write(snapshot);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var factLines = lines.Count(l => l.Contains("=LIVE|") || l.Contains("=DERIVED|") || l.Contains("=CACHED|") || l.Contains("=SIMULATED|") || l.Contains("=UNKNOWN|"));
        Assert.Equal(snapshot.Facts().Count(), factLines);
        Assert.Equal(lines.Length, lines.Distinct(StringComparer.Ordinal).Count());
        Assert.StartsWith("schema=1\nrevision=7\nobservedAt=1788602401000\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownIsWrittenAsEmptySetNeverAsZero()
    {
        var text = WorldModelCanonicalText.Write(WorldModelSnapshot.Empty(T0));

        Assert.Contains("player.hp=UNKNOWN|0|0|1788602400000|∅|not observed\n", text);
        Assert.DoesNotContain("=LIVE|", text);
        Assert.DoesNotContain("|0/0|", text);
    }

    [Fact]
    public void KnownFactCarriesProvenanceConfidenceAndTimestamp()
    {
        var text = WorldModelCanonicalText.Write(Populated());

        Assert.Contains("player.hp=LIVE|1|1|1788602400250|812/1000|\n", text);
        Assert.Contains("map.name=DERIVED|3|0.8|1788602400250|NosVille|ocr\n", text);
        Assert.Contains("goal[g-fox-hunt].progress=DERIVED|3|0.8|1788602400250|0.4|ocr\n", text);
    }

    [Fact]
    public void SeparatorsInsideValuesAreEscaped()
    {
        var a = Populated();
        var hostile = a with
        {
            Player = a.Player with { Name = Net("a|b=c\nd[e]\\f") },
            Quests = [a.Quests[0] with { Id = new QuestId("q|1") }]
        };

        var text = WorldModelCanonicalText.Write(hostile);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("player.name=LIVE|1|1|1788602400250|a\\|b\\=c\\nd\\[e\\]\\\\f|", lines);
        Assert.Contains(lines, l => l.StartsWith("quest[q\\|1].title=", StringComparison.Ordinal));
        Assert.Equal(lines.Length, WorldModelCanonicalText.Write(a).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void DigestIsPinnedForReplay()
    {
        // Pinned on 2026-09-05 against the AP-01 fixture. A change here means the
        // canonical form changed: bump WorldModelContract.SchemaVersion and re-pin.
        Assert.Equal(0x2D9BF29FBE8B0DC9UL, Populated().ComputeDigest());
        Assert.Equal(0xED1653B61EA4DB38UL, WorldModelSnapshot.Empty(T0).ComputeDigest());
    }

    [Fact]
    public void DigestIsStableAcrossRepeatedComputation()
    {
        var snapshot = Populated();
        var first = snapshot.ComputeDigest();

        for (var i = 0; i < 50; i++)
        {
            Assert.Equal(first, snapshot.ComputeDigest());
        }
    }

    [Fact]
    public void DigestDistinguishesRevisionAndTimestamp()
    {
        var a = Populated();

        Assert.NotEqual(a.ComputeDigest(), (a with { Revision = 8 }).ComputeDigest());
        Assert.NotEqual(a.ComputeDigest(), (a with { ObservedAtUnixMillis = T2 + 1 }).ComputeDigest());
    }

    [Fact]
    public void NullSnapshotIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => WorldModelCanonicalText.Write(null!));
    }
}
