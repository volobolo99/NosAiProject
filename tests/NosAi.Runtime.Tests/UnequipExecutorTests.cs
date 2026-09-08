using System.Collections.Immutable;
using NosAi.Core.WorldModel;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Perception.Network;
using NosAi.Runtime.Safety;
using NosAi.Runtime.Tactical;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// AP-07/A2A4: <see cref="UnequipExecutor"/> — one click on an equipment slot,
/// verified against the panel calibration and then the wire's <c>equip</c>
/// reading (no desktop, no real mouse: the executor takes the gated backend as
/// a dependency, so it is driven with a <see cref="RecordingInputBackend"/>).
/// </summary>
/// <remarks>
/// <para>
/// The four outcomes are kept apart on purpose: <c>Confirmed</c> says a new
/// <c>equip</c> arrived with the slot gone, <c>StillWorn</c> says a new
/// <c>equip</c> arrived with the slot still there (the click did something
/// else), <c>NotConfirmed</c> says nothing arrived, and <c>NotAttempted</c>
/// says nothing was emitted.
/// </para>
/// </remarks>
public sealed class UnequipExecutorTests
{
    private static readonly DateTime Now = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
    private static readonly IntPtr Session = 0x7200;
    private static readonly ActuationAuthority Operator = ActuationAuthority.Commanded(UnequipCommand.Flag);

    private static readonly TimeSpan FastWindow = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan FastPoll = TimeSpan.FromMilliseconds(2);

    // ------------------------------------------------------------- the calibration

    /// <summary>
    /// A complete calibration at <paramref name="width"/>x<paramref name="height"/>:
    /// every declared slot gets the same fixed crop, so the resolved pixel is
    /// deterministic and the exact centre can be asserted.
    /// </summary>
    private static InventoryPanelRoiCalibration Calibrated(int width = 1024, int height = 768)
    {
        var rois = new Dictionary<EquipmentSlot, InventorySlotRoi>();
        foreach (EquipmentSlot slot in Enum.GetValues<EquipmentSlot>())
            rois[slot] = new InventorySlotRoi(0.5, 0.5, 0.02, 0.02);
        return InventoryPanelRoiCalibration.Confirmed(rois, width, height, Now);
    }

    // ------------------------------------------------------------- the geometry

    private static GeometryStamp StatedGeometry(IntPtr window, int width = 1024, int height = 768) => new(
        new GeometryEpoch(window, new PixelRect(0, 0, width, height), 96, 0xABCD),
        new DateTimeOffset(Now));

    // ------------------------------------------------------------- the wire

    private static WornEquipment Equip(params (int Slot, int Vnum)[] slots) =>
        new(3443217,
            slots.Select(s => new WornEquipmentSlot(s.Slot, s.Vnum)).ToImmutableArray(),
            EquipmentWireOpcode.Equip);

    private static Func<WornEquipment?> EquipReader(params WornEquipment?[] sequence)
    {
        var queue = new Queue<WornEquipment?>(sequence);
        return () => queue.Count > 0 ? queue.Dequeue() : null;
    }

    private static UnequipRequest Request(EquipmentSlot slot = EquipmentSlot.Necklace, UnequipGesture gesture = UnequipGesture.Single) =>
        new(slot, gesture);

    private static UnequipExecutor Build(RecordingInputBackend recorder, Func<IntPtr, GeometryStamp>? readGeometry = null) =>
        new(
            new GatedInputBackend(recorder, () => RuntimeSafetyPolicy.SafeDefault with { LiveInputEnabled = true }),
            () => Session,
            readGeometry: readGeometry ?? (w => StatedGeometry(w)),
            verificationWindow: FastWindow,
            pollInterval: FastPoll);

    // ------------------------------------------------------- 1. an uncalibrated panel is not clicked

    [Fact]
    public void AnUncalibratedPanel_IsNotClicked_WithTheCalibrationsOwnReason()
    {
        var recorder = new RecordingInputBackend();
        UnequipExecutor executor = Build(recorder);

        UnequipReport report = executor.Unequip(
            Request(), InventoryPanelRoiCalibration.Uncalibrated, in Operator, EquipReader());

        Assert.False(report.Emitted);
        Assert.Equal(InventoryPanelRoiCalibration.NotCalibratedReason, report.RefusalReason);
        Assert.Equal(UnequipOutcome.NotAttempted, report.Verification.Outcome);
        Assert.Empty(recorder.Events);
    }

