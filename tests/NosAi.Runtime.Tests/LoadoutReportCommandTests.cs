using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Loadout;
using NosAi.Runtime.GameData;
using NosAi.Runtime.Tactical;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Q-094 (AP-07): <see cref="LoadoutReportCommand"/>'s two testable halves.
/// <see cref="LoadoutReportCommand.Build"/> is pure -- it runs the three
/// <see cref="LoadoutPlanner"/> generators and
/// <see cref="LoadoutPlanner.CheckHardConstraints"/> over a hand-built
/// <see cref="Player"/> and a fake slot resolver, exactly the pattern
/// <c>LoadoutPlannerTests</c> already uses (no desktop, no real time, no
/// <c>ClientMemorySession</c>). <see cref="LoadoutReportCommand.BuildResolveSlot"/>
/// is tested against a real in-memory <see cref="GameReferenceDatabase"/>
/// seeded the same way <c>GameReferenceDatabaseTests</c> seeds one, with the
/// <c>"item"</c> kind its own <c>Import</c> helper hardcodes away.
/// <see cref="LoadoutReportCommand.Run"/> is tested only on its off-Windows
/// branch, the way <c>InputGuardsProbeTests</c>/<c>ClientWindowDpiProbeTests</c>
/// already test theirs; the Windows path needs a real attached client and is
/// not exercised here. An earlier version of this remark claimed no other
/// command's console entry is tested at all -- false, and corrected:
/// <c>CollectCommandTests</c>, <c>EngageCommandTests</c>,
/// <c>RecoverCommandTests</c> and <c>AutoplayCommandTests</c> all call their
/// own <c>Run</c>.
/// </summary>
public sealed class LoadoutReportCommandTests
{
    private static readonly DateTime Now = DateTime.UnixEpoch;

    // ------------------------------------------------------------- helpers

    private static Player BuildPlayer(
        EquatableArray<EquipmentItem>? equipment = null,
        EquatableArray<InventoryItem>? inventory = null) =>
        new(
            new EntityId("player-1"),
            WorldFact<WorldPosition>.Unknown("r", Now),
            WorldFact<float>.Unknown("r", Now),
            WorldFact<bool>.Live(true, 1d, Now),
            WorldFact<MapId>.Live(new MapId("map-1"), 1d, Now),
            CombatantStatus.Empty,
            EquatableArray<Skill>.Empty,
            EquatableArray<Cooldown>.Empty,
            inventory ?? EquatableArray<InventoryItem>.Empty,
            equipment ?? EquatableArray<EquipmentItem>.Empty);

    private static EquipmentItem BuildEquipped(string id, EquipmentSlot slot, bool equipped = true) =>
        new(new ItemId(id), WorldFact<string>.Live(id, 1d, Now), slot, WorldFact<bool>.Live(equipped, 1d, Now));

    private static InventoryItem BuildStack(string id, int quantity) =>
        new(new ItemId(id), WorldFact<string>.Live(id, 1d, Now), WorldFact<int>.Live(quantity, 1d, Now), WorldFact<int>.Live(0, 1d, Now));

