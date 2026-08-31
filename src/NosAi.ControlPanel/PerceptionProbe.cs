using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;

namespace NosAi.ControlPanel;

/// <summary>
/// In-process DXGI probe for the operator. Results stay on this view: they are
/// not written into the Gate 1 snapshot (ADR-0012: fields land when a provider
/// is versioned into the contract).
/// </summary>
internal static class PerceptionProbe
{
    public static PerceptionProbeResult Run(string? repoRoot = null)
    {
        if (!DxgiDesktopDuplicationSource.TryCreate(out var capture, out var unavailable))
        {
            var reason = unavailable?.Reason ?? "dxgi_unavailable";
            var hr = unavailable is null ? "UNKNOWN" : $"0x{unavailable.HResult:X8}";
            return new PerceptionProbeResult(
                $"DXGI non disponibile: {reason}. Nessun pixel inventato.",
                [
                    new DisplayField("Stato", "UNKNOWN", "UNKNOWN"),
                    new DisplayField("Motivo", reason, "UNKNOWN"),
                    new DisplayField("HRESULT", hr, "UNKNOWN"),
                    new DisplayField("Fotogramma", "UNKNOWN · capture_not_opened", "UNKNOWN")
                ]);
        }

        using (capture)
        {
            var size = new DisplayField("Desktop", $"{capture!.Width}x{capture.Height}", "LIVE");
            for (var attempt = 1; attempt <= 40; attempt++)
            {
                if (!capture.TryAcquire(out var frame) || !frame.HasPixels)
                {
                    Thread.Sleep(50);
                    continue;
                }

                var observation = new ScreenVitalReader().Read(frame);
                var cropDir = HudCropStore.TrySave(repoRoot, frame, observation);
                return new PerceptionProbeResult(
                    Summarize(frame, observation),
                    [
                        new DisplayField("Stato", "aperto", "LIVE"),
                        size,
                        new DisplayField("Fotogramma", $"{frame.Width}x{frame.Height} [{frame.Source.ToWire()}]", frame.Source.ToWire()),
                        new DisplayField("Byte", frame.Bgra.Length.ToString(), "LIVE"),
                        new DisplayField("Tentativo", attempt.ToString(), "DERIVED"),
                        .. ObservationFields(observation, cropDir),
                        new DisplayField("Snapshot Gate 1", "non aggiornato da questo probe", "DERIVED")
                    ]);
            }

            return new PerceptionProbeResult(
                "Duplicazione aperta, nessun fotogramma nel budget (desktop fermo). Dimensione LIVE, fotogramma e HP/MP UNKNOWN.",
                [
                    new DisplayField("Stato", "aperto", "LIVE"),
                    size,
                    new DisplayField("Fotogramma", "UNKNOWN · no_frame_within_budget", "UNKNOWN"),
                    .. VitalUnknownFields()
                ]);
        }
    }

    private static string Summarize(CaptureFrame frame, ScreenVitalObservation observation)
    {
        var hp = FormatBar(observation.HpBar);
        var mp = FormatBar(observation.MpBar);
        return $"DXGI LIVE {frame.Width}x{frame.Height}. HP barra {hp}; MP barra {mp}. HP/MP numerici UNKNOWN (glifi non addestrati). Non entra nello snapshot Gate 1.";
    }

    private static string FormatBar(ScreenBarFill bar)
    {
        if (bar.Ratio.HasValue)
            return $"DERIVED {bar.Ratio.Value:0.00}";
        return $"UNKNOWN · {bar.FailureReason ?? "unclassified"}";
    }

    private static DisplayField[] ObservationFields(ScreenVitalObservation observation, string? cropDir)
    {
        return
        [
            BarField("HP barra", observation.HpBar),
            BarField("MP barra", observation.MpBar),
            new DisplayField("HP ROI", FormatRoi(observation.HpRoi), "DERIVED"),
            new DisplayField("MP ROI", FormatRoi(observation.MpRoi), "DERIVED"),
            new DisplayField("HP attuale", FormatVital(observation.Hp.Current), observation.Hp.Current.Source.ToWire()),
            new DisplayField("HP massimo", FormatVital(observation.Hp.Maximum), observation.Hp.Maximum.Source.ToWire()),
            new DisplayField("Glifi HP nel ritaglio", observation.HpGlyphs.ToString(), "DERIVED"),
            new DisplayField("Glifi MP nel ritaglio", observation.MpGlyphs.ToString(), "DERIVED"),
            new DisplayField("Glifi addestrati", observation.TrainedGlyphs.ToString(), observation.TrainedGlyphs == 0 ? "UNKNOWN" : "DERIVED"),
            new DisplayField("Ritagli HUD", cropDir is null ? "UNKNOWN · crop_not_saved" : cropDir, cropDir is null ? "UNKNOWN" : "LIVE")
        ];
    }

    private static DisplayField BarField(string label, ScreenBarFill bar)
    {
        if (bar.Ratio.HasValue)
            return new DisplayField(label, bar.Ratio.Value.ToString("0.00"), bar.Ratio.Source.ToWire());
        return new DisplayField(label, $"UNKNOWN · {bar.FailureReason ?? "unclassified"}", "UNKNOWN");
    }

    private static string FormatVital(ClassifiedValue<int> value)
        => value.HasValue ? value.Value.ToString() : $"UNKNOWN · {value.FailureReason ?? "unclassified"}";

    private static string FormatRoi(PixelRect rect) => $"{rect.X},{rect.Y} {rect.Width}x{rect.Height}";

    private static DisplayField[] VitalUnknownFields()
    {
        var vitals = ScreenDerivedVitalGate.Unknown(0, "ocr_glyphs_not_trained");
        return
        [
            new DisplayField("HP barra", "UNKNOWN · no_frame_within_budget", "UNKNOWN"),
            new DisplayField("HP attuale", "UNKNOWN · ocr_glyphs_not_trained", "UNKNOWN"),
            new DisplayField("HP massimo", "UNKNOWN · ocr_glyphs_not_trained", "UNKNOWN"),
            new DisplayField("HP classificazione", vitals.Current.Source.ToWire(), vitals.Current.Source.ToWire())
        ];
    }
}

internal sealed record PerceptionProbeResult(string Summary, IReadOnlyList<DisplayField> Fields);
