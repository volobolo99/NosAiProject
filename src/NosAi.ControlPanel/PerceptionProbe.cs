using System.Diagnostics;
using System.IO;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;

namespace NosAi.ControlPanel;

/// <summary>Where the game draws, or why that could not be established.</summary>
internal sealed record ClientWindowLookup(ClientWindow? Window, string? FailureReason, string ProcessNames)
{
    public PixelRect? ClientArea => Window?.ClientArea;

    public DisplayField Field()
    {
        if (Window is { } window)
        {
            return new DisplayField(
                "Finestra client",
                $"0x{window.Handle.ToInt64():X} class={window.ClassName} {window.ClientArea.Width}x{window.ClientArea.Height}@{window.ClientArea.X},{window.ClientArea.Y}",
                "LIVE");
        }

        return new DisplayField(
            "Finestra client",
            $"UNKNOWN · {FailureReason ?? "no_visible_client_window"}",
            "UNKNOWN");
    }
}

/// <summary>
/// In-process DXGI probe for the operator. Results stay on this view: they are
/// not written into the Gate 1 snapshot (ADR-0012: fields land when a provider
/// is versioned into the contract).
/// </summary>
internal static class PerceptionProbe
{
    public const string FullscreenFallbackNote =
        "Finestra client non trovata: le regioni sono frazioni dello schermo intero e valgono solo in fullscreen. Una finestra in windowed legge i pixel sbagliati.";