    private static LoadoutConstraintCheck Single(
        LoadoutReportCommand.LoadoutReport report, LoadoutActionKind kind)
    {
        IReadOnlyList<LoadoutConstraintCheck> checks = kind switch
        {
            LoadoutActionKind.Equip => report.EquipChecks,
            LoadoutActionKind.Unequip => report.UnequipChecks,
            LoadoutActionKind.Upgrade => report.UpgradeChecks,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        return Assert.Single(checks);
    }

    // ------------------------------------------------------------- Build

    [Fact]
    public void Build_ResolvableStackIntoFreeSlot_ProducesAnAllowedEquipCheck()
    {
        Player player = BuildPlayer(inventory: EquatableArray<InventoryItem>.From(new[] { BuildStack("12", 1) }));

        LoadoutReportCommand.LoadoutReport report = LoadoutReportCommand.Build(player, id => id == new ItemId("12") ? EquipmentSlot.Weapon : null);

        LoadoutConstraintCheck check = Single(report, LoadoutActionKind.Equip);
        Assert.Equal(LoadoutActionKind.Equip, check.Candidate.Kind);
        Assert.Equal(new ItemId("12"), check.Candidate.Item);
        Assert.Equal(EquipmentSlot.Weapon, check.Candidate.Slot);
        Assert.True(check.IsAllowed, string.Join(", ", check.ViolatedConstraints));
    }

    [Fact]
    public void Build_ResolvableStackIntoOccupiedSlot_ProducesAViolatedEquipCheck()
    {
        // The slot is taken by an already-equipped item, so the hard
        // constraint stage must flag slot_already_occupied -- proving the
        // check actually ran against this player.
        Player player = BuildPlayer(
            equipment: EquatableArray<EquipmentItem>.From(new[] { BuildEquipped("old", EquipmentSlot.Hat) }),
            inventory: EquatableArray<InventoryItem>.From(new[] { BuildStack("new", 1) }));

        LoadoutReportCommand.LoadoutReport report = LoadoutReportCommand.Build(player, id => id == new ItemId("new") ? EquipmentSlot.Hat : null);

        LoadoutConstraintCheck check = Single(report, LoadoutActionKind.Equip);
        Assert.False(check.IsAllowed);
        Assert.Contains("slot_already_occupied", check.ViolatedConstraints);
    }

    [Fact]
    public void Build_EquippedItem_ProducesAnUnequipAndAnUpgradeCheck()
    {
        Player player = BuildPlayer(equipment: EquatableArray<EquipmentItem>.From(new[] { BuildEquipped("sword", EquipmentSlot.Weapon) }));

        LoadoutReportCommand.LoadoutReport report = LoadoutReportCommand.Build(player, _ => null);

        LoadoutConstraintCheck unequip = Single(report, LoadoutActionKind.Unequip);
        Assert.Equal(EquipmentSlot.Weapon, unequip.Candidate.Slot);
        Assert.True(unequip.IsAllowed, string.Join(", ", unequip.ViolatedConstraints));

        LoadoutConstraintCheck upgrade = Single(report, LoadoutActionKind.Upgrade);
        Assert.Equal(new ItemId("sword"), upgrade.Candidate.Item);
        Assert.True(upgrade.IsAllowed, string.Join(", ", upgrade.ViolatedConstraints));
    }

    // ----------------------------------------------- BuildResolveSlot

    /// <summary>
    /// Seeding an item record for the resolver, with the <c>"item"</c> kind
    /// this test needs (the <c>Import</c> helper in GameReferenceDatabaseTests
    /// hardcodes <c>"monster"</c>, so this test writes its own call).
    /// ItemReferenceDecoder reads the slot from the INDEX field's 4th value
    /// (position 3), exactly as ItemReferenceDecoderTests proves with its own
    /// seeded records -- this layout is not invented here.
    /// </summary>
    private static void SeedItem(GameReferenceDatabase database, int vnum, string slotCode)
    {
        // The five values around position 3 are deliberately distinct from each
        // other and from every slot code a test asserts, so a decoder reading
        // the wrong position cannot accidentally produce the expected answer.
        var record = new NosRecord(vnum, new[]
        {
            new NosField("VNUM", new[] { vnum.ToString() }),
            new NosField("INDEX", new[] { "90", "91", "92", slotCode, "94", "95" }),
        });
        database.Import("item", "test.NOS", "item.dat", "C:/test", new[] { record },
            System.Text.Encoding.UTF8.GetBytes($"payload-item-{vnum}-{Guid.NewGuid()}"));
    }

    /// <summary>
    /// <see cref="EquipmentSlot.Gloves"/> is 3 -- a code that is neither the
    /// enum's default nor equal to any neighbouring INDEX value, so this
    /// asserts the decoder really reads position 3 and really maps the code.
    /// </summary>
    [Theory]
    [InlineData(EquipmentSlot.Gloves)]
    [InlineData(EquipmentSlot.Weapon)]
    [InlineData(EquipmentSlot.MiniPet)]
    public void BuildResolveSlot_KnownVnum_DecodesItsSeededSlot(EquipmentSlot expected)
    {
        using GameReferenceDatabase database = GameReferenceDatabase.OpenInMemory();
        SeedItem(database, vnum: 12, slotCode: ((int)expected).ToString());

        Func<ItemId, EquipmentSlot?> resolveSlot = LoadoutReportCommand.BuildResolveSlot(database);

        Assert.Equal(expected, resolveSlot(new ItemId("12")));
    }

    [Fact]
    public void BuildResolveSlot_VnumAbsentFromCatalog_ResolvesToNull()
    {
        using GameReferenceDatabase database = GameReferenceDatabase.OpenInMemory();
        SeedItem(database, vnum: 12, slotCode: ((int)EquipmentSlot.Weapon).ToString());

        Func<ItemId, EquipmentSlot?> resolveSlot = LoadoutReportCommand.BuildResolveSlot(database);

        // Lookup returns null for a vnum the catalogue does not know; the
        // resolver propagates that null instead of inventing a slot.
        Assert.Null(resolveSlot(new ItemId("9999")));
    }

    [Fact]
    public void BuildResolveSlot_NonIntegerItemId_ResolvesToNullWithoutThrowing()
    {
        using GameReferenceDatabase database = GameReferenceDatabase.OpenInMemory();
        SeedItem(database, vnum: 12, slotCode: ((int)EquipmentSlot.Weapon).ToString());

        Func<ItemId, EquipmentSlot?> resolveSlot = LoadoutReportCommand.BuildResolveSlot(database);

        // A wire inventory id is always a numeric vnum string, but an id that
        // is not one must resolve to null, never throw and never guess.
        Assert.Null(resolveSlot(new ItemId("not-a-vnum")));
    }

    [Fact]
    public void BuildResolveSlot_TheNotEquippableSlotCode_ResolvesToNull()
    {
        using GameReferenceDatabase database = GameReferenceDatabase.OpenInMemory();
        // The catalogue's own -1 ("cannot be equipped") decodes to null rather
        // than a fabricated member -- ItemReferenceDecoderTests proves the
        // decode side; this proves the wiring keeps the null.
        SeedItem(database, vnum: 13, slotCode: "-1");

        Func<ItemId, EquipmentSlot?> resolveSlot = LoadoutReportCommand.BuildResolveSlot(database);

        Assert.Null(resolveSlot(new ItemId("13")));
    }

    // ------------------------------------------------- console entry, off Windows

    /// <summary>
    /// The one branch of <see cref="LoadoutReportCommand.Run"/> that needs no
    /// client: off Windows it refuses before any window lookup or memory
    /// attach, the same shape <c>InputGuardsProbeTests.ProbeRefusesOffWindows</c>
    /// pins for its own probe.
    /// </summary>
    [Fact]
    public void Run_OffWindows_RefusesWithoutTouchingTheClient()
    {
        if (OperatingSystem.IsWindows())
            return;

        Assert.Equal(NosAi.Runtime.Navigation.WalkCommand.ExitAbandoned, LoadoutReportCommand.Run());
    }

    /// <summary>
    /// The two refusal reasons are part of the operator-visible contract, so
    /// they are pinned here rather than left to whoever next edits the strings.
    /// </summary>
    [Fact]
    public void RefusalReasons_AreTheNamedIdentifiersTheOperatorSees()
    {
        Assert.Equal("loadout_report_requires_windows", LoadoutReportCommand.NotWindowsReason);
        Assert.Equal("loadout_report_gameplay_provider_unavailable", LoadoutReportCommand.GameplayUnavailableReason);
        Assert.Equal("--loadout-report", LoadoutReportCommand.Flag);
    }
}
