using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Perception;

/// <summary>
/// Real-environment probe for the HUD reader: captures the desktop, runs
/// <see cref="ScreenVitalReader"/> over it, and writes the crops it worked from.
/// </summary>
/// <remarks>
/// <para>
/// T-03 in <c>docs/TEST_RIMANDATI.md</c>. The Control Panel has had this behind a
/// button since the beginning; the same thing runs here so the test can be
/// repeated, scripted and quoted in a report rather than clicked and described.
/// </para>
/// <para>
/// It answers one question and refuses to answer more: <b>is the ROI on the
/// HUD?</b> A bar ratio read from the wrong hundred pixels is a real measurement
/// of the wrong thing, and nothing inside the reader can tell that from a correct
/// one -- so the crops go to disk and a person looks at them. Numeric HP and MP
/// stay UNKNOWN regardless, because the glyphs are untrained; a ratio is not an
/// HP, which is the distinction ADR-0012 turns on.
/// </para>
/// </remarks>
public static class HudProbe
{
    /// <summary>Frames to wait for. A still desktop can take a moment to produce one.</summary>
    private const int AcquireAttempts = 40;

    /// <param name="processName">Client process to locate the client area from.</param>
    public static int RunConsoleProbe(string? repoRoot = null, string processName = "NostaleClientX")
    {
        repoRoot ??= Directory.GetCurrentDirectory();

        // Where the game actually draws. Without this the regions are fractions of
        // the whole desktop, which is only the client area when the client is
        // fullscreen -- T-03 measured the editor behind it instead.
        PixelRect? clientArea = null;
        if (OperatingSystem.IsWindows())
        {
            foreach (var process in System.Diagnostics.Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    ClientWindow? window = ClientWindowLocator.TryFind(process.Id, out string? why);
                    if (window is null)
                        continue;

                    clientArea = window.ClientArea;
                    Console.WriteLine(
                        $"Client window: 0x{window.Handle.ToInt64():X} class={window.ClassName} " +
                        $"client={window.ClientArea.Width}x{window.ClientArea.Height}" +
                        $"@{window.ClientArea.X},{window.ClientArea.Y}");
                    break;
                }
            }
        }

        if (clientArea is null)
        {
            Console.WriteLine($"Client window: not found for '{processName}'.");
            Console.WriteLine("  Regions fall back to fractions of the whole frame, which is right only");
            Console.WriteLine("  for a fullscreen client. A windowed one will read the wrong pixels.");
        }

        if (!DxgiDesktopDuplicationSource.TryCreate(out DxgiDesktopDuplicationSource? capture, out var unavailable))
        {
            Console.WriteLine($"[UNKNOWN] DXGI unavailable: {unavailable?.Reason ?? "dxgi_unavailable"}");
            Console.WriteLine("  No pixels are invented. HP and MP stay UNKNOWN.");
            return 1;
        }

        using (capture)
        {
            Console.WriteLine($"Desktop duplication open: {capture!.Width}x{capture.Height} [LIVE]");

            for (int attempt = 1; attempt <= AcquireAttempts; attempt++)
            {
                if (!capture.TryAcquire(out CaptureFrame frame) || !frame.HasPixels)
                {
                    Thread.Sleep(50);
                    continue;
                }

                ScreenVitalObservation observation = new ScreenVitalReader().Read(frame, clientArea: clientArea);
                string? directory = HudCropWriter.TrySave(repoRoot, frame, observation);

                Console.WriteLine($"Frame: {frame.Width}x{frame.Height} [{frame.Source.ToWire()}] after {attempt} attempt(s)");
                Console.WriteLine($"  HP roi={Describe(observation.HpRoi)} bar={Describe(observation.HpBar)}");
                Console.WriteLine($"  MP roi={Describe(observation.MpRoi)} bar={Describe(observation.MpBar)}");
                Console.WriteLine($"  Crops: {directory ?? "not written"}");
                Console.WriteLine();
                Console.WriteLine("Open hp_latest.bmp and mp_latest.bmp. The reading is DERIVED only if the");
                Console.WriteLine("crop is actually the HUD bar; if it is anything else the number is a");
                Console.WriteLine("measurement of the wrong pixels and the honest answer stays UNKNOWN.");

                // The probe cannot tell whether the ROI is right -- that is what the
                // crops are for -- so it reports success only for having captured
                // and read, never for the reading being correct.
                return 0;
            }

            Console.WriteLine("[UNKNOWN] Duplication opened but produced no frame within the budget.");
            Console.WriteLine("  A completely static desktop can do this. HP and MP stay UNKNOWN.");
            return 1;
        }
    }

    private static string Describe(PixelRect rect) =>
        rect.Width <= 0 || rect.Height <= 0
            ? "empty"
            : $"{rect.Width}x{rect.Height}@{rect.X},{rect.Y}";

    private static string Describe(ScreenBarFill bar) =>
        bar.Ratio.HasValue
            ? $"{bar.Ratio.Value:P1} [{bar.Ratio.Source.ToWire()}] confidence={bar.Confidence:P0}"
            : $"UNKNOWN ({bar.FailureReason ?? bar.Ratio.FailureReason})";
}
