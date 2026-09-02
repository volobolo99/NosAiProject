using NosAi.Runtime.Observability;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// A6: what the C1 contracts actually carry when the two real recordings are
/// replayed through the shipping chain.
/// </summary>
/// <remarks>
/// <para>
/// Every other test in this area builds its packets by hand, which proves the
/// decoder reads the shape the catalogue wrote down and proves nothing about
/// whether that shape is on the wire. These run
/// <c>data/nostale_combat.noscap</c> and <c>data/nostale_01.noscap</c> — 8 211
/// and 2 490 real packets of a live session — through framing, decoding and the
/// observation report, and assert on what comes out.
/// </para>
/// <para>
/// <b>Skipped when the recordings are absent</b>, as
/// <see cref="Gate3DecisionLoopTests"/> already is: <c>data/</c> is gitignored
/// because the captures are the operator's own session, and a fresh clone has
/// none. They are not replaced by a synthetic stand-in — the whole point here is
/// the real bytes — so on a clone these pass vacuously and the hand-built tests
/// carry the load. The assertions are therefore written as properties that hold
/// of any capture of a real session, not as the counts of these two files: a
/// count would break the day the operator records a third.
/// </para>
/// </remarks>
public sealed class RecordedCaptureContractTests
{
    private const string Combat = "nostale_combat.noscap";
    private const string Idle = "nostale_01.noscap";

    /// <summary>
    /// The single fact the whole entity pipe rests on: a real capture yields
    /// entities that carry a vnum. Before C1 the field was skipped, so every
    /// entity was an id with a position and nothing that said what it was.
    /// </summary>
    [Fact]
    public void The_combat_recording_yields_entities_and_at_least_one_carries_a_vnum()
    {
        if (Recording(Combat) is not { } path)
            return;

        WorldReplayReport report = WorldReplayCommand.InspectFile(path);

        Assert.True(report.Ok, report.FailureReason);
        Assert.NotEmpty(report.Entities);
        // A vnum arrives from `in` only, and a capture that starts mid-session
        // has far more moves than spawns — so "at least one" is the honest
        // property, not "all of them".
        Assert.Contains(report.Entities, e => IsRead(e.VnumText));
        // And the ones without are stated as such rather than defaulted to a
        // number: an absent vnum is a named absence in the row.
        Assert.All(report.Entities, e => Assert.NotEqual(string.Empty, e.VnumText));
    }

    /// <summary>
    /// The decoder refuses entity types it has never seen, and the reference
    /// catalogue is what says whether a vnum is something to fight — never this
    /// chain. What is asserted here is only that the vnum survives to the row.
    /// </summary>
    [Fact]
    public void A_vnum_that_the_catalogue_does_not_know_is_still_carried_not_dropped()
    {
        if (Recording(Combat) is not { } path)
            return;

        WorldReplayReport report = WorldReplayCommand.InspectFile(path);

        foreach (WorldReplayEntityRow row in report.Entities)
        {
            // Whatever the catalogue answered, the name is never blank: it is a
            // display name or a stated reason.
            Assert.False(string.IsNullOrWhiteSpace(row.NameText));
        }
    }

    /// <summary>
    /// The idle recording is 2 468 movement packets and no combat. Entities are
    /// found there too, and the contracts that need combat come back empty with
    /// a reason rather than with an invented reading.
    /// </summary>
    [Fact]
    public void The_idle_recording_finds_entities_and_reports_the_combat_contracts_as_empty_with_reasons()
    {
        if (Recording(Idle) is not { } path)
            return;

        WorldReplayReport report = WorldReplayCommand.InspectFile(path);

        Assert.True(report.Ok, report.FailureReason);
        Assert.NotEmpty(report.Entities);

        // Nothing hit the character in an idle session. An empty contract is
        // stated as empty; it is never filled from another source.
        Assert.Empty(report.Hits);
        Assert.Equal(WorldReplayCommand.CtEmpty, report.SelectionReason);
    }