    // ------------------------------------------------------- 2. a different client resolution is refused

    [Fact]
    public void AClientAreaAtAnotherResolution_IsNotClicked_WithItsOwnReason()
    {
        var recorder = new RecordingInputBackend();
        UnequipExecutor executor = Build(recorder, readGeometry: w => StatedGeometry(w, 1280, 720));

        UnequipReport report = executor.Unequip(
            Request(), Calibrated(), in Operator, EquipReader());

        Assert.False(report.Emitted);
        Assert.StartsWith(UnequipExecutor.ResolutionMismatchReason, report.RefusalReason, StringComparison.Ordinal);
        Assert.Contains("1280x720", report.RefusalReason, StringComparison.Ordinal);
        Assert.Empty(recorder.Events);
    }

    // ------------------------------------------------------- 5. the wire verdict: slot gone

    [Fact]
    public void ANewEquipWithoutTheSlot_Confirms()
    {
        var recorder = new RecordingInputBackend();
        UnequipExecutor executor = Build(recorder);

        // Baseline: slot 6 (Necklace) is worn. After the click a new equip arrives
        // where slot 6 is gone (slot 0 survives).
        UnequipReport report = executor.Unequip(
            Request(EquipmentSlot.Necklace),
            Calibrated(),
            in Operator,
            EquipReader(Equip((0, 262), (6, 309)), Equip((0, 262))));

        Assert.True(report.Emitted);
        Assert.Equal(UnequipOutcome.Confirmed, report.Verification.Outcome);
        Assert.True(report.Verification.Confirmed);
        Assert.Equal(new[] { "move-absolute:522,391", "click:Left" }, recorder.Events);
    }

    // ------------------------------------------------------- 6. the slot is still worn

    [Fact]
    public void ANewEquipStillContainingTheSlot_IsStillWorn_NotNotConfirmed()
    {
        var recorder = new RecordingInputBackend();
        UnequipExecutor executor = Build(recorder);

        // Baseline: slots 6 and 8 are worn. The click took slot 8 off instead
        // (the wrong aim), so the new equip still contains the requested slot 6.
        UnequipReport report = executor.Unequip(
            Request(EquipmentSlot.Necklace),
            Calibrated(),
            in Operator,
            EquipReader(Equip((6, 309), (8, 518)), Equip((6, 309))));

        Assert.True(report.Emitted);
        Assert.Equal(UnequipOutcome.StillWorn, report.Verification.Outcome);
        Assert.NotEqual(UnequipOutcome.NotConfirmed, report.Verification.Outcome);
    }

    // ------------------------------------------------------- 7. no equip within the window

    [Fact]
    public void NoEquipWithinTheWindow_IsNotConfirmed_AndReportsTheWait()
    {
        var recorder = new RecordingInputBackend();
        UnequipExecutor executor = Build(recorder);

        // Only the baseline arrives; the wire never reports a changed set.
        UnequipReport report = executor.Unequip(
            Request(),
            Calibrated(),
            in Operator,
            EquipReader(Equip((6, 309))));

        Assert.True(report.Emitted);
        Assert.Equal(UnequipOutcome.NotConfirmed, report.Verification.Outcome);
        Assert.True(report.Verification.Waited >= FastWindow);
    }

    // ------------------------------------------------------- 8. a refused scope is NotAttempted

    [Fact]
    public void AScopeRefusedByTheGate_IsNotAttempted_AndTheWindowNeverOpens()
    {
        var recorder = new RecordingInputBackend();
        UnequipExecutor executor = Build(recorder);

        // default(ActuationAuthority) is kind None: the gate refuses the scope
        // before the baseline is even read.
        ActuationAuthority none = default;
        UnequipReport report = executor.Unequip(
            Request(), Calibrated(), in none, EquipReader());

        Assert.False(report.Emitted);
        Assert.Equal(UnequipOutcome.NotAttempted, report.Verification.Outcome);
        Assert.StartsWith(UnequipExecutor.ScopeRefusedPrefix, report.RefusalReason, StringComparison.Ordinal);
        Assert.Empty(recorder.Events);
    }

    // ------------------------------------------------------- a slot that is not worn is refused

