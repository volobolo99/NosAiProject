using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Combat;
using NosAi.LiveIntegration;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Navigation;
using NosAi.Runtime.Tactical;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// AP-05/A4: <see cref="RecoverCommand.ExecuteOneRound"/> -- one operator-named
/// <see cref="CombatActionKind.UseConsumable"/> press at a quickbar slot,
/// executed through an injected keybind map and input backend and verified
/// through injected before/after vitals reads (no desktop, no real time, no
/// <c>ClientMemorySession</c>). Covers: a confirmed keybind with an accepted
/// press and rising <c>Hp</c> → <see cref="CombatExecutionResult.ResourceGainConfirmed"/>;
/// the same with unchanged or falling <c>Hp</c> →
/// <see cref="CombatExecutionResult.NoResourceChangeObserved"/>; an
/// unconfirmed/missing keybind → <c>NotAttempted</c> with the key press never
/// reaching the backend; a non-<c>UseConsumable</c> kind → <c>NotAttempted</c>
/// without reading vitals or touching the input backend at all; and the
/// <see cref="RecoverCommand.Run"/> argument guards refusing <c>slot &lt; 1</c>
/// and <c>rounds &lt; 1</c> cleanly.
/// </summary>
public sealed class RecoverCommandTests
{
    private static readonly DateTime Now = new(2026, 9, 6, 9, 30, 0, DateTimeKind.Utc);
    private static readonly ActuationAuthority RecoverAuthority = ActuationAuthority.Commanded(RecoverCommand.Flag);
    private const int Slot = 3;

    // ------------------------------------------------------------- helpers

