using System.Globalization;
using System.IO;
using NosAi.Core.WorldModel;
using NosAi.Runtime.Perception;

namespace NosAi.ControlPanel;

/// <summary>What the equipment-panel unequip card can offer, drawn from the calibration file.</summary>
internal sealed class UnequipView
{
    /// <summary>Whether the card may offer the unequip button. False means "say why, show no button".</summary>
    public bool CanRun { get; init; }

    /// <summary>Operator-facing state: calibrated resolution, or the not-calibrated reason.</summary>
    public string Summary { get; init; } = "";

    /// <summary>The slots read from the calibration file, in file order. Never a handwritten list.</summary>
    public IReadOnlyList<string> Slots { get; init; } = Array.Empty<string>();

    /// <summary>The client width the calibration was measured on; zero when not calibrated.</summary>
    public int ClientWidth { get; init; }

    /// <summary>The client height the calibration was measured on; zero when not calibrated.</summary>
    public int ClientHeight { get; init; }
}

/// <summary>
/// Read-only view of what the unequip card needs: the panel calibration state
/// and the slot list, both read from <c>data/perception/inventory-panel-roi.calibration</c>.
/// </summary>
/// <remarks>
/// <para>
/// The slot list is read from the calibration file, not written by hand, so the
/// card offers exactly the slots the operator actually confirmed. When the file
/// is missing the card says so and offers no button: a button that would refuse
/// is worse than no button.
/// </para>
/// </remarks>
internal static class UnequipInspect
{
    public const string NotCalibratedLabel = "NON CALIBRATO";

    public static UnequipView Inspect(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        InventoryPanelRoiCalibration calibration = InventoryPanelRoiCalibration.Load(path, out string? reason);

        if (!calibration.IsCalibrated)
        {
            string named = string.IsNullOrWhiteSpace(reason) ? "calibration_absent" : reason;
            return new UnequipView
            {
                CanRun = false,
                Summary = $"{NotCalibratedLabel} — {named}",
                Slots = Array.Empty<string>(),
            };
        }

        return new UnequipView
        {
            CanRun = true,
            Summary = string.Create(CultureInfo.InvariantCulture,
                $"Calibrato su {calibration.ClientWidth}x{calibration.ClientHeight} — il client deve stare a questa risoluzione"),
            Slots = ReadSlotNames(path),
            ClientWidth = calibration.ClientWidth,
            ClientHeight = calibration.ClientHeight,
        };
    }

    /// <summary>The slot names as they appear in the file, in file order.</summary>
    /// <remarks>
    /// The calibration format is <c>name x y w h</c> per line from the third line
    /// on. Only the name is read; the fractions stay in the runtime's own load.
    /// </remarks>
    private static IReadOnlyList<string> ReadSlotNames(string path)
    {
        try
        {
            string[] lines = File.ReadAllLines(path);
            var names = new List<string>();
            for (int i = 2; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5 && Enum.TryParse<EquipmentSlot>(parts[0], ignoreCase: true, out EquipmentSlot slot) && Enum.IsDefined(slot))
                    names.Add(slot.ToString());
            }
            return names;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
