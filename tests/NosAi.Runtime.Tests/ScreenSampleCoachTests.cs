using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Il consigliere separa la sessione che ha calibrato da quella che è stata
/// rifiutata, usando i clic veri di entrambe.
/// </summary>
/// <remarks>
/// <para>
/// <b>Da cosa nasce.</b> Il 2026-09-07 una sessione di cinque clic è stata
/// rifiutata da <see cref="ScreenProjectionCalibration"/> con
/// <c>scale_not_determined:72x43pct</c>. I clic erano buoni — rifittati insieme
/// ai dodici già in archivio il loro residuo peggiore era 1,41 caselle, meglio di
/// nove dei dodici — ma cadevano tutti in una direzione e due erano lo stesso
/// pixel. Lo strumento aveva chiesto esattamente questo: <i>«5 volte, in
/// direzioni diverse»</i>. L'istruzione era il difetto.
/// </para>
/// <para>
/// I due insiemi in questi test sono quelli reali, copiati da
/// <c>data/perception/screen-samples-2026-09-03.v0.txt</c> e
/// <c>data/perception/screen-samples.txt</c>. Non sono inventati per far passare
/// il test: sono la sessione che ha prodotto l'unica calibrazione accettata di
/// questo repository e la sessione che è stata rifiutata.
/// </para>
/// </remarks>
public sealed class ScreenSampleCoachTests
{
    /// <summary>I dodici clic del 2026-09-03, disposti ad anello.</summary>
    private static readonly (int Dx, int Dy, int Px, int Py)[] AnelloDodici =
    [
        (8, 2, 843, 466), (6, 7, 730, 555), (2, 9, 559, 597), (-3, 8, 375, 583),
        (-8, 6, 228, 515), (-10, 0, 157, 412), (-10, -8, 181, 302), (-8, -16, 294, 213),
        (-3, -20, 465, 171), (5, -18, 649, 185), (10, -13, 796, 253), (11, -6, 867, 356)
    ];

    /// <summary>I cinque clic del 2026-09-07, tutti verso il basso a sinistra.</summary>
    private static readonly (int Dx, int Dy, int Px, int Py)[] CinqueRifiutati =
    [
        (-4, 1, 375, 454), (-1, 7, 493, 566), (-9, -5, 174, 346),
        (-9, -5, 174, 346), (-11, -7, 143, 315)
    ];

    private static ScreenProjectionSample Sample((int Dx, int Dy, int Px, int Py) row) =>
        new(new MapPoint(row.Dx, row.Dy), row.Px, row.Py);

    private static ScreenSampleCoach Feed((int Dx, int Dy, int Px, int Py)[] rows)
    {
        var coach = new ScreenSampleCoach();
        foreach (var row in rows)
            coach.Offer(Sample(row), characterWasAtRest: true);
        return coach;
    }

    [Fact]
    public void Un_clic_col_personaggio_ancora_in_cammino_e_rifiutato()
    {
        var coach = new ScreenSampleCoach();

        ScreenSampleVerdict verdict = coach.Offer(Sample((8, 2, 843, 466)), characterWasAtRest: false);

        Assert.False(verdict.Accepted);
        Assert.Equal(ScreenSampleCoach.CharacterWasMovingReason, verdict.Reason);
        Assert.Empty(coach.Accepted);
    }

    [Fact]
    public void Lo_stesso_pixel_due_volte_conta_una_volta_sola()
    {
        var coach = new ScreenSampleCoach();
        Assert.True(coach.Offer(Sample((-9, -5, 174, 346)), true).Accepted);

        ScreenSampleVerdict second = coach.Offer(Sample((-9, -5, 174, 346)), true);

        Assert.False(second.Accepted);
        Assert.Equal(ScreenSampleCoach.PixelAlreadySampledReason, second.Reason);
        Assert.Single(coach.Accepted);
    }

