using System.IO;
using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Combat;
using NosAi.LiveIntegration;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Navigation;
using NosAi.Runtime.Tactical;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// AP-05/A4: <see cref="EngageCommand.ExecuteOneRound"/> -- one operator-named
/// <c>UseSkill</c> act, executed through an injected keybind map and input
/// backend and verified through injected before/after vitals reads (no
/// desktop, no real time, no <c>ClientMemorySession</c>). Covers: a confirmed
/// keybind with an accepted press and falling Mp →
/// <c>ResourceCostConfirmed</c>; the same with unchanged Mp →
/// <c>NoResourceChangeObserved</c>; an unconfirmed/missing keybind →
/// <c>NotAttempted</c> with the key press never reaching the backend; a
/// non-<c>UseSkill</c> kind → <c>NotAttempted</c> without reading vitals or
/// touching the input backend at all.
/// </summary>
[Collection(ConsoleCaptureCollection.Name)]
public sealed class EngageCommandTests
{
    private static readonly DateTime Now = new(2026, 9, 6, 9, 30, 0, DateTimeKind.Utc);
    private static readonly ActuationAuthority EngageAuthority = ActuationAuthority.Commanded(EngageCommand.Flag);
    private static readonly EntityId Target = new("mob-1");
    private static readonly SkillId Skill = new("201");

    // ------------------------------------------------------------- helpers

    private static CombatActionCandidate UseSkillCandidate() =>
        new(CombatActionKind.UseSkill, target: Target, skill: Skill);

    private static CombatActionCandidate BasicAttackCandidate() =>
        new(CombatActionKind.BasicAttack, target: Target);

    private static PlayerVitalsReading Vitals(uint mp) => new(Hp: 100, MaxHp: 100, Mp: mp, MaxMp: 100);

    private static KeybindMap ConfirmedMap(string intent = "skill.201", ushort virtualKey = 112)
    {
        // KeybindMap's dictionary is private and it only parses from JSON, so a
        // confirmed map is built through the same public loader the production
        // path uses, on a small in-memory file.
        return MapFromJson($$"""
            {
              "version": 1,
              "binds": {
                "{{intent}}": { "virtualKey": {{virtualKey}}, "label": "F1", "confirmed": true }
              }
            }
            """);
    }

