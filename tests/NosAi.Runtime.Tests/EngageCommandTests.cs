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

    [Fact]
    public void Run_BlankTargetEntityId_IsRefusedCleanly_NeverThrows()
    {
        int exitCode = EngageCommand.Run(targetEntityId: "   ", skillId: "201");

        Assert.Equal(WalkCommand.ExitAbandoned, exitCode);
    }

    [Fact]
    public void Run_BlankSkillId_IsRefusedCleanly_NeverThrows()
    {
        int exitCode = EngageCommand.Run(targetEntityId: "mob-1", skillId: "");

        Assert.Equal(WalkCommand.ExitAbandoned, exitCode);
    }

    [Fact]
    public void Run_ZeroRounds_IsRefusedCleanly_NeverThrows()
    {
        int exitCode = EngageCommand.Run(targetEntityId: "mob-1", skillId: "201", rounds: 0);

        Assert.Equal(WalkCommand.ExitAbandoned, exitCode);
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

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        Assert.True(directory is not null, "Repository root not found.");
        return directory!.FullName;
    }
}
