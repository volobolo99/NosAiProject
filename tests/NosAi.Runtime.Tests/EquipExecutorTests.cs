using NosAi.Core.WorldModel;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Perception.Network;
using NosAi.Runtime.Safety;
using NosAi.Runtime.Tactical;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// C-312: <see cref="EquipExecutor"/> — one click on a bag slot, verified
/// against the bag calibration and then the wire's <c>equip</c> reading (no
/// desktop, no real mouse: the executor takes the gated backend as a
/// dependency, so it is driven with a <see cref="RecordingInputBackend"/>).
/// </summary>
/// <remarks>
/// <para>
/// The four outcomes mirror <see cref="UnequipExecutorTests"/>'s, inverted for
/// the opposite action: <c>Confirmed</c> says a new <c>equip</c> arrived with
/// the target slot now carrying the requested item's vnum, <c>NotWorn</c> says
/// a new <c>equip</c> arrived but the target slot does not (the click did
/// something else, or nothing landed), <c>NotConfirmed</c> says nothing
/// arrived, and <c>NotAttempted</c> says nothing was emitted.
/// </para>
/// </remarks>
public sealed class EquipExecutorTests
{
    private static readonly DateTime Now = new(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);
    private static readonly IntPtr Session = 0x7300;
    private static readonly ActuationAuthority Operator = ActuationAuthority.Commanded(EquipCommand.Flag);

    private static readonly TimeSpan FastWindow = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan FastPoll = TimeSpan.FromMilliseconds(2);

    private static readonly ItemId Item = new("909");

    // ------------------------------------------------------------- the calibration

    private static BagPanelRoiCalibration Calibrated(int width = 1024, int height = 768, int slots = 5)
    {
        var rois = new Dictionary<int, InventorySlotRoi>();
        for (int i = 0; i < slots; i++)
            rois[i] = new InventorySlotRoi(0.5, 0.5, 0.02, 0.02);
        return BagPanelRoiCalibration.Confirmed(rois, width, height, Now);
    }

    // ------------------------------------------------------------- the geometry

    private static GeometryStamp StatedGeometry(IntPtr window, int width = 1024, int height = 768) => new(
        new GeometryEpoch(window, new PixelRect(0, 0, width, height), 96, 0xABCD),
        new DateTimeOffset(Now));

    // ------------------------------------------------------------- the wire

    private static WornEquipmentReading Equip(params (int Slot, int Vnum)[] slots) =>
        new(slots.Select(s => new WornEquipmentSlot(s.Slot, s.Vnum)).ToList(), EquipmentWireOpcode.Equip);

    private static Func<WornEquipmentReading?> EquipReader(params WornEquipmentReading?[] sequence)
    {
        var queue = new Queue<WornEquipmentReading?>(sequence);
        return () => queue.Count > 0 ? queue.Dequeue() : null;
    }

    private static EquipRequest Request(int bagSlotIndex = 2, EquipmentSlot targetSlot = EquipmentSlot.Necklace, EquipGesture gesture = EquipGesture.Single) =>
        new(Item, targetSlot, bagSlotIndex, gesture);

    private static EquipExecutor Build(RecordingInputBackend recorder, Func<IntPtr, GeometryStamp>? readGeometry = null) =>
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
        EquipExecutor executor = Build(recorder);

        EquipReport report = executor.Equip(
            Request(), BagPanelRoiCalibration.Uncalibrated, in Operator, EquipReader());

