using System;
using System.IO;
using NosAi.ControlPanel;
using NosAi.Core.WorldModel;
using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.ControlPanel.Tests;

/// <summary>
/// La calibrazione dei riquadri slot equipaggiamento è un file reale versionato:
/// assente, illeggibile e calibrato sono tre disegni distinti, come per la ROI
/// bersaglio.
/// </summary>
public sealed class InventoryPanelInspectTests
{
    private static string TempPath(string stem) => Path.Combine(Path.GetTempPath(), $"{stem}-{Guid.NewGuid():N}.calibration");

    [Fact]
    public void AnAbsentFileIsNotCalibrated()
    {
        InventoryPanelRoiView view = InventoryPanelInspect.Inspect(TempPath("absent"));

        Assert.Equal(InventoryPanelRoiKind.NotCalibrated, view.Kind);
        Assert.Contains(InventoryPanelInspect.NotCalibratedLabel, view.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void AConfirmedCalibrationListsAllSlotsWhenAndResolution()
    {
        string path = TempPath("calibrated");
        try
        {
            var rois = new Dictionary<EquipmentSlot, InventorySlotRoi>();
            foreach (EquipmentSlot slot in (EquipmentSlot[])Enum.GetValues(typeof(EquipmentSlot)))
                rois[slot] = new InventorySlotRoi(0.4, 0.2, 0.05, 0.05);
            InventoryPanelRoiCalibration.Confirmed(
                rois, 1024, 768, new DateTime(2026, 9, 6, 22, 18, 0, DateTimeKind.Utc)).Save(path);

            InventoryPanelRoiView view = InventoryPanelInspect.Inspect(path);

            Assert.Equal(InventoryPanelRoiKind.Calibrated, view.Kind);
            Assert.Contains("18/18", view.Summary, StringComparison.Ordinal);
            Assert.Contains("1024x768", view.Summary, StringComparison.Ordinal);
            Assert.Contains("2026-09-06", view.Summary, StringComparison.Ordinal);
            Assert.Equal(18, view.Fields.Count);
            Assert.Contains(view.Fields, f => f.Label == "Weapon" && f.Source == "CACHED");
            Assert.Contains(view.Fields, f => f.Label == "MiniPet");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AMalformedFileIsUnreadableNotCalibrated()
    {
        string path = TempPath("malformed");
        try
        {
            File.WriteAllText(path, "nosai-inventory-panel-roi 1\n1024 768 2026-09-06T22:18:27Z\nWeapon 0.5 0.5 0.1 0.1\n");

            InventoryPanelRoiView view = InventoryPanelInspect.Inspect(path);

            Assert.Equal(InventoryPanelRoiKind.Unreadable, view.Kind);
            Assert.Contains("UNKNOWN", view.Summary, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

/// <summary>
/// Il pannello ora sa dire quale entità è l'ultimo bersaglio del personaggio,
/// oltre che se un bersaglio è presente (ADR-0018: il "quale" accanto al "se").
/// </summary>
public sealed class CombatSelectedTargetTests
{
    private static readonly DateTime Now = new(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AKnownSelectedTargetNamesWhichEntity()
    {
        CombatView view = CombatInspect.Inspect(
            null,
            ClassifiedValue<bool>.Derived(true, Now),
            Now,
            ClassifiedValue<TargetedEntity>.Live(new TargetedEntity(42, 2), Now.AddSeconds(-2)));

        Assert.Contains("id=42", view.SelectedTargetLine, StringComparison.Ordinal);
        Assert.Contains("type=2", view.SelectedTargetLine, StringComparison.Ordinal);
        Assert.Contains("id=42", view.Fields.Single(f => f.Label == "Ultimo bersaglio").Value, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutSelectedTargetTheReasonIsShownNotABlank()
    {
        CombatView view = CombatInspect.Inspect(
            null, ClassifiedValue<bool>.Derived(true, Now), Now);

        Assert.Contains("UNKNOWN", view.SelectedTargetLine, StringComparison.Ordinal);
        Assert.Contains(CombatInspect.SelectedTargetNotSurfacedReason, view.SelectedTargetLine, StringComparison.Ordinal);
        Assert.DoesNotContain(GameplayObservation.NotPublishedReason, view.SelectedTargetLine, StringComparison.Ordinal);
        Assert.Equal("UNKNOWN", view.Fields.Single(f => f.Label == "Ultimo bersaglio").Source);
    }
}