    private static CombatActionCandidate UseConsumableCandidate(int slot = Slot) =>
        new(CombatActionKind.UseConsumable, item: new ItemId(slot.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    private static CombatActionCandidate BasicAttackCandidate() =>
        new(CombatActionKind.BasicAttack, target: new EntityId("mob-1"));

    private static PlayerVitalsReading Vitals(uint hp) =>
        new(Hp: hp, MaxHp: 100, Mp: 60, MaxMp: 100);

    private static KeybindMap ConfirmedMap(string intent = "consumable.3", ushort virtualKey = 112)
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
        string path = Path.Combine(Path.GetTempPath(), "nosai-recover-keybinds-" + Guid.NewGuid().ToString("N") + ".json");
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
        int slot,
        KeybindMap keybinds,
        RecordingInput input,
        PlayerVitalsReading? before,
        PlayerVitalsReading? after)
    {
        var reads = new Queue<PlayerVitalsReading?>(new[] { before, after });
        return RecoverCommand.ExecuteOneRound(
            candidate,
            slot,
            keybinds,
            input,
            readVitals: () => reads.Count > 0 ? reads.Dequeue() : null,
            verificationDelay: () => { }, // no real time in tests
            in RecoverAuthority,
            Now);
    }

    // -------------------------------------------------- confirmed gain

    [Fact]
    public void UseConsumableWithConfirmedKeybindAcceptedPressAndRisingHp_ConfirmsRecovery()
    {
        RecordingInput input = new();
        CombatExecutionEvidence evidence = RunRound(
            UseConsumableCandidate(),
            Slot,
            ConfirmedMap(),
            input,
            before: Vitals(hp: 60),
            after: Vitals(hp: 100));

        Assert.True(evidence.ResourceGainConfirmed);
        Assert.Equal(CombatExecutionResult.ResourceGainConfirmed, evidence.Result);
        Assert.Equal(ResourceKind.Health, evidence.ResourceObserved);
        Assert.Null(evidence.Detail);
        Assert.True(evidence.Before.HasValue);
        Assert.True(evidence.After.HasValue);
        Assert.Equal(60d, evidence.Before.Value);
        Assert.Equal(100d, evidence.After.Value);
        Assert.Equal(NosAi.Core.WorldModel.DataSourceKind.Live, evidence.Before.Source);
        Assert.Equal(NosAi.Core.WorldModel.DataSourceKind.Live, evidence.After.Source);

        // The press went out exactly once, for the confirmed key, with the
        // command's press duration.
        (ushort virtualKey, int duration) = Assert.Single(input.Presses);
        Assert.Equal(112, virtualKey);
        Assert.Equal(80, duration);
    }

    // -------------------------------------------------- no gain observed

    [Fact]
    public void UseConsumableWithConfirmedKeybindAndUnchangedHp_ReportsNoResourceChangeObserved()
    {
        RecordingInput input = new();
        CombatExecutionEvidence evidence = RunRound(
            UseConsumableCandidate(),
            Slot,
            ConfirmedMap(),
            input,
            before: Vitals(hp: 100),
            after: Vitals(hp: 100));

        Assert.Equal(CombatExecutionResult.NoResourceChangeObserved, evidence.Result);
        Assert.False(evidence.ResourceGainConfirmed);
        Assert.Single(input.Presses);
    }

    [Fact]
    public void UseConsumableWithFallingHp_ReportsNoResourceChangeObserved()
    {
        // Falling Health is also "no gain observed" -- the slot did not heal (the
        // player may have taken damage in the window). Only a rise is evidence of
        // a recovery, never a fall, mirroring Project's "only a fall is evidence
        // of a cost" direction.
        RecordingInput input = new();
        CombatExecutionEvidence evidence = RunRound(
            UseConsumableCandidate(),
            Slot,
            ConfirmedMap(),
            input,
            before: Vitals(hp: 90),
            after: Vitals(hp: 70));

        Assert.Equal(CombatExecutionResult.NoResourceChangeObserved, evidence.Result);
        Assert.False(evidence.ResourceGainConfirmed);
        Assert.Single(input.Presses);
    }

    // -------------------------------------------------- keybind refusals

    [Fact]
    public void UseConsumableWithNoKeybind_IsNotAttempted_AndNeverTouchesTheInputBackend()
    {
        RecordingInput input = new();
        CombatExecutionEvidence evidence = RunRound(
            UseConsumableCandidate(),
            Slot,
            KeybindMap.Empty,
            input,
            before: Vitals(hp: 60),
            after: Vitals(hp: 100));

        Assert.Equal(CombatExecutionResult.Aborted, evidence.Result);
        Assert.Equal("keybind_not_confirmed", evidence.Detail);
        Assert.False(evidence.Before.HasValue);
        Assert.False(evidence.After.HasValue);
        Assert.Empty(input.Presses);
    }

    [Fact]
    public void UseConsumableWithDeclaredButUnconfirmedKeybind_IsNotAttempted_AndNeverTouchesTheInputBackend()
    {
        RecordingInput input = new();
        KeybindMap map = MapFromJson("""
            {
              "version": 1,
              "binds": {
                "consumable.3": { "virtualKey": 112, "label": "F1", "confirmed": false }
              }
            }
            """);

        CombatExecutionEvidence evidence = RunRound(
            UseConsumableCandidate(),
            Slot,
            map,
            input,
            before: Vitals(hp: 60),
            after: Vitals(hp: 100));

        Assert.Equal(CombatExecutionResult.Aborted, evidence.Result);
        Assert.Equal("keybind_not_confirmed", evidence.Detail);
        Assert.Empty(input.Presses);
    }

    [Fact]
    public void UseConsumableWithAWrongSlotIntent_IsNotAttempted_WhenOnlyAnotherSlotIsBound()
    {
        // The intent is exactly "consumable.{slot}": a bind for a different slot
        // must not fire this slot's press.
        RecordingInput input = new();
        KeybindMap map = ConfirmedMap(intent: "consumable.4", virtualKey: 113);

        CombatExecutionEvidence evidence = RunRound(
            UseConsumableCandidate(),
            Slot,
            map,
            input,
            before: Vitals(hp: 60),
            after: Vitals(hp: 100));

        Assert.Equal(CombatExecutionResult.Aborted, evidence.Result);
        Assert.Equal("keybind_not_confirmed", evidence.Detail);
        Assert.Empty(input.Presses);
    }

    // -------------------------------------------------- input refusals

    [Fact]
    public void UseConsumableWithARefusedKeyPress_IsNotAttempted_WithTheRefusalDetail()
    {
        RecordingInput input = new() { Accepted = false };
        CombatExecutionEvidence evidence = RunRound(
            UseConsumableCandidate(),
            Slot,
            ConfirmedMap(),
            input,
            before: Vitals(hp: 60),
            after: Vitals(hp: 100));

        Assert.Equal(CombatExecutionResult.Aborted, evidence.Result);
        Assert.Equal("key_press_not_accepted", evidence.Detail);
        Assert.Single(input.Presses); // the backend was asked; it refused
        Assert.False(evidence.Before.HasValue);
    }

    // -------------------------------------------------- kind refusal

    [Fact]
    public void NonUseConsumableKind_IsNotAttempted_WithoutReadingVitalsOrTouchingTheInputBackend()
    {
        RecordingInput input = new();
        var reads = 0;

        CombatExecutionEvidence evidence = RecoverCommand.ExecuteOneRound(
            BasicAttackCandidate(),
            Slot,
            ConfirmedMap(),
            input,
            readVitals: () => { reads++; return Vitals(hp: 60); },
            verificationDelay: () => { },
            in RecoverAuthority,
            Now);

        Assert.Equal(CombatExecutionResult.Aborted, evidence.Result);
        Assert.Equal("recover_v1_supports_useconsumable_only", evidence.Detail);
        Assert.Null(evidence.ResourceObserved);
        Assert.False(evidence.Before.HasValue);
        Assert.False(evidence.After.HasValue);
        Assert.Equal(0, reads); // vitals were never even asked for
        Assert.Empty(input.Presses);
    }

    [Fact]
    public void EveryNonUseConsumableKind_IsRefusedBeforeAnyInput_WithTheSameNamedReason()
    {
        foreach (CombatActionKind kind in new[]
                 {
                     CombatActionKind.BasicAttack,
                     CombatActionKind.UseSkill,
                     CombatActionKind.Reposition,
                     CombatActionKind.Flee
                 })
        {
            CombatActionCandidate candidate = kind switch
            {
                CombatActionKind.BasicAttack => new(kind, target: new EntityId("mob-1")),
                CombatActionKind.UseSkill => new(kind, skill: new SkillId("201")),
                CombatActionKind.Reposition => new(kind, destination: new WorldPosition(3f, 4f)),
                CombatActionKind.Flee => new(kind),
                _ => throw new ArgumentOutOfRangeException()
            };

            RecordingInput input = new();
            CombatExecutionEvidence evidence = RunRound(
                candidate,
                Slot,
                ConfirmedMap(),
                input,
                before: Vitals(hp: 60),
                after: Vitals(hp: 100));

            Assert.Equal(CombatExecutionResult.Aborted, evidence.Result);
            Assert.Equal("recover_v1_supports_useconsumable_only", evidence.Detail);
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

        CombatExecutionEvidence evidence = RecoverCommand.ExecuteOneRound(
            UseConsumableCandidate(),
            Slot,
            ConfirmedMap(),
            input,
            readVitals: () => { reads++; return Vitals(hp: 60); },
            verificationDelay: () => { },
            in missing,
            Now);

        Assert.Equal(CombatExecutionResult.Aborted, evidence.Result);
        Assert.Equal(ActuationAuthority.MissingReason, evidence.Detail);
        Assert.Equal(0, reads); // nothing was read or pressed
        Assert.Empty(input.Presses);
    }

    // ------------------------------------------------ Run(...) argument validation

    // Program.cs's dispatch only checks that the flag has a following argument
    // and that it parses as an integer, so a caller that reaches Run with a
    // non-positive slot or round count must be refused cleanly, not crash the
    // process with an unhandled exception -- the same [REFUSED] boundary every
    // other guard in this command already gives, and the only part of
    // Run/RunWindows testable without a desktop.

    [Fact]
    public void Run_ZeroSlot_IsRefusedCleanly_NeverThrows()
    {
        int exitCode = RecoverCommand.Run(slot: 0);

        Assert.Equal(WalkCommand.ExitAbandoned, exitCode);
    }

    [Fact]
    public void Run_NegativeSlot_IsRefusedCleanly_NeverThrows()
    {
        int exitCode = RecoverCommand.Run(slot: -1);

        Assert.Equal(WalkCommand.ExitAbandoned, exitCode);
    }

    [Fact]
    public void Run_ZeroRounds_IsRefusedCleanly_NeverThrows()
    {
        int exitCode = RecoverCommand.Run(slot: 3, rounds: 0);

        Assert.Equal(WalkCommand.ExitAbandoned, exitCode);
    }

    [Fact]
    public void Run_NegativeRounds_IsRefusedCleanly_NeverThrows()
    {
        int exitCode = RecoverCommand.Run(slot: 3, rounds: -1);

        Assert.Equal(WalkCommand.ExitAbandoned, exitCode);
    }

    // ------------------------------------------------------------ wiring

    [Fact]
    public void TheRuntimeWiresTheRecoverFlag()
    {
        string root = RepositoryRoot();
        string program = File.ReadAllText(Path.Combine(root, "src", "NosAi.Runtime", "Program.cs"));

        Assert.Contains(RecoverCommand.Flag, program, StringComparison.Ordinal);
        Assert.Contains("RecoverCommand.Run", program, StringComparison.Ordinal);
        Assert.Contains("\"" + RecoverCommand.Flag + "\"", program, StringComparison.Ordinal);
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
