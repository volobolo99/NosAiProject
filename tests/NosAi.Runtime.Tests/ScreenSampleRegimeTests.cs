using System.Globalization;
using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Il regime di geometria sta su ogni riga dei campioni, e due regimi non si
/// mescolano.
/// </summary>
/// <remarks>
/// <para>
/// <b>Da cosa nasce.</b> Il 2026-09-07 i dodici campioni allora in archivio non
/// si adattavano a una sola trasformazione: residuo peggiore 73,6 px contro una
/// soglia di 1,5 caselle (≈38 px). Sottoinsiemi contigui si adattavano bene, e a
/// trasformazioni <i>diverse</i> — 36,6 contro 30,3 px per casella. Il file era
/// una miscela di due sessioni.
/// </para>
/// <para>
/// Un filtro c'era già: <c>ReadSamples</c> teneva solo i campioni della stessa
/// larghezza e altezza dell'ultimo. Non è bastato perché <b>entrambe le sessioni
/// erano 1024×768</b>: la dimensione della finestra non distingue due scale di
/// disegno. Il DPI sì, ed è il terzo componente di <c>GeometryShape</c>, che
/// questo repository già definiva e che la riga non portava.
/// </para>
/// </remarks>
[Collection(ConsoleCaptureCollection.Name)]
public sealed class ScreenSampleRegimeTests : IDisposable
{
    private readonly string _root;
    private readonly TextWriter _originalOut;

    public ScreenSampleRegimeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"nosai_regime_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_root, "data", "perception"));
        _originalOut = Console.Out;
    }

    public void Dispose()
    {
        Console.SetOut(_originalOut);
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private string SamplePath => Path.Combine(_root, "data", "perception", "screen-samples.txt");

    private void Write(bool header, params string[] lines)
    {
        var all = new List<string>();
        if (header) all.Add(ScreenProjectionProbe.SamplesHeader);
        all.AddRange(lines);
        File.WriteAllLines(SamplePath, all);
    }

    private static string Line(int mx, int my, int sx, int sy, int w, int h, uint dpi) =>
        string.Create(CultureInfo.InvariantCulture, $"{mx} {my} {sx} {sy} {w} {h} {dpi}");

    /// <summary>Un anello coerente, tutto allo stesso regime.</summary>
    /// <remarks>
    /// Otto punti e non piu' cinque dal 2026-09-07: lo scarto dei campioni fuori
    /// bersaglio passa ora da <see cref="ScreenProjectionAutoCalibrator"/>, che non
    /// lavora sotto sei coppie. Questi test parlano di regimi, non del numero
    /// minimo, e cinque punti li facevano fallire per un motivo che non e' il loro.
    /// </remarks>
    private static string[] OneRegime(uint dpi, int scale) => new[]
    {
        Line(4, 0, 512 + 4 * scale, 384, 1024, 768, dpi),
        Line(0, 4, 512, 384 + 4 * scale, 1024, 768, dpi),
        Line(-4, 0, 512 - 4 * scale, 384, 1024, 768, dpi),
        Line(0, -4, 512, 384 - 4 * scale, 1024, 768, dpi),
        Line(3, 3, 512 + 3 * scale, 384 + 3 * scale, 1024, 768, dpi),
        Line(-3, 3, 512 - 3 * scale, 384 + 3 * scale, 1024, 768, dpi),
        Line(-3, -3, 512 - 3 * scale, 384 - 3 * scale, 1024, 768, dpi),
        Line(3, -3, 512 + 3 * scale, 384 - 3 * scale, 1024, 768, dpi),
    };

    private string Solve()
    {
        var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            ScreenProjectionProbe.RunSolve(_root);
        }
        finally
        {
            Console.SetOut(_originalOut);
        }
        return captured.ToString();
    }

    [Fact]
    public void UnFileSenzaIntestazioneERifiutatoPerVersione_ENessunCampioneNeEsce()
    {
        // Esattamente la forma del file che esisteva prima: sei campi, nessuna
        // intestazione. Rifiutato intero, non letto a meta' mettendo DPI zero --
        // fabbricare il regime sarebbe fabbricare l'unica cosa che serve sapere.
        Write(header: false, "8 2 843 466 1024 768", "6 7 730 555 1024 768");

        string output = Solve();

        Assert.Contains(ScreenProjectionProbe.SamplesVersionUnsupportedReason, output, StringComparison.Ordinal);
        Assert.Contains("--screen-samples-clear", output, StringComparison.Ordinal);
    }

    [Fact]
    public void UnaRigaASeiCampiDentroUnFileVersionatoNonEntraNelFit()
    {
        Write(header: true, OneRegime(dpi: 96, scale: 30).Concat(new[] { "1 1 542 414 1024 768" }).ToArray());

        string output = Solve();

        // Il file ha otto campioni buoni piu' una riga vecchia: la riga vecchia
        // non partecipa, e il solutore non la conta fra i campioni.
        Assert.DoesNotContain(ScreenProjectionProbe.SamplesVersionUnsupportedReason, output, StringComparison.Ordinal);
        Assert.Contains("8 samples", output, StringComparison.Ordinal);
    }

    [Fact]
    public void CampioniDiUnSoloRegimeSiRisolvonoComePrima()
    {
        Write(header: true, OneRegime(dpi: 96, scale: 30));

        string output = Solve();

        Assert.DoesNotContain("[REFUSED]", output, StringComparison.Ordinal);
        Assert.DoesNotContain("altro regime", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Il caso che conta: stessa finestra, DPI diverso.
    /// </summary>
    /// <remarks>
    /// È la forma esatta della miscela reale — dodici campioni tutti a 1024×768 —
    /// e l'unica che il formato precedente non poteva vedere. Se questo test
    /// passasse anche senza il DPI sulla riga, la modifica non servirebbe a
    /// niente.
    /// </remarks>
    [Fact]
    public void DueRegimiCheDifferisconoSoloPerIlDpi_SonoRiconosciutiComeDue()
    {
        string[] vecchi = OneRegime(dpi: 120, scale: 36);
        string[] nuovi = OneRegime(dpi: 96, scale: 30);
        Write(header: true, vecchi.Concat(nuovi).ToArray());

        string output = Solve();

        Assert.Contains("altro regime", output, StringComparison.Ordinal);
        Assert.Contains("1024x768@120dpi", output, StringComparison.Ordinal);
        Assert.Contains("Tenuti quelli di 1024x768@96dpi", output, StringComparison.Ordinal);
    }

    [Fact]
    public void UnCampioneConDpiZeroNonEntra_PercheUnRegimeIgnotoNonEUnRegime()
    {
        // GeometryShape.IsKnown e' gia' false con DPI zero, e il suo commento
        // dice perche': unknown is not a shape.
        Write(header: true, OneRegime(dpi: 96, scale: 30).Concat(new[]
        {
            Line(1, 1, 542, 414, 1024, 768, dpi: 0),
        }).ToArray());

        string output = Solve();

        Assert.Contains("8 samples", output, StringComparison.Ordinal);
    }
}
