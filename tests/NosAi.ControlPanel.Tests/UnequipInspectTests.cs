using System;
using System.Collections.Generic;
using System.IO;
using NosAi.ControlPanel;
using NosAi.Core.WorldModel;
using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.ControlPanel.Tests;

/// <summary>
/// La card "togli equipaggiamento" offre il bottone solo con una calibrazione
/// presente, e la lista degli slot è letta dal file, non scritta a mano.
/// </summary>
public sealed class UnequipInspectTests
{
    private static string TempPath(string stem) => Path.Combine(Path.GetTempPath(), $"{stem}-{Guid.NewGuid():N}.calibration");

    [Fact]
    public void AnAbsentCalibration_OffersNoButton_WithTheReason()
    {
        UnequipView view = UnequipInspect.Inspect(TempPath("unequip-absent"));

        Assert.False(view.CanRun);
        Assert.Contains(UnequipInspect.NotCalibratedLabel, view.Summary, StringComparison.Ordinal);
        Assert.Empty(view.Slots);
    }

    [Fact]
    public void AConfirmedCalibration_OffersTheButton_AndListsTheCalibratedSlots()
    {
        string path = TempPath("unequip-calibrated");
        try
        {
            var rois = new Dictionary<EquipmentSlot, InventorySlotRoi>();
            foreach (EquipmentSlot slot in (EquipmentSlot[])Enum.GetValues(typeof(EquipmentSlot)))
                rois[slot] = new InventorySlotRoi(0.4, 0.2, 0.05, 0.05);
            InventoryPanelRoiCalibration.Confirmed(
                rois, 1024, 768, new DateTime(2026, 9, 6, 22, 18, 0, DateTimeKind.Utc)).Save(path);

            UnequipView view = UnequipInspect.Inspect(path);

            Assert.True(view.CanRun);
            Assert.Contains("1024x768", view.Summary, StringComparison.Ordinal);
            Assert.Equal(1024, view.ClientWidth);
            Assert.Equal(768, view.ClientHeight);

            // The list is exactly the values the file carries, in file order:
            // the eighteen declared slots, no more and no fewer.
            string[] expected = Enum.GetNames<EquipmentSlot>();
            Assert.Equal(expected, view.Slots);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TheSlotList_ReadsTheFileOrder_NotTheEnumOrder()
    {
        string path = TempPath("unequip-order");
        try
        {
            // Hand-written with a deliberately reversed order: the view must read
            // the file, not reproduce the enum by memory.
            File.WriteAllText(path,
                "nosai-inventory-panel-roi 1\n"
                + "1024 768 2026-09-06T22:18:27Z\n"
                + "MiniPet 0.4 0.2 0.05 0.05\n"
                + "Weapon 0.4 0.2 0.05 0.05\n"
                + "Armor 0.4 0.2 0.05 0.05\n"
                + "Hat 0.4 0.2 0.05 0.05\n"
                + "Gloves 0.4 0.2 0.05 0.05\n"
                + "Boots 0.4 0.2 0.05 0.05\n"
                + "SecondaryWeapon 0.4 0.2 0.05 0.05\n"
                + "Necklace 0.4 0.2 0.05 0.05\n"
                + "Ring 0.4 0.2 0.05 0.05\n"
                + "Bracelet 0.4 0.2 0.05 0.05\n"
                + "Mask 0.4 0.2 0.05 0.05\n"
                + "Fairy 0.4 0.2 0.05 0.05\n"
                + "Amulet 0.4 0.2 0.05 0.05\n"
                + "SpecialistCard 0.4 0.2 0.05 0.05\n"
                + "CostumeSuit 0.4 0.2 0.05 0.05\n"
                + "CostumeHat 0.4 0.2 0.05 0.05\n"
                + "WeaponSkin 0.4 0.2 0.05 0.05\n"
                + "CostumeWings 0.4 0.2 0.05 0.05\n");

            UnequipView view = UnequipInspect.Inspect(path);

            Assert.True(view.CanRun);
            Assert.Equal("MiniPet", view.Slots[0]);
            Assert.Equal("Weapon", view.Slots[1]);
            Assert.Equal(18, view.Slots.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