        Assert.False(report.Emitted);
        Assert.Equal(EquipExecutor.NotCalibratedReason, report.RefusalReason);
        Assert.Equal(EquipOutcome.NotAttempted, report.Verification.Outcome);
        Assert.Empty(recorder.Events);
    }

    // ------------------------------------------------------- 2. a different client resolution is refused

    [Fact]
    public void AClientAreaAtAnotherResolution_IsNotClicked_WithItsOwnReason()
    {
        var recorder = new RecordingInputBackend();
        EquipExecutor executor = Build(recorder, readGeometry: w => StatedGeometry(w, 1280, 720));

        EquipReport report = executor.Equip(
            Request(), Calibrated(), in Operator, EquipReader());

        Assert.False(report.Emitted);
        Assert.StartsWith(EquipExecutor.ResolutionMismatchReason, report.RefusalReason, StringComparison.Ordinal);
        Assert.Contains("1280x720", report.RefusalReason, StringComparison.Ordinal);
        Assert.Empty(recorder.Events);
    }

    // ------------------------------------------------------- 3. a bag slot the calibration does not cover

    [Fact]
    public void ABagSlotOutsideTheCalibration_IsRefused_WithoutClicking()
    {
        var recorder = new RecordingInputBackend();
        EquipExecutor executor = Build(recorder);

        EquipReport report = executor.Equip(
            Request(bagSlotIndex: 40), Calibrated(slots: 5), in Operator, EquipReader());

        Assert.False(report.Emitted);
        Assert.Equal(EquipExecutor.BagSlotNotResolvedReason, report.RefusalReason);
        Assert.Equal(EquipOutcome.NotAttempted, report.Verification.Outcome);
        Assert.Empty(recorder.Events);
    }

    // ------------------------------------------------------- 4. the wire verdict: item now worn

    [Fact]
    public void ANewEquipCarryingTheItemInTheTargetSlot_Confirms()
    {
        var recorder = new RecordingInputBackend();
        EquipExecutor executor = Build(recorder);

        // No baseline equip at all: an empty bag/equipment set is a normal
        // starting point for Equip, unlike Unequip which needs the slot
        // already worn.
        EquipReport report = executor.Equip(
            Request(targetSlot: EquipmentSlot.Necklace),
            Calibrated(),
            in Operator,
            EquipReader(null, Equip((6, 909))));

        Assert.True(report.Emitted);
        Assert.Equal(EquipOutcome.Confirmed, report.Verification.Outcome);
        Assert.True(report.Verification.Confirmed);
        Assert.Equal(new[] { "move-absolute:522,391", "click:Left" }, recorder.Events);
    }

    // ------------------------------------------------------- 5. the wire changed, but not with our item

    [Fact]
    public void ANewEquipThatDoesNotCarryTheItem_IsNotWorn_NotNotConfirmed()
    {
        var recorder = new RecordingInputBackend();
        EquipExecutor executor = Build(recorder);

        // The wire changed (something else appeared) but slot 6 either stayed
        // empty or carries a different vnum: the click did not do what it set
        // out to do.
        EquipReport report = executor.Equip(
            Request(targetSlot: EquipmentSlot.Necklace),
            Calibrated(),
            in Operator,
            EquipReader(Equip((0, 262)), Equip((0, 262), (6, 111))));

        Assert.True(report.Emitted);
        Assert.Equal(EquipOutcome.NotWorn, report.Verification.Outcome);
        Assert.NotEqual(EquipOutcome.NotConfirmed, report.Verification.Outcome);
    }

    // ------------------------------------------------------- 6. no change within the window

    [Fact]
    public void NoEquipChangeWithinTheWindow_IsNotConfirmed_AndReportsTheWait()
    {
        var recorder = new RecordingInputBackend();
        EquipExecutor executor = Build(recorder);

        // The same reading over and over: nothing ever changes.
        EquipReport report = executor.Equip(
            Request(),
            Calibrated(),
            in Operator,
            EquipReader(Equip((0, 262))));

        Assert.True(report.Emitted);
        Assert.Equal(EquipOutcome.NotConfirmed, report.Verification.Outcome);
        Assert.True(report.Verification.Waited >= FastWindow);
    }

    // ------------------------------------------------------- 7. a refused scope is NotAttempted

    [Fact]
    public void AScopeRefusedByTheGate_IsNotAttempted_AndTheWindowNeverOpens()
    {
        var recorder = new RecordingInputBackend();
        EquipExecutor executor = Build(recorder);

        ActuationAuthority none = default;
        EquipReport report = executor.Equip(
            Request(), Calibrated(), in none, EquipReader());

        Assert.False(report.Emitted);
        Assert.Equal(EquipOutcome.NotAttempted, report.Verification.Outcome);
        Assert.StartsWith(EquipExecutor.ScopeRefusedPrefix, report.RefusalReason, StringComparison.Ordinal);
        Assert.Empty(recorder.Events);
    }

    // ------------------------------------------------------- the gesture

    [Fact]
    public void ADoubleClickGesture_EmitsTwoLeftClicksInOneScope()
    {
        var recorder = new RecordingInputBackend();
        EquipExecutor executor = Build(recorder);

        EquipReport report = executor.Equip(
            Request(gesture: EquipGesture.Double),
            Calibrated(),
            in Operator,
            EquipReader(null, Equip((6, 909))));

        Assert.True(report.Emitted);
        Assert.Equal(new[] { "move-absolute:522,391", "click:Left", "click:Left" }, recorder.Events);
    }

    // ------------------------------------------------------- the report narrates what happened

    [Fact]
    public void TheSummary_NamesTheOutcome_NotJustWhetherItSucceeded()
    {
        var recorder = new RecordingInputBackend();
        EquipExecutor executor = Build(recorder);

        EquipReport confirmed = executor.Equip(
            Request(targetSlot: EquipmentSlot.Necklace),
            Calibrated(),
            in Operator,
            EquipReader(null, Equip((6, 909))));

        Assert.Contains("confirmed", confirmed.Summary, StringComparison.Ordinal);

        EquipReport refused = executor.Equip(
            Request(), BagPanelRoiCalibration.Uncalibrated, in Operator, EquipReader());

        Assert.StartsWith("not emitted", refused.Summary, StringComparison.Ordinal);
    }

    // ------------------------------------------------------- the three outcomes are distinct

    [Fact]
    public void TheOutcomes_AreDistinctNamedStates()
    {
        Assert.NotEqual(EquipOutcome.Confirmed, EquipOutcome.NotWorn);
        Assert.NotEqual(EquipOutcome.NotWorn, EquipOutcome.NotConfirmed);
        Assert.NotEqual(EquipOutcome.NotConfirmed, EquipOutcome.NotAttempted);
        Assert.NotEqual(EquipOutcome.Confirmed, EquipOutcome.NotConfirmed);
        Assert.NotEqual(EquipOutcome.Confirmed, EquipOutcome.NotAttempted);
    }
}
