using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// La calibrazione che sta nel repository si carica e proietta dentro la
/// finestra.
/// </summary>
/// <remarks>
/// <para>
/// <b>Perché un test su un file di dati.</b> Fino al 2026-09-07
/// <c>data/perception/</c> era escluso dal versionamento, quindi non c'era niente
/// da sorvegliare: la calibrazione esisteva su una macchina sola e nessun test
/// poteva vederla. Ora è versionata, ed è la misura del client reale su cui
/// poggia ogni clic che il runtime calcola. Un file che cambia formato, o che
/// viene riscritto da una sessione andata male, si vede qui e non alla prima
/// azione sbagliata sul client.
/// </para>
/// <para>
/// Il test non verifica <i>che i numeri siano giusti</i> — quello lo dice il
/// residuo, misurato quando la calibrazione è stata scritta, e in ultima istanza
/// un operatore che guarda dove finisce il clic. Verifica che il file si legga,
/// che la mappa sia invertibile, e che il personaggio e i suoi dintorni cadano
/// dentro la finestra: gli errori che rendono una calibrazione peggio che
/// assente, perché una calibrazione assente rifiuta e una sbagliata clicca.
/// </para>
/// </remarks>
public sealed class CommittedScreenCalibrationTests
{
    private static string? CalibrationPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;

        if (directory is null) return null;
        string path = Path.Combine(directory.FullName, ScreenProjectionCalibration.RelativePath);
        return File.Exists(path) ? path : null;
    }

    private static ScreenProjectionCalibration Load()
    {
        string? path = CalibrationPath();
        Assert.NotNull(path);

        ScreenProjectionCalibration calibration =
            ScreenProjectionCalibration.Load(path!, out string? failure);
        Assert.True(calibration.IsCalibrated, failure ?? "nessun motivo riferito");
        return calibration;
    }

    [Fact]
    public void Si_carica_e_dichiara_il_proprio_regime()
    {
        ScreenProjectionCalibration calibration = Load();

        // Il regime va dichiarato per intero: una calibrazione senza DPI
        // appartiene a un regime che nessun campione ha mai avuto, ed e' il
        // difetto che il file portava fino al 2026-09-07.
        Assert.True(calibration.ClientWidth > 0);
        Assert.True(calibration.ClientHeight > 0);
        Assert.True(calibration.ClientDpi > 0);
        Assert.NotNull(calibration.CalibratedAtUtc);
    }

    [Fact]
    public void Il_personaggio_e_disegnato_dentro_la_finestra()
    {
        ScreenProjectionCalibration calibration = Load();

        (double X, double Y)? anchor = calibration.ProjectDelta(new MapPoint(0, 0));

        Assert.NotNull(anchor);
        Assert.InRange(anchor!.Value.X, 0, calibration.ClientWidth);
        Assert.InRange(anchor.Value.Y, 0, calibration.ClientHeight);
    }

    /// <summary>
    /// Le otto direzioni attorno al personaggio proiettano in otto punti diversi,
    /// e nell'ordine giusto.
    /// </summary>
    /// <remarks>
    /// Il controllo che una mappa collassata non supera: se la trasformazione
    /// schiaccia il piano, o scambia gli assi, due direzioni opposte finiscono
    /// dalla stessa parte. Le coordinate mappa crescono verso destra e verso il
    /// basso dello schermo, come dicono i campioni.
    /// </remarks>
    [Fact]
    public void Le_direzioni_opposte_finiscono_da_parti_opposte()
    {
        ScreenProjectionCalibration calibration = Load();

        (double X, double Y) origin = calibration.ProjectDelta(new MapPoint(0, 0))!.Value;
        (double X, double Y) east = calibration.ProjectDelta(new MapPoint(5, 0))!.Value;
        (double X, double Y) west = calibration.ProjectDelta(new MapPoint(-5, 0))!.Value;
        (double X, double Y) south = calibration.ProjectDelta(new MapPoint(0, 5))!.Value;
        (double X, double Y) north = calibration.ProjectDelta(new MapPoint(0, -5))!.Value;

        Assert.True(east.X > origin.X, $"est {east.X:F0} non e' a destra di {origin.X:F0}");
        Assert.True(west.X < origin.X, $"ovest {west.X:F0} non e' a sinistra di {origin.X:F0}");
        Assert.True(south.Y > origin.Y, $"sud {south.Y:F0} non e' sotto {origin.Y:F0}");
        Assert.True(north.Y < origin.Y, $"nord {north.Y:F0} non e' sopra {origin.Y:F0}");
    }

    /// <summary>
    /// Il passo della casella è dell'ordine di grandezza che il client disegna.
    /// </summary>
    /// <remarks>
    /// Non una verifica dei numeri, che sarebbe ricopiarli: una rete larga attorno
    /// a ciò che tre sessioni reali hanno misurato — da 30 a 42 px in orizzontale
    /// e da 13 a 23 in verticale. Una calibrazione che dicesse tre pixel per
    /// casella, come i campioni con la posizione letta in ritardo del 2026-09-07,
    /// cadrebbe qui.
    /// </remarks>
    [Fact]
    public void Il_passo_della_casella_e_plausibile()
    {
        ScreenProjectionCalibration calibration = Load();

        double pitchX = Math.Sqrt((calibration.A * calibration.A) + (calibration.D * calibration.D));
        double pitchY = Math.Sqrt((calibration.B * calibration.B) + (calibration.E * calibration.E));

        Assert.InRange(pitchX, 10, 80);
        Assert.InRange(pitchY, 5, 60);
    }

    /// <summary>
    /// Quanti campioni sono stati lasciati fuori è scritto, e sono pochi.
    /// </summary>
    /// <remarks>
    /// Il tetto vive in <see cref="ScreenProjectionAutoCalibrator.MaxDroppedFraction"/>
    /// e questo test guarda il risultato: una calibrazione che avesse buttato via
    /// mezza sessione descriverebbe i campioni sopravvissuti invece dello schermo,
    /// e il campo esiste perché lo si possa vedere senza rifare il fit.
    /// </remarks>
    [Fact]
    public void I_campioni_lasciati_fuori_sono_pochi_e_dichiarati()
    {
        ScreenProjectionCalibration calibration = Load();

        int used = calibration.VerifiedAgainstSamples + ScreenProjectionCalibration.MinimumSamples;
        Assert.True(calibration.DiscardedSamples >= 0);
        Assert.True(
            calibration.DiscardedSamples <= used * ScreenProjectionAutoCalibrator.MaxDroppedFraction,
            $"scartati {calibration.DiscardedSamples} su {used + calibration.DiscardedSamples} offerti");
    }
}
