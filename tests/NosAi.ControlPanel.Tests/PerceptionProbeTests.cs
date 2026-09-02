using NosAi.ControlPanel;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.ControlPanel.Tests;

public sealed class PerceptionProbeTests
{
    /// <summary>
    /// Il nome dice « non inventa pixel », e questo e' cio' che verifica: nessun
    /// campo porta un valore quando la cattura non si e' aperta.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Prima cercava la sottostringa « inventat » nel riassunto e falliva su una
    /// macchina che non puo' catturare, perche' la frase che il probe scrive in
    /// quel caso e' « DXGI non disponibile: ... <b>Nessun pixel inventato.</b> » —
    /// cioe' il test puniva il messaggio onesto. Peggio: non sapeva distinguere
    /// « il probe ha inventato dei pixel », il difetto che sorveglia, da « questa
    /// macchina non puo' catturare adesso », che e' un fatto d'ambiente. Visto il
    /// 3 settembre 2026 con <c>dxgi_duplication_access_denied</c> (0x80070005).
    /// </para>
    /// <para>
    /// L'invariante vera non dipende dall'hardware: <b>sconosciuto non e' zero</b>.
    /// Un campo che non ha una lettura porta <c>UNKNOWN</c> <i>e</i> un motivo, mai
    /// un valore plausibile e mai una casella vuota.
    /// </para>
    /// </remarks>
    [Fact]
    public void Probe_never_invents_pixels()
    {
        var result = PerceptionProbe.Run();

        Assert.False(string.IsNullOrWhiteSpace(result.Summary));
        Assert.Contains(result.Fields, f => f.Label == "Fotogramma");

        foreach (var field in result.Fields)
        {
            // Nessuna provenienza inventata: le cinque della baseline e basta.
            Assert.Contains(field.Source, new[] { "LIVE", "DERIVED", "CACHED", "SIMULATED", "UNKNOWN" });
            Assert.False(string.IsNullOrWhiteSpace(field.Label));

            // Un campo senza lettura deve dire perche'. Una casella vuota, uno zero
            // o un falso al posto di UNKNOWN sono il difetto che questo test guarda.
            Assert.False(string.IsNullOrWhiteSpace(field.Value));
            if (field.Source == "UNKNOWN")
                Assert.DoesNotContain(field.Value, new[] { "0", "false", "-" });
        }
    }

    /// <summary>
    /// Il ramo che conta davvero, e che si puo' verificare su qualunque macchina:
    /// quando la cattura non si apre, il fotogramma resta UNKNOWN col suo motivo
    /// invece di diventare un'immagine vuota.
    /// </summary>
    [Fact]
    public void When_capture_cannot_open_the_frame_is_unknown_with_a_reason()
    {
        if (DxgiDesktopDuplicationSource.TryCreate(out var capture, out _))
        {
            capture?.Dispose();
            return; // Questa macchina cattura: il ramo non e' esercitabile qui.
        }

        var result = PerceptionProbe.Run();
        var frame = Assert.Single(result.Fields, f => f.Label == "Fotogramma");

        Assert.Equal("UNKNOWN", frame.Source);
        Assert.Contains("capture_not_opened", frame.Value, StringComparison.Ordinal);
        Assert.Contains(result.Fields, f => f.Label == "Motivo" && f.Source == "UNKNOWN");
        Assert.Contains(result.Fields, f => f.Label == "HRESULT" && f.Source == "UNKNOWN");
    }

    [Fact]
    public void ReadFrame_passes_client_area_so_windowed_roi_is_not_the_full_desktop()
    {
        var pixels = new byte[200 * 200 * 4];
        var frame = new CaptureFrame(200, 200, pixels, DataSourceKind.Simulated, DateTime.UtcNow);
        var clientArea = new PixelRect(50, 50, 100, 100);

        ScreenVitalObservation fullscreen = PerceptionProbe.ReadFrame(frame, clientArea: null);
        ScreenVitalObservation windowed = PerceptionProbe.ReadFrame(frame, clientArea);

        Assert.NotEqual(fullscreen.HpRoi, windowed.HpRoi);
        Assert.True(windowed.HpRoi.X >= clientArea.X);
        Assert.True(windowed.HpRoi.Y >= clientArea.Y);
        Assert.True(windowed.HpRoi.X + windowed.HpRoi.Width <= clientArea.X + clientArea.Width);
        Assert.True(windowed.HpRoi.Y + windowed.HpRoi.Height <= clientArea.Y + clientArea.Height);
    }

    [Fact]
    public void Missing_client_window_is_unknown_and_declares_fullscreen_fallback()
    {
        var result = PerceptionProbe.Run(repoRoot: null, clientProcessName: "");
        DisplayField window = Assert.Single(result.Fields, f => f.Label == "Finestra client");
        Assert.Equal("UNKNOWN", window.Source);
        Assert.Contains("client_process_name_empty", window.Value);
        Assert.Contains(PerceptionProbe.FullscreenFallbackNote, result.Summary);
    }
}
