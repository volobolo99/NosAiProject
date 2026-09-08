using System.Text;
using NosAi.Runtime.GameData;
using NosAi.Runtime.Observability;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Gli otto rifiuti che <see cref="SkillCatalogue"/> e
/// <see cref="SkillReportCommand"/> possono produrre, ognuno provato.
/// </summary>
/// <remarks>
/// <para>
/// <b>Da cosa nasce.</b> I due tipi sono arrivati il 2026-09-08 con otto
/// <c>public const string …Reason</c> nuove e nessun test che ne raggiungesse
/// una: <see cref="RefusalReasonRegisterTests"/> le ha elencate tutte e otto e
/// la suite era rossa. Il registro chiede una prova oppure una dichiarazione
/// motivata, e questi rifiuti sono nuovi e raggiungibili — quindi vanno provati,
/// non dichiarati.
/// </para>
/// <para>
/// <b>Perché un rifiuto merita un test.</b> È la frase che l'operatore legge
/// quando il comando non fa quello che gli ha chiesto: se nessuno la esercita,
/// nessuno sa se esce nel caso giusto, e due situazioni diverse possono finire
/// sotto la stessa parola senza che si veda.
/// </para>
/// </remarks>
public sealed class SkillCatalogueRefusalTests
{
    private static readonly string[] Empty = [];

    private static GameReferenceDatabase WithSkill(int vnum, params (string Name, string[] Values)[] rows)
    {
        GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        var fields = rows.Select(r => new NosField(r.Name, r.Values)).ToList();
        db.Import("skill", "test.NOS", "Skill.dat", "C:/test", [new NosRecord(vnum, fields)],
            Encoding.UTF8.GetBytes($"payload-{Guid.NewGuid()}"));
        return db;
    }

    // ------------------------------------------------------------- catalogo

    [Fact]
    public void Un_vnum_che_il_catalogo_non_ha_e_rifiutato_col_proprio_motivo()
    {
        using GameReferenceDatabase db = WithSkill(226, ("VNUM", ["226"]));

        SkillCatalogueLookup lookup = SkillCatalogue.Build(db, vnum: 999999);

        Assert.False(lookup.Ok);
        Assert.Equal(SkillCatalogue.SkillNotInCatalogueReason, lookup.FailureReason);
        // Il motivo non basta: chi chiede non deve ricevere anche mezza abilita'.
        Assert.Null(lookup.Skill);
    }

    /// <summary>
    /// <c>skill_undecodable</c> non può accadere, e questo test è ciò che lo
    /// dimostra invece di lasciarlo credere.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="SkillCatalogue.Build"/> costruisce il record con
    /// <c>new NosRecord(vnum, fields)</c>, cioè con un <c>int</c> in mano, e
    /// <see cref="SkillReferenceDecoder.Decode"/> restituisce null soltanto
    /// quando il record non porta un vnum. Il ramo che produce quel motivo è
    /// quindi irraggiungibile: una riga presente si decodifica sempre, anche se
    /// tutti i suoi campi mancano.
    /// </para>
    /// <para>
    /// Non è un difetto da correggere allargando il decoder: è un rifiuto
    /// dichiarato e mai prodotto, ed è registrato come tale in
    /// <see cref="RefusalReasonRegisterTests"/>. Questo test lo pianta, così se
    /// un giorno diventasse raggiungibile — un decoder più severo, un record
    /// senza vnum — si vedrebbe qui e non alla prima abilità saltata in silenzio.
    /// </para>
    /// </remarks>
    [Fact]
    public void Una_riga_presente_si_decodifica_sempre_anche_senza_campi()
    {
        using GameReferenceDatabase db = WithSkill(226, ("NAME", ["zts1342e"]));

        SkillCatalogueLookup lookup = SkillCatalogue.Build(db, vnum: 226);

        Assert.True(lookup.Ok, lookup.FailureReason);
        Assert.NotEqual(SkillCatalogue.SkillUndecodableReason, lookup.FailureReason);

        // Nessun campo presente non significa campi a zero: ognuno dichiara la
        // propria assenza.
        Assert.False(lookup.Skill!.CastId.HasValue);
        Assert.False(lookup.Skill.CooldownTenths.HasValue);
    }