    private static KeybindMap MapFromJson(string json)
    {
        string path = Path.Combine(Path.GetTempPath(), "nosai-engage-keybinds-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        try
        {
            Assert.True(KeybindMap.TryLoad(path, out KeybindMap map, out string? reason), reason);
            return map;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class RecordingInput : IInputBackend
    {
        public List<(ushort VirtualKey, int PressDurationMs)> Presses { get; } = new();

        public bool IsLive => false;
        public bool TryGetCursorPosition(out int x, out int y) { x = 0; y = 0; return true; }
        public bool MoveRelative(int dx, int dy) => true;
        public bool MoveAbsolute(int x, int y) => true;
        public bool Click(MouseButton button, int delayBetweenDownUpMs = 45) => true;
        public bool KeyPress(ushort virtualKey, int pressDurationMs = 80, ReadOnlySpan<ushort> modifiers = default)
        {
            Presses.Add((virtualKey, pressDurationMs));
            return Accepted;
        }
        public bool ScrollWheel(int detents) => true;

        public bool Accepted { get; set; } = true;
    }

    private static CombatExecutionEvidence RunRound(
        CombatActionCandidate candidate,
        KeybindMap keybinds,
        RecordingInput input,
        PlayerVitalsReading? before,
        PlayerVitalsReading? after,
        ActuationAuthority? authority = null)
    {
        var reads = new Queue<PlayerVitalsReading?>(new[] { before, after });
        return EngageCommand.ExecuteOneRound(
            candidate,
            keybinds,
            input,
            readVitals: () => reads.Count > 0 ? reads.Dequeue() : null,
            verificationDelay: () => { }, // no real time in tests
            in EngageAuthority,
            Now);
    }

    // -------------------------------------------------- confirmed cost

    [Fact]
    public void UseSkillWithConfirmedKeybindAcceptedPressAndFallingMp_ConfirmsResourceCost()
    {
        RecordingInput input = new();
        CombatExecutionEvidence evidence = RunRound(
            UseSkillCandidate(),
            ConfirmedMap(),
            input,
            before: Vitals(mp: 100),
            after: Vitals(mp: 60));

        Assert.True(evidence.ResourceCostConfirmed);
        Assert.Equal(CombatExecutionResult.ResourceCostConfirmed, evidence.Result);
        Assert.Equal(ResourceKind.Mana, evidence.ResourceObserved);
        Assert.Null(evidence.Detail);
        Assert.True(evidence.Before.HasValue);
        Assert.True(evidence.After.HasValue);
        Assert.Equal(100d, evidence.Before.Value);
        Assert.Equal(60d, evidence.After.Value);
        Assert.Equal(NosAi.Core.WorldModel.DataSourceKind.Live, evidence.Before.Source);
        Assert.Equal(NosAi.Core.WorldModel.DataSourceKind.Live, evidence.After.Source);

        // The press went out exactly once, for the confirmed key, with the
        // command's press duration.
        (ushort virtualKey, int duration) = Assert.Single(input.Presses);
        Assert.Equal(112, virtualKey);
        Assert.Equal(80, duration);
    }

    [Fact]
    public void UseSkillWithConfirmedKeybindAndUnchangedMp_ReportsNoResourceChangeObserved()
    {
        RecordingInput input = new();
        CombatExecutionEvidence evidence = RunRound(
            UseSkillCandidate(),
            ConfirmedMap(),
            input,
            before: Vitals(mp: 100),
            after: Vitals(mp: 100));

        Assert.Equal(CombatExecutionResult.NoResourceChangeObserved, evidence.Result);
        Assert.False(evidence.ResourceCostConfirmed);
        Assert.Single(input.Presses);
    }

    // -------------------------------------------------- keybind refusals

    [Fact]
    public void UseSkillWithNoKeybind_IsNotAttempted_AndNeverTouchesTheInputBackend()
    {
        RecordingInput input = new();
        CombatExecutionEvidence evidence = RunRound(
            UseSkillCandidate(),
            KeybindMap.Empty,
            input,
            before: Vitals(mp: 100),
            after: Vitals(mp: 60));

        Assert.Equal(CombatExecutionResult.Aborted, evidence.Result);
        Assert.Equal("keybind_not_confirmed", evidence.Detail);
        Assert.False(evidence.Before.HasValue);
        Assert.False(evidence.After.HasValue);
        Assert.Empty(input.Presses);
    }

    [Fact]
    public void UseSkillWithDeclaredButUnconfirmedKeybind_IsNotAttempted_AndNeverTouchesTheInputBackend()
    {
        RecordingInput input = new();
        KeybindMap map = MapFromJson("""
            {
              "version": 1,
              "binds": {
                "skill.201": { "virtualKey": 112, "label": "F1", "confirmed": false }
              }
            }
            """);

        CombatExecutionEvidence evidence = RunRound(
            UseSkillCandidate(),
            map,
            input,
            before: Vitals(mp: 100),
            after: Vitals(mp: 60));

        Assert.Equal(CombatExecutionResult.Aborted, evidence.Result);
        Assert.Equal("keybind_not_confirmed", evidence.Detail);
        Assert.Empty(input.Presses);
    }

    [Fact]
    public void UseSkillWithAWrongSkillIntent_IsNotAttempted_WhenOnlyAnotherSkillIsBound()
    {
        // The intent is exactly "skill.{id}": a bind for a different skill id
        // must not fire this skill's press.
        RecordingInput input = new();
        KeybindMap map = ConfirmedMap(intent: "skill.202", virtualKey: 113);

        CombatExecutionEvidence evidence = RunRound(
            UseSkillCandidate(),
            map,
            input,
            before: Vitals(mp: 100),
            after: Vitals(mp: 60));

        Assert.Equal(CombatExecutionResult.Aborted, evidence.Result);
        Assert.Equal("keybind_not_confirmed", evidence.Detail);
        Assert.Empty(input.Presses);
    }

    // -------------------------------------------------- input refusals

    [Fact]
    public void UseSkillWithARefusedKeyPress_IsNotAttempted_WithTheRefusalDetail()
    {
        RecordingInput input = new() { Accepted = false };
        CombatExecutionEvidence evidence = RunRound(
            UseSkillCandidate(),
            ConfirmedMap(),
            input,
            before: Vitals(mp: 100),
            after: Vitals(mp: 60));

        Assert.Equal(CombatExecutionResult.Aborted, evidence.Result);
        Assert.Equal("key_press_not_accepted", evidence.Detail);
        Assert.Single(input.Presses); // the backend was asked; it refused
        Assert.False(evidence.Before.HasValue);
    }

    // -------------------------------------------------- kind refusal

    [Fact]
    public void BasicAttack_IsNotAttempted_WithoutReadingVitalsOrTouchingTheInputBackend()
    {
        RecordingInput input = new();
        var reads = 0;

        CombatExecutionEvidence evidence = EngageCommand.ExecuteOneRound(
            BasicAttackCandidate(),
            ConfirmedMap(),
            input,
            readVitals: () => { reads++; return Vitals(mp: 100); },
            verificationDelay: () => { },
            in EngageAuthority,
            Now);

        Assert.Equal(CombatExecutionResult.Aborted, evidence.Result);
        Assert.Equal("engage_v1_supports_useskill_only", evidence.Detail);
        Assert.Null(evidence.ResourceObserved);
        Assert.False(evidence.Before.HasValue);
        Assert.False(evidence.After.HasValue);
        Assert.Equal(0, reads); // vitals were never even asked for
        Assert.Empty(input.Presses);
    }

    [Fact]
    public void EveryNonUseSkillKind_IsRefusedBeforeAnyInput_WithTheSameNamedReason()
    {
        foreach (CombatActionKind kind in new[]
                 {
                     CombatActionKind.BasicAttack,
                     CombatActionKind.UseConsumable,
                     CombatActionKind.Reposition,
                     CombatActionKind.Flee
                 })
        {
            CombatActionCandidate candidate = kind switch
            {
                CombatActionKind.BasicAttack => new(kind, target: Target),
                CombatActionKind.UseConsumable => new(kind, item: new ItemId("item-1")),
                CombatActionKind.Reposition => new(kind, destination: new WorldPosition(3f, 4f)),
                CombatActionKind.Flee => new(kind),
                _ => throw new ArgumentOutOfRangeException()
            };

            RecordingInput input = new();
            CombatExecutionEvidence evidence = RunRound(
                candidate,
                ConfirmedMap(),
                input,
                before: Vitals(mp: 100),
                after: Vitals(mp: 60));

            Assert.Equal(CombatExecutionResult.Aborted, evidence.Result);
            Assert.Equal("engage_v1_supports_useskill_only", evidence.Detail);
            Assert.Empty(input.Presses);
        }
    }

    // -------------------------------------------------- authority

    [Fact]
    public void AMissingAuthority_IsRefusedByName_BeforeAnythingElseHappens()
    {
        RecordingInput input = new();
        var reads = 0;
        ActuationAuthority missing = default; // Kind == None, not an authority

        CombatExecutionEvidence evidence = EngageCommand.ExecuteOneRound(
            UseSkillCandidate(),
            ConfirmedMap(),
            input,
            readVitals: () => { reads++; return Vitals(mp: 100); },
            verificationDelay: () => { },
            in missing,
            Now);

        Assert.Equal(CombatExecutionResult.Aborted, evidence.Result);
        Assert.Equal(ActuationAuthority.MissingReason, evidence.Detail);
        Assert.Equal(0, reads); // nothing was read or pressed
        Assert.Empty(input.Presses);
    }

    // ------------------------------------------------------- Run(...) argument validation

    // Program.cs's dispatch only checks argument *count*, not content, so a
    // caller that resolves an entity id to an empty string (e.g. an
    // automation harness driving --engage from a not-yet-fused id) can reach
    // Run with a present-but-blank argument. It must be refused cleanly, not
    // crash the process with an unhandled exception -- the same [REFUSED]
    // boundary every other guard in this command already gives, and the only
    // part of Run/RunWindows testable without a desktop.

    /// <summary>
    /// Each guard is pinned by the reason it prints, not by its exit code.
    /// </summary>
    /// <remarks>
    /// Every refusal in this command returns
    /// <see cref="NosAi.Runtime.Navigation.WalkCommand.ExitAbandoned"/>,
    /// including the ones further down that fire when no client is attached --
    /// which is every run on a machine without the game open, i.e. every run
    /// in this suite. Asserting only the exit code therefore passed whether or
    /// not the argument guard existed at all: deleting it would have left
    /// these three green, because <c>Run</c> would have gone one step further
    /// and refused for the next reason instead. That is also why the obvious
    /// contrast -- usable arguments getting <b>past</b> the guard -- is not
    /// tested here: past the guard is <c>RunWindows</c>, which attaches to a
    /// running client and, on a machine where the game is open and WinDivert
    /// is available, presses a key. A unit test must not be one elevation
    /// away from actuating.
    /// </remarks>
    [Theory]
    [InlineData("   ", "201", 1)]
    [InlineData("mob-1", "", 1)]
    [InlineData("mob-1", "201", 0)]
    [InlineData("", "", 0)]
    public void Run_WithUnusableArguments_RefusesForThatReason_BeforeReachingTheClient(
        string targetEntityId, string skillId, int rounds)
    {
        TextWriter original = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);
        int exitCode;
        try
        {
            exitCode = EngageCommand.Run(targetEntityId, skillId, rounds);
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Equal(NosAi.Runtime.Navigation.WalkCommand.ExitAbandoned, exitCode);
        Assert.Contains(EngageCommand.InvalidArgumentsReason, captured.ToString(), StringComparison.Ordinal);
    }


    // ------------------------------------------------------------ wiring

    [Fact]
    public void TheRuntimeWiresTheEngageFlag()
    {
        string root = RepositoryRoot();
        string program = File.ReadAllText(Path.Combine(root, "src", "NosAi.Runtime", "Program.cs"));

        Assert.Contains(EngageCommand.Flag, program, StringComparison.Ordinal);
        Assert.Contains("EngageCommand.Run", program, StringComparison.Ordinal);
        Assert.Contains("\"" + EngageCommand.Flag + "\"", program, StringComparison.Ordinal);
    }

    // ------------------------------------------- target verification contract

    /// <summary>
    /// The two refusal reasons the target check adds are operator-visible and
    /// must read differently: one says "the check ran and said no", the other
    /// says "the check could not run". Collapsing them would hide the second
    /// case, which is the one that means the command is blind.
    /// </summary>
    [Fact]
    public void TheTwoTargetRefusalReasons_AreDistinctNamedIdentifiers()
    {
        Assert.Equal("engage_target_refused", EngageCommand.TargetRefusedReason);
        Assert.Equal("engage_target_not_verifiable", EngageCommand.TargetNotVerifiableReason);
        Assert.NotEqual(EngageCommand.TargetRefusedReason, EngageCommand.TargetNotVerifiableReason);
    }

    /// <summary>
    /// The decision a refused round rests on, composed the way the runtime
    /// composes it: a real world, a real
    /// <see cref="CombatPlanner.CheckTargetConstraints"/> verdict, and the
    /// refusal string the operator and the ledger are given.
    /// </summary>
    /// <remarks>
    /// The predecessor of this test built a
    /// <see cref="CombatExecutionEvidence"/> by hand and then asserted on the
    /// three things <see cref="CombatExecutionEvidence.NotAttempted"/>
    /// hardcodes, so it could not fail whatever
    /// <see cref="EngageCommand"/> did.
    /// </remarks>
    [Theory]
    [InlineData(1.0, true, true, null)]
    [InlineData(50.0, true, true, "engage_target_refused:target_out_of_range")]
    [InlineData(1.0, null, true, "engage_target_refused:target_not_hostile")]
    [InlineData(1.0, true, null, "engage_target_refused:target_not_alive")]
    [InlineData(1.0, null, null, "engage_target_refused:target_not_hostile|target_not_alive")]
    public void TheRefusalStringNamesEveryViolationTheVerdictCarried(
        double distance, bool? hostile, bool? alive, string? expected)
    {
        var now = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
        var candidate = new CombatActionCandidate(
            CombatActionKind.UseSkill, target: new EntityId("mob-1"), skill: new SkillId("7"));

        var player = new Player(
            new EntityId("player-1"),
            WorldFact<WorldPosition>.Live(new WorldPosition(0, 0), 1.0, now),
            WorldFact<float>.Unknown("orientation_not_read", now),
            WorldFact<bool>.Unknown("alive_not_read", now),
            WorldFact<MapId>.Unknown("map_not_read", now),
            CombatantStatus.Empty,
            WorldFact<EquatableArray<Skill>>.Live(EquatableArray<Skill>.Empty, 1d, now),
            WorldFact<EquatableArray<Cooldown>>.Live(EquatableArray<Cooldown>.Empty, 1d, now),
            WorldFact<EquatableArray<InventoryItem>>.Live(EquatableArray<InventoryItem>.Empty, 1d, now),
            WorldFact<EquatableArray<EquipmentItem>>.Live(EquatableArray<EquipmentItem>.Empty, 1d, now));

        var mob = new Mob(
            new EntityId("mob-1"),
            WorldFact<WorldPosition>.Live(new WorldPosition((float)distance, 0), 1.0, now),
            WorldFact<string>.Unknown("species_name_catalog_not_available", now),
            hostile is { } h ? WorldFact<bool>.Live(h, 1.0, now) : WorldFact<bool>.Unknown("hostility_never_established", now),
            alive is { } a ? WorldFact<bool>.Derived(a, 1.0, now) : WorldFact<bool>.Unknown("hp_never_stated", now),
            CombatantStatus.Empty);

        CombatConstraintCheck verdict = CombatPlanner.CheckTargetConstraints(
            candidate, player, EquatableArray<Mob>.From(new[] { mob }));

        Assert.Equal(expected, EngageCommand.DescribeTargetRefusal(verdict));
    }

    // ------------------------------------- the picture the act is decided from

    private static readonly DateTime GuardNow = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

    private static Mob MobObservedAt(DateTime at, double confidence = 1.0, string id = "mob-1") => new(
        new EntityId(id),
        WorldFact<WorldPosition>.Cached(new WorldPosition(1, 0), confidence, at),
        WorldFact<string>.Unknown("species_name_catalog_not_available", at),
        WorldFact<bool>.Live(true, 1.0, at),
        WorldFact<bool>.Derived(true, 1.0, at),
        CombatantStatus.Empty);

    private static CombatActionCandidate SkillOn(string id = "mob-1") => new(
        CombatActionKind.UseSkill, target: new EntityId(id), skill: new SkillId("7"));

    /// <summary>
    /// A position observed seconds ago still permits the act: these sightings
    /// come from a channel that stops mentioning a monster that stops moving.
    /// </summary>
    [Fact]
    public void ARecentlyObservedTarget_IsFitToDecideFrom()
    {
        string? refusal = EngageCommand.DescribeUnfitPicture(
            SkillOn(),
            EquatableArray<Mob>.From(new[] { MobObservedAt(GuardNow.AddSeconds(-5)) }),
            gateIsLive: true,
            GuardNow);

        Assert.Null(refusal);
    }

    /// <summary>
    /// The hole this closes: nothing on this path read
    /// <see cref="WorldFact{T}.ObservedAtUtc"/>, so a position of any age passed
    /// every check in silence.
    /// </summary>
    [Fact]
    public void APositionOlderThanTheBound_RefusesAndSaysHowOld()
    {
        string? refusal = EngageCommand.DescribeUnfitPicture(
            SkillOn(),
            EquatableArray<Mob>.From(new[] { MobObservedAt(GuardNow.AddSeconds(-45)) }),
            gateIsLive: true,
            GuardNow);

        Assert.NotNull(refusal);
        Assert.StartsWith(EngageCommand.PictureNotFitReason, refusal, StringComparison.Ordinal);
        Assert.Contains("position_age_ms=45000/30000", refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// A closed gate refuses before the key press rather than at it, and the
    /// refusal says which condition failed.
    /// </summary>
    [Fact]
    public void AClosedSafetyGate_RefusesTheRound()
    {
        string? refusal = EngageCommand.DescribeUnfitPicture(
            SkillOn(),
            EquatableArray<Mob>.From(new[] { MobObservedAt(GuardNow.AddSeconds(-1)) }),
            gateIsLive: false,
            GuardNow);

        Assert.NotNull(refusal);
        Assert.Contains("gate_live=False", refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// A position the runtime is not confident about does not authorise an act.
    /// </summary>
    /// <remarks>
    /// Today's live path stamps 1.0, so this branch never fires on a real
    /// client -- which is exactly why it is pinned here rather than assumed: the
    /// first fused or screen-derived position with a lower confidence must meet
    /// a guard that is already in place, not one somebody remembers to add.
    /// </remarks>
    [Fact]
    public void APositionBelowTheConfidenceThreshold_RefusesTheRound()
    {
        string? refusal = EngageCommand.DescribeUnfitPicture(
            SkillOn(),
            EquatableArray<Mob>.From(new[] { MobObservedAt(GuardNow.AddSeconds(-1), confidence: 0.40) }),
            gateIsLive: true,
            GuardNow);

        Assert.NotNull(refusal);
        Assert.Contains("confidence=0.40/0.80", refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// The guard runs after the target check, so a target it cannot find is a
    /// disagreement between two views of the same instant, not a normal refusal.
    /// </summary>
    [Fact]
    public void ATargetMissingFromTheList_IsNamedAsADisagreement_NotAsAStalePosition()
    {
        string? refusal = EngageCommand.DescribeUnfitPicture(
            SkillOn("mob-that-is-not-there"),
            EquatableArray<Mob>.From(new[] { MobObservedAt(GuardNow.AddSeconds(-1)) }),
            gateIsLive: true,
            GuardNow);

        Assert.Equal($"{EngageCommand.PictureNotFitReason}:target_vanished_between_checks", refusal);
    }

    /// <summary>
    /// A target the world does not contain is refused by name rather than
    /// taken on the operator's word -- the gap <c>--engage</c> was changed to
    /// close.
    /// </summary>
    [Fact]
    public void AnEntityIdNamingNothingObserved_IsRefusedAsTargetNotFound()
    {
        var now = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
        var candidate = new CombatActionCandidate(
            CombatActionKind.UseSkill, target: new EntityId("mob-that-does-not-exist"), skill: new SkillId("7"));

        var player = new Player(
            new EntityId("player-1"),
            WorldFact<WorldPosition>.Live(new WorldPosition(0, 0), 1.0, now),
            WorldFact<float>.Unknown("orientation_not_read", now),
            WorldFact<bool>.Unknown("alive_not_read", now),
            WorldFact<MapId>.Unknown("map_not_read", now),
            CombatantStatus.Empty,
            WorldFact<EquatableArray<Skill>>.Live(EquatableArray<Skill>.Empty, 1d, now),
            WorldFact<EquatableArray<Cooldown>>.Live(EquatableArray<Cooldown>.Empty, 1d, now),
            WorldFact<EquatableArray<InventoryItem>>.Live(EquatableArray<InventoryItem>.Empty, 1d, now),
            WorldFact<EquatableArray<EquipmentItem>>.Live(EquatableArray<EquipmentItem>.Empty, 1d, now));

        CombatConstraintCheck verdict = CombatPlanner.CheckTargetConstraints(
            candidate, player, EquatableArray<Mob>.Empty);

        Assert.Equal("engage_target_refused:target_not_found", EngageCommand.DescribeTargetRefusal(verdict));
    }


    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        Assert.True(directory is not null, "Repository root not found.");
        return directory!.FullName;
    }
}
