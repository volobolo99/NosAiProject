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

            // Confronto ripulito e senza distinzione di maiuscole: la forma a
            // collezione di DoesNotContain e' uguaglianza esatta, quindi "False",
            // "0.0" e "0 " passavano una guardia che il commento diceva stretta.
            if (field.Source != "UNKNOWN")
                continue;

            string bare = field.Value.Trim();
            Assert.False(
                bare.Equals("0", StringComparison.OrdinalIgnoreCase)
                || bare.Equals("0.0", StringComparison.OrdinalIgnoreCase)
                || bare.Equals("false", StringComparison.OrdinalIgnoreCase)
                || bare == "-",
                $"Il campo '{field.Label}' e' UNKNOWN ma porta '{field.Value}' invece di un motivo.");
        }
    }

    /// <summary>
    /// Il fotogramma concorda sempre con l'esito della cattura, su qualunque
    /// macchina e in tutti e tre i rami che il probe puo' produrre.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Scritto cosi' per due motivi. Una versione precedente usciva con un
    /// <c>return</c> quando la macchina sapeva catturare: contava come test
    /// superato senza aver verificato niente, cioe' copertura apparente. E apriva
    /// DXGI per conto suo per decidere quale ramo prendere — due duplicazioni
    /// sullo stesso output nello stesso processo danno
    /// <c>DXGI_ERROR_NOT_CURRENTLY_AVAILABLE</c>, che avrebbe fabbricato una
    /// intermittenza nuova mentre se ne chiudeva una vecchia.
    /// </para>
    /// <para>
    /// Il ramo si legge dal referto stesso: <c>Stato</c> e' <c>UNKNOWN</c> quando
    /// la duplicazione non si e' aperta e <c>aperto</c> quando si e' aperta. Cosi'
    /// il test asserisce sempre, e non tocca la cattura.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_frame_always_agrees_with_whether_capture_opened()
    {
        var result = PerceptionProbe.Run();
        var stato = Assert.Single(result.Fields, f => f.Label == "Stato");
        var frame = Assert.Single(result.Fields, f => f.Label == "Fotogramma");

        if (stato.Source == "UNKNOWN")
        {
            // La duplicazione non si e' aperta: il fotogramma non esiste e il
            // referto deve dire perche', con tanto di HRESULT.
            Assert.Equal("UNKNOWN", frame.Source);
            Assert.Contains("capture_not_opened", frame.Value, StringComparison.Ordinal);
            Assert.Contains(result.Fields, f => f.Label == "Motivo" && f.Source == "UNKNOWN");
            Assert.Contains(result.Fields, f => f.Label == "HRESULT" && f.Source == "UNKNOWN");
            return;
        }

        // La duplicazione si e' aperta: la dimensione del desktop e' una lettura
        // vera anche quando nessun fotogramma arriva nel budget.
        Assert.Equal("aperto", stato.Value);
        Assert.Contains(result.Fields, f => f.Label == "Desktop" && f.Source == "LIVE");

        // Un fotogramma mancante e' ammesso — un desktop fermo non ne produce —
        // ma allora deve nominarsi, non passare per un'immagine vuota.
        if (frame.Source == "UNKNOWN")
            Assert.Contains("no_frame_within_budget", frame.Value, StringComparison.Ordinal);
        else
            Assert.DoesNotContain("UNKNOWN", frame.Value, StringComparison.Ordinal);
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