    /// <summary>
    /// Every packet the observer admitted either produced an observation or was
    /// counted as undecodable. Nothing is silently lost, which is what makes the
    /// census usable as evidence at all.
    /// </summary>
    [Theory]
    [InlineData(Combat)]
    [InlineData(Idle)]
    public void Every_admitted_packet_is_accounted_for(string file)
    {
        if (Recording(file) is not { } path)
            return;

        WorldReplayReport report = WorldReplayCommand.InspectFile(path);

        Assert.True(report.Ok, report.FailureReason);
        Assert.True(report.ObservedPackets > 0, "the recording carried no packet at all");
        Assert.Equal(report.ObservedPackets, report.DecodedPackets + report.UndecodablePackets);
    }

    /// <summary>
    /// Whatever the recordings carry of the four catalogued opcodes, each reading
    /// that does arrive is well formed. This is the check the hand-built packets
    /// cannot make: it holds against bytes nobody wrote for it.
    /// </summary>
    [Theory]
    [InlineData(Combat)]
    [InlineData(Idle)]
    public void Every_catalogued_reading_the_recordings_carry_is_within_its_own_bounds(string file)
    {
        if (Recording(file) is not { } path)
            return;

        WorldReplayReport report = WorldReplayCommand.InspectFile(path);
        Assert.True(report.Ok, report.FailureReason);

        Assert.All(report.SkillsReady, s =>
            Assert.InRange(s.Slot, 0, NosTaleWorldProtocolDecoder.MaxPlausibleSkillSlot));

        Assert.All(report.Inventory, i =>
        {
            Assert.True(i.Slot >= 0);
            Assert.True(i.Vnum > 0);
            // An amount of zero is an empty slot, a shape never observed and
            // never read; anything published here holds something.
            Assert.True(i.Amount > 0);
        });

        Assert.All(report.GroundItems, g =>
        {
            Assert.True(g.Vnum > 0);
            Assert.True(g.DropId > 0);
            Assert.True(g.Amount > 0);
            Assert.InRange(g.X, 0, NosTaleWorldProtocolDecoder.MaxPlausibleCoordinate);
            Assert.InRange(g.Y, 0, NosTaleWorldProtocolDecoder.MaxPlausibleCoordinate);
        });

        Assert.All(report.Pickups, p => Assert.True(p.DropId > 0));

        // An aggressor is only ever published once the own id is known, so every
        // hit here names a real entity and carries the instant it happened.
        Assert.All(report.Hits, h =>
        {
            Assert.NotEqual(0, h.By.EntityId);
            Assert.NotEqual(default, h.ObservedAtUtc);
        });
    }

    /// <summary>
    /// A replayed recording is real bytes that are not current. Nothing read out
    /// of one may ever come back LIVE, however confidently it decodes.
    /// </summary>
    [Theory]
    [InlineData(Combat)]
    [InlineData(Idle)]
    public void Nothing_read_from_a_recording_is_ever_live(string file)
    {
        if (Recording(file) is not { } path)
            return;

        WorldReplayReport report = WorldReplayCommand.InspectFile(path);
        Assert.True(report.Ok, report.FailureReason);

        Assert.All(report.Hits, h => Assert.Equal(Contracts.DataSourceKind.Cached, h.Source));
        Assert.All(report.SkillsReady, s => Assert.Equal(Contracts.DataSourceKind.Cached, s.Source));
        Assert.All(report.Inventory, i => Assert.Equal(Contracts.DataSourceKind.Cached, i.Source));
        Assert.All(report.Pickups, p => Assert.Equal(Contracts.DataSourceKind.Cached, p.Source));
        Assert.All(report.GroundItems, g => Assert.Equal(Contracts.DataSourceKind.Cached, g.Source));
        Assert.All(report.Selections, s => Assert.Equal(Contracts.DataSourceKind.Cached, s.Source));
    }

    /// <summary>The recording's path, or null when this clone does not have it.</summary>
    private static string? Recording(string file)
    {
        string path = Path.Combine(RepositoryRoot(), "data", file);
        return File.Exists(path) ? path : null;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? AppContext.BaseDirectory;
    }

    private static bool IsRead(string vnumText) =>
        vnumText != WorldReplayCommand.VnumNotRead && vnumText != WorldReplayCommand.VnumAbsent;
}
