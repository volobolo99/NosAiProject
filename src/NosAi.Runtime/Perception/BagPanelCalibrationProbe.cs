using System.Globalization;
using NosAi.Core.WorldModel;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Perception;

public static class BagPanelCalibrationProbe
{
    public static bool TryParseSlots(IReadOnlyList<string> tokens, out IReadOnlyDictionary<int, InventorySlotRoi> rois, out string? failureReason)
    {
        throw new NotImplementedException();
    }

    public static int Run(string? repoRoot = null, string processName = "NostaleClientX", IReadOnlyDictionary<int, InventorySlotRoi>? slots = null)
    {
        throw new NotImplementedException();
    }

    private static bool TryParseOne(string token, out int slot, out InventorySlotRoi roi, out string? reason)
    {
        throw new NotImplementedException();
    }

    private static void ReportCalibrationState(string path, IReadOnlyDictionary<int, InventorySlotRoi>? proposed, PixelRect area)
    {
        throw new NotImplementedException();
    }

    private static CaptureFrame? AcquireFrame(DxgiDesktopDuplicationSource capture)
    {
        throw new NotImplementedException();
    }

    private static void WritePreview(string repoRoot, CaptureFrame frame, PixelRect area)
    {
        throw new NotImplementedException();
    }
}