    [Fact]
    public void Un_clic_troppo_vicino_al_personaggio_e_rifiutato()
    {
        var coach = new ScreenSampleCoach();

        // (-4,1) dista 4,12 caselle: sotto il minimo, ed e' il primo dei cinque
        // clic della sessione rifiutata.
        ScreenSampleVerdict verdict = coach.Offer(Sample((-4, 1, 375, 454)), true);

        Assert.False(verdict.Accepted);
        Assert.Equal(ScreenSampleCoach.TooCloseReason, verdict.Reason);
    }

    /// <summary>
    /// Il test che dice il perché di questa classe: l'anello soddisfa, i cinque no.
    /// </summary>
    [Fact]
    public void Lanello_di_dodici_soddisfa_e_i_cinque_rifiutati_no()
    {
        ScreenSampleCoach anello = Feed(AnelloDodici);
        ScreenSampleCoach cinque = Feed(CinqueRifiutati);

        Assert.True(anello.IsSatisfied);
        Assert.Equal(12, anello.Coverage.Accepted);
        Assert.Equal(ScreenSampleCoach.SectorCount, anello.Coverage.SectorsFilled);

        // Dei cinque ne sopravvivono tre: uno era troppo vicino, uno era il
        // doppione dello stesso pixel.
        Assert.False(cinque.IsSatisfied);
        Assert.Equal(3, cinque.Coverage.Accepted);
    }

    /// <summary>
    /// La dispersione è la grandezza che ha deciso il rifiuto, e si vede.
    /// </summary>
    [Fact]
    public void La_dispersione_dellanello_e_di_altri_ordini_di_grandezza()
    {
        double anello = Feed(AnelloDodici).Coverage.Spread;
        double cinque = Feed(CinqueRifiutati).Coverage.Spread;

        Assert.True(anello > 100_000, $"anello {anello:F0}");
        Assert.True(cinque < 100, $"cinque {cinque:F0}");
    }

    [Fact]
    public void Il_consiglio_nomina_una_direzione_ancora_scoperta()
    {
        var coach = new ScreenSampleCoach();
        coach.Offer(Sample((8, 2, 843, 466)), true);   // a destra

        // Il primo settore scoperto in ordine e' quello successivo a destra.
        Assert.Contains("in basso a destra", coach.Advice);

        coach.Offer(Sample((6, 7, 730, 555)), true);   // in basso a destra
        Assert.Contains("in basso", coach.Advice);
        Assert.DoesNotContain("in basso a destra", coach.Advice);
    }

    [Fact]
    public void Coperte_tutte_le_direzioni_il_consiglio_chiede_solo_piu_lontano()
    {
        var coach = new ScreenSampleCoach(wanted: 12);
        foreach (var row in AnelloDodici.Take(11))
            coach.Offer(Sample(row), true);

        Assert.Equal(ScreenSampleCoach.SectorCount, coach.Coverage.SectorsFilled);
        Assert.Contains("più lontano", coach.Advice);
        Assert.Contains("Ne manca", coach.Advice);
    }

    [Fact]
    public void Il_consigliere_non_rifa_il_fit_ma_chiede_alla_calibrazione()
    {
        ScreenSampleCoach anello = Feed(AnelloDodici);

        bool solved = anello.TrySolve(
            1024, 768, new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc),
            out ScreenProjectionCalibration calibration, out string? failure, clientDpi: 120);

        // I dodici del 3 settembre non si risolvono, e il motivo e' il residuo:
        // e' la misura che ha aperto T-15. Quello che questo test fissa non e'
        // l'esito ma la delega -- la risposta e' quella di TrySolve, parola per
        // parola, non una seconda aritmetica scritta nel consigliere.
        ScreenProjectionCalibration.TrySolve(
            anello.Accepted, 1024, 768, new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc),
            out _, out string? direct, clientDpi: 120);

        Assert.Equal(direct, failure);
        Assert.Equal(solved, calibration.IsCalibrated);
    }
}