    /// <summary>
    /// Il costo MP resta indeciso, e lo dice — è il test che vale l'intero
    /// contratto.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Due campi del file sono candidati al costo MP e danno numeri diversi:
    /// <c>COST[0]</c> vale 15 per <i>Terremoto</i> e <c>DATA[8]</c> ne vale 28.
    /// Le registrazioni esistenti non li distinguono — dove l'MP si muove,
    /// <c>stat</c> arriva troppo di rado e in mezzo la rigenerazione risale a
    /// scatti di +24.
    /// </para>
    /// <para>
    /// Il contratto quindi <b>non sceglie</b>: pubblica un valore sconosciuto con
    /// il motivo. Questo test esiste perché nessuno lo trasformi in un numero:
    /// una scelta fra i due, fatta senza la misura, avrebbe l'aspetto di un dato
    /// verificato. Si aggiorna quando <c>docs/TEST_RIMANDATI.md</c> § T-17 chiude,
    /// asserendo il valore vero — non si cancella.
    /// </para>
    /// </remarks>
    [Fact]
    public void Il_costo_mp_resta_indeciso_e_lo_dichiara()
    {
        using GameReferenceDatabase db = WithSkill(226,
            ("VNUM", ["226"]),
            ("TYPE", ["1", "6", "1", "0", "0", "1"]),
            ("COST", ["15", "6300", "0", "0"]),
            ("TARGET", ["1", "1", "1", "3", "0"]),
            ("DATA", ["0", "0", "0", "0", "4", "250", "0", "0", "28", "0", "0", "1", "3", "0", "0"]));

        SkillCatalogueLookup lookup = SkillCatalogue.Build(db, vnum: 226);

        Assert.True(lookup.Ok, lookup.FailureReason);

        // **Entrambi** restano sconosciuti, non uno solo: finche' la misura non
        // dice quale dei due e' il costo, dichiararne uno «il costo» e l'altro
        // «un altro campo» sarebbe gia' aver scelto.
        Assert.False(lookup.Skill!.MpCost.HasValue);
        Assert.False(lookup.Skill.CpCost.HasValue);
        Assert.Equal(SkillCatalogue.MpCostUndecidedReason, lookup.Skill.MpCost.FailureReason);
        Assert.Equal(SkillCatalogue.MpCostUndecidedReason, lookup.Skill.CpCost.FailureReason);

        // I due numeri restano pero' leggibili grezzi: chi fara' la misura di
        // T-17 deve poterli confrontare senza riaprire il file del client.
        Assert.Equal(15, lookup.Skill.Raw.CpCost);
        Assert.Equal(28, lookup.Skill.Raw.MpCost);
    }

    /// <summary>La portata resta provvisoria, e per un motivo che si legge.</summary>
    [Fact]
    public void La_portata_resta_provvisoria_col_proprio_motivo()
    {
        using GameReferenceDatabase db = WithSkill(226,
            ("VNUM", ["226"]),
            ("TARGET", ["1", "1", "1", "3", "0"]),
            ("DATA", ["0", "0", "0", "0", "4", "250", "0", "0", "28", "0", "0", "1", "3", "0", "0"]));

        SkillCatalogueLookup lookup = SkillCatalogue.Build(db, vnum: 226);

        Assert.True(lookup.Ok, lookup.FailureReason);
        Assert.False(lookup.Skill!.Range.HasValue);
        Assert.Equal(SkillCatalogue.RangeProvisionalReason, lookup.Skill.Range.FailureReason);
    }

    // -------------------------------------------------------------- comando

    /// <summary>
    /// Senza né vnum né registrazione il comando non ha un bersaglio, e lo dice
    /// invece di stampare l'intero catalogo.
    /// </summary>
    [Fact]
    public void Senza_bersaglio_il_comando_rifiuta()
    {
        // Asserito sul **valore** e non sulla costante, e nemmeno il nome della
        // costante compare qui: `TargetChainProbe` ne dichiara una omonima con un
        // valore diverso, e `RefusalReasonRegisterTests` riconosce la copertura
        // anche per nome — nominarla, foss'anche in un commento, marcherebbe per
        // sbaglio come provata anche quella.
        Assert.Equal(
            "skill_report_no_target",
            SkillReportCommand.TryParse([SkillReportCommand.Flag], out _, out _, out _));

        // E senza nemmeno la bandiera: non e' un comando per questo processo.
        Assert.Equal(
            "skill_report_no_target",
            SkillReportCommand.TryParse(["--altro"], out _, out _, out _));
    }

