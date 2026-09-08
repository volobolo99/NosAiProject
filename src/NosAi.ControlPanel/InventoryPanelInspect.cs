using System.Globalization;
using System.IO;
using NosAi.Core.WorldModel;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;

namespace NosAi.ControlPanel;

/// <summary>How the equipment-panel ROI calibration was answered, as three drawings.</summary>
internal enum InventoryPanelRoiKind : byte
{
    /// <summary>No calibration file. The operator has not confirmed the panel yet.</summary>
    NotCalibrated = 0,

    /// <summary>A confirmed calibration: all declared slots, when, and the resolution.</summary>
    Calibrated = 1,

    /// <summary>A file was present but could not be believed.</summary>
    Unreadable = 2
}

/// <summary>Operator-facing view of the equipment-panel ROI calibration.</summary>
internal sealed class InventoryPanelRoiView
{
    public InventoryPanelRoiKind Kind { get; init; }
    public string Summary { get; init; } = "";
    public IReadOnlyList<DisplayField> Fields { get; init; } = Array.Empty<DisplayField>();
}

/// <summary>
/// Read-only view of the equipment-panel ROI calibration the operator confirmed
/// against a real screenshot of their own client. Drawn like
/// <see cref="TargetInspect"/> draws the target-frame ROI: which slots are
/// calibrated, when, and on what resolution.
/// </summary>
/// <remarks>
/// <para>
/// This only shows where each slot sits on screen; it does not read what
/// occupies a slot and does not execute equip/unequip. The file lives in
/// <c>data/perception/inventory-panel-roi.calibration</c>, is versioned real
/// data (Q-089: confirmed on a live client at 1024x768), and was invisible to
/// the panel until now.
/// </para>
/// <para>
/// The calibration is "all declared slots or none" — the runtime refuses a
/// partial set — so <c>IsCalibrated</c> already means every
/// <see cref="EquipmentSlot"/> value has a confirmed crop.
/// </para>
/// </remarks>
internal static class InventoryPanelInspect
{
    public const string NotCalibratedLabel = "NON CALIBRATO";
    public const string ScopeNote = "solo calibrazione screen-space; non esegue equip/unequip (AP-07)";

    public static InventoryPanelRoiView Inspect(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        InventoryPanelRoiCalibration calibration = InventoryPanelRoiCalibration.Load(path, out string? reason);

        if (calibration.IsCalibrated)
            return Calibrated(calibration, path);

        if (File.Exists(path) && reason is { } named && named != InventoryPanelRoiCalibration.NotCalibratedReason)
            return Unreadable(named);

        return NotCalibrated();
    }

    /// <summary>
    /// Cheap identity of the file, so the window can skip a redraw when it has
    /// not changed. Absence is part of the identity, not a default size.
    /// </summary>
    public static string Signature(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                return "absent";
            return string.Create(CultureInfo.InvariantCulture, $"{info.Length}:{info.LastWriteTimeUtc.Ticks}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "unreadable:" + ex.GetType().Name;
        }
    }

    private static InventoryPanelRoiView Calibrated(InventoryPanelRoiCalibration calibration, string path)
    {
        string when = calibration.CalibratedAtUtc is { } at
            ? at.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC"
            : "UNKNOWN";
        int declared = ((EquipmentSlot[])Enum.GetValues(typeof(EquipmentSlot))).Length;
        string summary = string.Create(CultureInfo.InvariantCulture,
            $"{declared}/{declared} slot calibrati il {when} su {calibration.ClientWidth}x{calibration.ClientHeight} — {ScopeNote}");
        return new InventoryPanelRoiView
        {
            Kind = InventoryPanelRoiKind.Calibrated,
            Summary = summary,
            Fields = SlotFields(path)
        };
    }

    private static InventoryPanelRoiView Unreadable(string reason) => new()
    {
        Kind = InventoryPanelRoiKind.Unreadable,
        Summary = $"UNKNOWN · {reason} — {ScopeNote}",
        Fields = [new DisplayField("Riquadri slot", $"UNKNOWN · {reason}", "UNKNOWN")]
    };

    private static InventoryPanelRoiView NotCalibrated() => new()
    {
        Kind = InventoryPanelRoiKind.NotCalibrated,
        Summary = $"{NotCalibratedLabel} — {ScopeNote}",
        Fields = [new DisplayField("Riquadri slot", NotCalibratedLabel, "UNKNOWN")]
    };

    /// <summary>
    /// One row per calibrated slot, read from the file the runtime wrote. The
    /// fractions are shown exactly as stored, of the client area the summary
    /// names. CACHED: read from disk, not measured again.
    /// </summary>
    private static IReadOnlyList<DisplayField> SlotFields(string path)
    {
        try
        {
            string[] lines = File.ReadAllLines(path);
            var fields = new List<DisplayField>();
            for (int i = 2; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 5
                    || !Enum.TryParse<EquipmentSlot>(parts[0], out EquipmentSlot slot)
                    || !Enum.IsDefined(slot))
                    continue;

                fields.Add(new DisplayField(
                    slot.ToString(),
                    string.Create(CultureInfo.InvariantCulture,
                        $"x={parts[1]} y={parts[2]} w={parts[3]} h={parts[4]}"),
                    NosAi.Runtime.Contracts.DataSourceKind.Cached.ToWire()));
            }
            return fields;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<DisplayField>();
        }
    }
}
