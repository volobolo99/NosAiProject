using NosAi.Runtime.Perception;

namespace NosAi.ControlPanel;

/// <summary>
/// Writes the last HUD crops so the operator can check the ROI. Not a provider
/// and not part of the Gate 1 snapshot. <c>data/</c> is gitignored.
/// </summary>
/// <remarks>
/// The writing itself now lives in <see cref="HudCropWriter"/>, in the runtime,
/// because the headless HUD probe needs the same crops and two copies of a BMP
/// writer would be two things to keep in step. This stays as the name the
/// Control Panel calls.
/// </remarks>
internal static class HudCropStore
{
    public const string RelativeDirectory = HudCropWriter.RelativeDirectory;

    public static string? TrySave(string? repoRoot, CaptureFrame frame, ScreenVitalObservation observation)
        => HudCropWriter.TrySave(repoRoot, frame, observation);
}