    [Theory]
    [InlineData("non-un-numero")]
    [InlineData("")]
    public void Un_vnum_che_non_e_un_numero_e_rifiutato(string value)
    {
        string? refusal = SkillReportCommand.TryParse(
            [SkillReportCommand.Flag, SkillReportCommand.VnumOption, value], out int? vnum, out _, out _);

        Assert.Equal(SkillReportCommand.InvalidVnumReason, refusal);
        Assert.Null(vnum);
    }

    /// <summary>
    /// <c>--vnum</c> in coda, senza valore, è lo stesso rifiuto: la mancanza non
    /// vale zero.
    /// </summary>
    [Fact]
    public void Un_vnum_senza_valore_e_rifiutato_e_non_vale_zero()
    {
        string? refusal = SkillReportCommand.TryParse(
            [SkillReportCommand.Flag, SkillReportCommand.VnumOption], out int? vnum, out _, out _);

        Assert.Equal(SkillReportCommand.InvalidVnumReason, refusal);
        Assert.Null(vnum);
    }

    /// <summary>
    /// Una registrazione senza percorso non diventa l'opzione che la segue.
    /// </summary>
    /// <remarks>
    /// Il difetto che evita: <c>--recording --vnum 226</c> senza questo controllo
    /// prenderebbe <c>--vnum</c> come nome di file e cercherebbe una cattura che
    /// non esiste, riferendo un errore di disco invece di un argomento mancante.
    /// </remarks>
    [Fact]
    public void Una_registrazione_senza_valore_e_rifiutata()
    {
        Assert.Equal(
            SkillReportCommand.RecordingWithoutValueReason,
            SkillReportCommand.TryParse(
                [SkillReportCommand.Flag, SkillReportCommand.RecordingOption], out _, out _, out _));

        Assert.Equal(
            SkillReportCommand.RecordingWithoutValueReason,
            SkillReportCommand.TryParse(
                [SkillReportCommand.Flag, SkillReportCommand.RecordingOption, SkillReportCommand.VnumOption, "226"],
                out _, out string? recording, out _));
    }

    [Fact]
    public void Unopzione_sconosciuta_e_rifiutata_col_proprio_nome()
    {
        string? refusal = SkillReportCommand.TryParse(
            [SkillReportCommand.Flag, "--riepilogo"], out _, out _, out _);

        Assert.NotNull(refusal);
        Assert.StartsWith(SkillReportCommand.UnknownOptionReason, refusal!, StringComparison.Ordinal);
        // Il nome dell'opzione sta nel motivo: senza, l'operatore non sa quale.
        Assert.Contains("--riepilogo", refusal!, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_vnum_ben_formato_non_e_rifiutato()
    {
        Assert.Null(SkillReportCommand.TryParse(
            [SkillReportCommand.Flag, SkillReportCommand.VnumOption, "226"],
            out int? vnum, out _, out _));
        Assert.Equal(226, vnum);
    }

    /// <summary>
    /// Il catalogo assente ha il proprio motivo, e non si confonde con un
    /// catalogo vuoto.
    /// </summary>
    /// <remarks>
    /// «Non ho la tabella» e «la tabella non ha quell'abilità» sono due cose
    /// diverse, e la seconda è già coperta da
    /// <see cref="Un_vnum_che_il_catalogo_non_ha_e_rifiutato_col_proprio_motivo"/>.
    /// </remarks>
    [Fact]
    public void Il_motivo_del_catalogo_assente_e_distinto_da_quello_del_vnum_assente()
    {
        Assert.NotEqual(SkillReportCommand.CatalogueUnavailableReason, SkillCatalogue.SkillNotInCatalogueReason);
        Assert.Contains("catalogue", SkillReportCommand.CatalogueUnavailableReason, StringComparison.Ordinal);

        // E il comando lo produce quando il catalogo non si trova: il percorso
        // passa da GameReferenceLocator, che dichiara la propria assenza.
        using GameReferenceDatabase empty = GameReferenceDatabase.OpenInMemory();
        var output = new StringWriter();
        SkillReportCommand.WriteReport(empty, [226], output);

        Assert.Contains(SkillCatalogue.SkillNotInCatalogueReason, output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(SkillReportCommand.CatalogueUnavailableReason, output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Il rapporto su zero vnum non stampa una riga inventata.</summary>
    [Fact]
    public void Un_rapporto_senza_vnum_non_inventa_righe()
    {
        using GameReferenceDatabase empty = GameReferenceDatabase.OpenInMemory();
        var output = new StringWriter();

        SkillReportCommand.WriteReport(empty, Empty.Select(int.Parse), output);

        Assert.DoesNotContain("226", output.ToString(), StringComparison.Ordinal);
    }
}