    public static PerceptionProbeResult Run(string? repoRoot = null, string? clientProcessName = null)
    {
        var window = LocateClientWindow(clientProcessName);
        string? atlasReason;
        HudGlyphAtlas atlas;
        if (repoRoot is null)
        {
            atlas = new HudGlyphAtlas();
            atlasReason = "atlas_path_unknown";
        }
        else
        {
            atlas = HudGlyphAtlas.Load(Path.Combine(repoRoot, HudGlyphAtlas.RelativePath), out atlasReason);
        }

        if (!DxgiDesktopDuplicationSource.TryCreate(out var capture, out var unavailable))
        {
            var reason = unavailable?.Reason ?? "dxgi_unavailable";
            var hr = unavailable is null ? "UNKNOWN" : $"0x{unavailable.HResult:X8}";
            var summary = $"DXGI non disponibile: {reason}. Nessun pixel inventato.";
            if (window.Window is null)
                summary += " " + FullscreenFallbackNote;
            return new PerceptionProbeResult(
                summary,
                [
                    new DisplayField("Stato", "UNKNOWN", "UNKNOWN"),
                    new DisplayField("Motivo", reason, "UNKNOWN"),
                    new DisplayField("HRESULT", hr, "UNKNOWN"),
                    window.Field(),
                    AtlasField(atlas, atlasReason),
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

                var observation = ReadFrame(frame, window.ClientArea, atlas);
                var cropDir = HudCropStore.TrySave(repoRoot, frame, observation);
                return new PerceptionProbeResult(
                    Summarize(frame, observation, window),
                    [
                        new DisplayField("Stato", "aperto", "LIVE"),
                        size,
                        window.Field(),
                        AtlasField(atlas, atlasReason),
                        new DisplayField("Fotogramma", $"{frame.Width}x{frame.Height} [{frame.Source.ToWire()}]", frame.Source.ToWire()),
                        new DisplayField("Byte", frame.Bgra.Length.ToString(), "LIVE"),
                        new DisplayField("Tentativo", attempt.ToString(), "DERIVED"),
                        .. ObservationFields(observation, cropDir),
                        new DisplayField("Snapshot Gate 1", "non aggiornato da questo probe", "DERIVED")
                    ]);
            }

            return new PerceptionProbeResult(
                "Duplicazione aperta, nessun fotogramma nel budget (desktop fermo). Dimensione LIVE, fotogramma e HP/MP UNKNOWN."
                + (window.Window is null ? " " + FullscreenFallbackNote : ""),
                [
                    new DisplayField("Stato", "aperto", "LIVE"),
                    size,
                    window.Field(),
                    AtlasField(atlas, atlasReason),
                    new DisplayField("Fotogramma", "UNKNOWN · no_frame_within_budget", "UNKNOWN"),
                    .. VitalUnknownFields()
                ]);
        }
    }

    /// <summary>
    /// Captures one frame and teaches the atlas from LIVE wire vitals. Saves only
    /// when the pass succeeded: a refused lesson must not write a half-taught file.
    /// </summary>
    public static PerceptionProbeResult TrainFromWire(
        string? repoRoot,
        string? clientProcessName,
        ClassifiedValue<int> lastHp,
        ClassifiedValue<int> lastMaxHp)
    {
        ArgumentNullException.ThrowIfNull(lastHp);
        ArgumentNullException.ThrowIfNull(lastMaxHp);

        var window = LocateClientWindow(clientProcessName);
        var atlasPath = Path.Combine(repoRoot ?? ".", HudGlyphAtlas.RelativePath);
        var atlas = HudGlyphAtlas.Load(atlasPath, out _);

        if (!DxgiDesktopDuplicationSource.TryCreate(out var capture, out var unavailable))
        {
            var reason = unavailable?.Reason ?? "dxgi_unavailable";
            return new PerceptionProbeResult(
                $"Addestramento non eseguito: DXGI non disponibile ({reason}). L'atlante non è stato scritto.",
                [
                    window.Field(),
                    new DisplayField("Addestramento", $"UNKNOWN · {reason}", "UNKNOWN")
                ]);
        }

        using (capture)
        {
            for (var attempt = 1; attempt <= 40; attempt++)
            {
                if (!capture!.TryAcquire(out var frame) || !frame.HasPixels)
                {
                    Thread.Sleep(50);
                    continue;
                }

                return FinishTraining(atlas, atlasPath, frame, lastHp, lastMaxHp, window);
            }
        }

        return new PerceptionProbeResult(
            "Addestramento non eseguito: nessun fotogramma nel budget. L'atlante non è stato scritto.",
            [
                window.Field(),
                new DisplayField("Addestramento", "UNKNOWN · no_frame_within_budget", "UNKNOWN")
            ]);
    }

    /// <summary>The training step, isolated so a test can pass a frame without DXGI.</summary>
    internal static PerceptionProbeResult FinishTraining(
        HudGlyphAtlas atlas,
        string atlasPath,
        CaptureFrame frame,
        ClassifiedValue<int> lastHp,
        ClassifiedValue<int> lastMaxHp,
        ClientWindowLookup window)
    {
        var result = HudGlyphTraining.TrainHpFromObservedVitals(
            atlas, frame, lastHp, lastMaxHp, window.ClientArea);

        if (!result.Succeeded)
        {
            var explanation = result.FailureReason is { } reason && reason.StartsWith("label_not_live", StringComparison.Ordinal)
                ? "L'addestramento richiede un'etichetta LIVE dal canale world (osservazione di rete attiva). " +
                  "CACHED, DERIVED o UNKNOWN non vanno scritti nell'atlante: una lezione sbagliata sopravvive alla sessione. " +
                  $"Rifiutato: {reason}."
                : $"Addestramento rifiutato: {result.FailureReason}. L'atlante non è stato scritto.";
            return new PerceptionProbeResult(
                explanation,
                [
                    window.Field(),
                    new DisplayField("Addestramento", $"UNKNOWN · {result.FailureReason}", "UNKNOWN"),
                    AtlasField(atlas, atlas.Count == 0 ? "atlas_not_trained_yet" : null)
                ]);
        }

        atlas.Save(atlasPath);
        return new PerceptionProbeResult(
            $"Addestramento riuscito: {result.Learned} glifi nuovi, {result.AlreadyKnown} già noti. Atlante scritto in {atlasPath}.",
            [
                window.Field(),
                new DisplayField("Addestramento", $"{result.Learned} appresi, {result.AlreadyKnown} già noti", "DERIVED"),
                AtlasField(atlas, null)
            ]);
    }

    /// <summary>Runs the reader with an explicit client area — the T-03 correction.</summary>
    internal static ScreenVitalObservation ReadFrame(
        CaptureFrame frame, PixelRect? clientArea, HudGlyphAtlas? atlas = null)
        => new ScreenVitalReader(atlas?.ToOcrCache()).Read(frame, clientArea: clientArea);

    internal static ClientWindowLookup LocateClientWindow(string? processNames)
    {
        if (string.IsNullOrWhiteSpace(processNames))
            return new ClientWindowLookup(null, "client_process_name_empty", processNames ?? "");

        if (!OperatingSystem.IsWindows())
            return new ClientWindowLookup(null, "client_window_unavailable_off_windows", processNames);

        string? lastWindowReason = null;
        var anyProcess = false;
        foreach (var name in processNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(name);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            foreach (var process in processes)
            {
                using (process)
                {
                    anyProcess = true;
                    ClientWindow? window = ClientWindowLocator.TryFind(process.Id, out var why);
                    if (window is not null)
                        return new ClientWindowLookup(window, null, processNames);
                    lastWindowReason = why;
                }
            }
        }

        if (!anyProcess)
            return new ClientWindowLookup(null, "client_process_not_running", processNames);
        return new ClientWindowLookup(null, lastWindowReason ?? "no_visible_client_window", processNames);
    }

    internal static DisplayField AtlasField(HudGlyphAtlas atlas, string? reason)
    {
        if (string.Equals(reason, "atlas_not_trained_yet", StringComparison.Ordinal))
        {
            return new DisplayField(
                "Atlante glifi",
                "UNKNOWN · da addestrare (atlas_not_trained_yet)",
                "UNKNOWN");
        }

        if (!string.IsNullOrWhiteSpace(reason) && atlas.Count == 0)
        {
            return new DisplayField("Atlante glifi", $"UNKNOWN · {reason}", "UNKNOWN");
        }

        var characters = atlas.KnownCharacters.Count == 0
            ? "(nessuno)"
            : string.Join("", atlas.KnownCharacters);
        return new DisplayField(
            "Atlante glifi",
            $"{atlas.Count} glifi, caratteri noti: {characters}",
            "DERIVED");
    }

    private static string Summarize(CaptureFrame frame, ScreenVitalObservation observation, ClientWindowLookup window)
    {
        var hp = FormatBar(observation.HpBar);
        var mp = FormatBar(observation.MpBar);
        var numbers = observation.Hp.Current.HasValue
            ? $"HP {observation.Hp.Current.Value}/{observation.Hp.Maximum.Value} DERIVED"
            : $"HP/MP numerici UNKNOWN · {observation.Hp.Current.FailureReason ?? "ocr_glyphs_not_trained"}";
        var fallback = window.Window is null ? " " + FullscreenFallbackNote : "";
        return $"DXGI LIVE {frame.Width}x{frame.Height}. HP barra {hp}; MP barra {mp}. {numbers}. Non entra nello snapshot Gate 1.{fallback}";
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