    [Fact]
    public void ASlotNotCurrentlyWorn_IsRefused_BeforeEmitting()
    {
        var recorder = new RecordingInputBackend();
        UnequipExecutor executor = Build(recorder);

        // Slot 7 (Ring) is not in the baseline set, so unequipping it cannot be
        // confirmed and the click never leaves.
        UnequipReport report = executor.Unequip(
            Request(EquipmentSlot.Ring),
            Calibrated(),
            in Operator,
            EquipReader(Equip((6, 309))));

        Assert.False(report.Emitted);
        Assert.Equal(UnequipExecutor.SlotNotWornReason, report.RefusalReason);
        // The cursor moved (reversible), but no click left: the refusal is an
        // act that did not happen, not a click that was refused at the last moment.
        Assert.DoesNotContain(recorder.Events, e => e.StartsWith("click:", StringComparison.Ordinal));
    }

    [Fact]
    public void NoEquipReadingAtAll_IsRefused_WithItsOwnReason()
    {
        var recorder = new RecordingInputBackend();
        UnequipExecutor executor = Build(recorder);

        UnequipReport report = executor.Unequip(
            Request(), Calibrated(), in Operator, EquipReader());

        Assert.False(report.Emitted);
        Assert.Equal(UnequipExecutor.NoEquipmentObservedReason, report.RefusalReason);
        Assert.DoesNotContain(recorder.Events, e => e.StartsWith("click:", StringComparison.Ordinal));
    }

    // ------------------------------------------------------- a slot the calibration does not name

    [Fact]
    public void ASlotOutsideTheCalibration_IsRefused_WithoutClicking()
    {
        var recorder = new RecordingInputBackend();
        UnequipExecutor executor = Build(recorder);

        // The calibration is complete, so it names every EquipmentSlot value
        // produced by Enum.GetValues<EquipmentSlot>(). Casting to an undeclared
        // value, e.g. (EquipmentSlot)200, is the only way to observe this
        // defensive branch: a complete calibration cannot miss a declared slot.
        UnequipReport report = executor.Unequip(
            Request((EquipmentSlot)200),
            Calibrated(),
            in Operator,
            EquipReader());

        Assert.False(report.Emitted);
        Assert.Equal(UnequipExecutor.SlotNotResolvedReason, report.RefusalReason);
        Assert.Equal(UnequipOutcome.NotAttempted, report.Verification.Outcome);
        Assert.Empty(recorder.Events);
    }

    // ------------------------------------------------------- the gesture

    [Fact]
    public void ARightClickGesture_EmitsARightClick()
    {
        var recorder = new RecordingInputBackend();
        UnequipExecutor executor = Build(recorder);

        UnequipReport report = executor.Unequip(
            Request(gesture: UnequipGesture.Right),
            Calibrated(),
            in Operator,
            EquipReader(Equip((6, 309)), Equip((0, 262))));

        Assert.True(report.Emitted);
        Assert.Equal(new[] { "move-absolute:522,391", "click:Right" }, recorder.Events);
    }

    [Fact]
    public void ADoubleClickGesture_EmitsTwoLeftClicksInOneScope()
    {
        var recorder = new RecordingInputBackend();
        UnequipExecutor executor = Build(recorder);

        UnequipReport report = executor.Unequip(
            Request(gesture: UnequipGesture.Double),
            Calibrated(),
            in Operator,
            EquipReader(Equip((6, 309)), Equip((0, 262))));

        Assert.True(report.Emitted);
        Assert.Equal(new[] { "move-absolute:522,391", "click:Left", "click:Left" }, recorder.Events);
    }

    // ------------------------------------------------------- the four outcomes are distinct

    [Fact]
    public void TheFourOutcomes_AreDistinctNamedStates()
    {
        Assert.NotEqual(UnequipOutcome.Confirmed, UnequipOutcome.StillWorn);
        Assert.NotEqual(UnequipOutcome.StillWorn, UnequipOutcome.NotConfirmed);
        Assert.NotEqual(UnequipOutcome.NotConfirmed, UnequipOutcome.NotAttempted);
        Assert.NotEqual(UnequipOutcome.Confirmed, UnequipOutcome.NotConfirmed);
        Assert.NotEqual(UnequipOutcome.Confirmed, UnequipOutcome.NotAttempted);
    }
}
