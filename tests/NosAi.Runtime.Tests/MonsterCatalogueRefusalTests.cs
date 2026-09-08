using System.Text;
using NosAi.Runtime.GameData;
using NosAi.Runtime.Observability;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// I rifiuti che <see cref="MonsterCatalogue"/> e
/// <see cref="MonsterReportCommand"/> possono produrre, ognuno provato, e la
/// divisione confermato/provvisorio che il catalogo pubblica.
/// </summary>
public sealed class MonsterCatalogueRefusalTests
{
    private static readonly string[] Empty = [];

    private static GameReferenceDatabase WithMonster(int vnum, params (string Name, string[] Values)[] rows)
    {
        GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        var fields = rows.Select(r => new NosField(r.Name, r.Values)).ToList();
        db.Import("monster", "NSgtdData.NOS", "monster.dat", "C:/test", [new NosRecord(vnum, fields)],
            Encoding.UTF8.GetBytes($"payload-{Guid.NewGuid()}"));
        return db;
    }

    // ------------------------------------------------------------- catalogo

    [Fact]
    public void Un_vnum_che_il_catalogo_non_ha_e_rifiutato_col_proprio_motivo()
    {
        using GameReferenceDatabase db = WithMonster(45, ("VNUM", ["45"]));

        MonsterCatalogueLookup lookup = MonsterCatalogue.Build(db, vnum: 999999);

        Assert.False(lookup.Ok);
        Assert.Equal(MonsterCatalogue.MonsterNotInCatalogueReason, lookup.FailureReason);
        // Il motivo non basta: chi chiede non deve ricevere anche mezzo mostro.
        Assert.Null(lookup.Monster);
    }

    /// <summary>
    /// <c>monster_undecodable</c> non può accadere, e questo test è ciò che lo
    /// dimostra invece di lasciarlo credere.
    /// </summary>
    [Fact]
    public void Una_riga_presente_si_decodifica_sempre_anche_senza_campi()
    {
        using GameReferenceDatabase db = WithMonster(45, ("NAME", ["zts45e"]));

        MonsterCatalogueLookup lookup = MonsterCatalogue.Build(db, vnum: 45);

        Assert.True(lookup.Ok, lookup.FailureReason);
        Assert.NotEqual(MonsterCatalogue.MonsterUndecodableReason, lookup.FailureReason);

        // Nessun campo presente non significa campi a zero: ognuno dichiara la
        // propria assenza.
        Assert.False(lookup.Monster!.Level.HasValue);
        Assert.False(lookup.Monster.MaxHpBonus.HasValue);
        Assert.False(lookup.Monster.MaxMpBonus.HasValue);
    }

    /// <summary>
    /// Il livello è l'unico campo confermato; i bonus HP/MP restano provvisori e
    /// lo dicono — è il test che vale l'intero contratto.
    /// </summary>
    /// <remarks>
    /// Il filo porta il livello (<c>st</c> campo 3, confermato 26/26 contro
    /// <c>entity.level</c>) e il totale vita/mana (<c>st</c> campi 9/10). Il
    /// catalogo porta <c>HP/MP[0]</c> e <c>HP/MP[1]</c>, che sono un <b>bonus</b>
    /// sul totale, non il totale: il bonus non si isola dal totale osservato, e
    /// dichiararlo confermato sarebbe presentare una misura che non esiste.
    /// </remarks>
    [Fact]
    public void Il_livello_e_confermato_e_i_bonus_hp_mp_restano_provvisori()
    {
        using GameReferenceDatabase db = WithMonster(45,
            ("VNUM", ["45"]),
            ("NAME", ["zts45e"]),
            ("LEVEL", ["8"]),
            ("HP/MP", ["310", "60"]));

        MonsterCatalogueLookup lookup = MonsterCatalogue.Build(db, vnum: 45);

        Assert.True(lookup.Ok, lookup.FailureReason);

        Assert.True(lookup.Monster!.Level.HasValue);
        Assert.Equal(8, lookup.Monster.Level.Value);

        // **Entrambi** restano sconosciuti: finché la misura non dice che il
        // bonus è isolabile dal totale, dichiararne uno «il massimo» sarebbe
        // già aver scelto una lettura che il filo non sostiene.
        Assert.False(lookup.Monster.MaxHpBonus.HasValue);
        Assert.False(lookup.Monster.MaxMpBonus.HasValue);
        Assert.Equal(MonsterCatalogue.HpBonusProvisionalReason, lookup.Monster.MaxHpBonus.FailureReason);
        Assert.Equal(MonsterCatalogue.MpBonusProvisionalReason, lookup.Monster.MaxMpBonus.FailureReason);

        // I numeri restano però leggibili grezzi: chi farà la misura deve poterli
        // confrontare senza riaprire il file del client.
        Assert.Equal(310, lookup.Monster.Raw.MaxHpBonus);
        Assert.Equal(60, lookup.Monster.Raw.MaxMpBonus);
    }

    // -------------------------------------------------------------- comando

    /// <summary>
    /// Senza né vnum né registrazione il comando non ha un bersaglio, e lo dice
    /// invece di stampare l'intero catalogo.
    /// </summary>
    [Fact]
    public void Senza_bersaglio_il_comando_rifiuta()
    {
        // Asserito sul **valore** e non sulla costante: SkillReportCommand
        // dichiara una costante omonima con un valore diverso, e
        // RefusalReasonRegisterTests riconosce la copertura anche per nome —
        // nominarla qui marcherebbe per sbaglio come provata anche quella.
        Assert.Equal(
            "monster_report_no_target",
            MonsterReportCommand.TryParse([MonsterReportCommand.Flag], out _, out _, out _));

        // E senza nemmeno la bandiera: non è un comando per questo processo.
        Assert.Equal(
            "monster_report_no_target",
            MonsterReportCommand.TryParse(["--altro"], out _, out _, out _));
    }

    [Theory]
    [InlineData("non-un-numero")]
    [InlineData("")]
    public void Un_vnum_che_non_e_un_numero_e_rifiutato(string value)
    {
        string? refusal = MonsterReportCommand.TryParse(
            [MonsterReportCommand.Flag, MonsterReportCommand.VnumOption, value], out int? vnum, out _, out _);

        Assert.Equal(MonsterReportCommand.InvalidVnumReason, refusal);
        Assert.Null(vnum);
    }

    [Fact]
    public void Un_vnum_senza_valore_e_rifiutato_e_non_vale_zero()
    {
        string? refusal = MonsterReportCommand.TryParse(
            [MonsterReportCommand.Flag, MonsterReportCommand.VnumOption], out int? vnum, out _, out _);

        Assert.Equal(MonsterReportCommand.InvalidVnumReason, refusal);
        Assert.Null(vnum);
    }

    [Fact]
    public void Una_registrazione_senza_valore_e_rifiutata()
    {
        Assert.Equal(
            MonsterReportCommand.RecordingWithoutValueReason,
            MonsterReportCommand.TryParse(
                [MonsterReportCommand.Flag, MonsterReportCommand.RecordingOption], out _, out _, out _));

        Assert.Equal(
            MonsterReportCommand.RecordingWithoutValueReason,
            MonsterReportCommand.TryParse(
                [MonsterReportCommand.Flag, MonsterReportCommand.RecordingOption, MonsterReportCommand.VnumOption, "45"],
                out _, out string? recording, out _));
    }

    [Fact]
    public void Unopzione_sconosciuta_e_rifiutata_col_proprio_nome()
    {
        string? refusal = MonsterReportCommand.TryParse(
            [MonsterReportCommand.Flag, "--riepilogo"], out _, out _, out _);

        Assert.NotNull(refusal);
        Assert.StartsWith(MonsterReportCommand.UnknownOptionReason, refusal!, StringComparison.Ordinal);
        // Il nome dell'opzione sta nel motivo: senza, l'operatore non sa quale.
        Assert.Contains("--riepilogo", refusal!, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_vnum_ben_formato_non_e_rifiutato()
    {
        Assert.Null(MonsterReportCommand.TryParse(
            [MonsterReportCommand.Flag, MonsterReportCommand.VnumOption, "45"],
            out int? vnum, out _, out _));
        Assert.Equal(45, vnum);
    }

    /// <summary>
    /// Il catalogo assente ha il proprio motivo, e non si confonde con un
    /// catalogo vuoto.
    /// </summary>
    [Fact]
    public void Il_motivo_del_catalogo_assente_e_distinto_da_quello_del_vnum_assente()
    {
        // Il valore è asserito, non il nome della costante: SkillReportCommand
        // dichiara una costante omonima con un valore diverso.
        Assert.Equal("monster_catalogue_unavailable", MonsterReportCommand.CatalogueUnavailableReason);
        Assert.NotEqual(MonsterCatalogue.MonsterNotInCatalogueReason, MonsterReportCommand.CatalogueUnavailableReason);
        Assert.Contains("catalogue", MonsterReportCommand.CatalogueUnavailableReason, StringComparison.Ordinal);

        // E il comando produce il motivo del vnum assente quando la tabella non lo
        // ha: il percorso passa da GameReferenceLocator, che dichiara la propria
        // assenza.
        using GameReferenceDatabase empty = GameReferenceDatabase.OpenInMemory();
        var output = new StringWriter();
        MonsterReportCommand.WriteReport(empty, [45], output);

        Assert.Contains(MonsterCatalogue.MonsterNotInCatalogueReason, output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("monster_catalogue_unavailable", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Il rapporto su zero vnum non stampa una riga inventata.</summary>
    [Fact]
    public void Un_rapporto_senza_vnum_non_inventa_righe()
    {
        using GameReferenceDatabase empty = GameReferenceDatabase.OpenInMemory();
        var output = new StringWriter();

        MonsterReportCommand.WriteReport(empty, Empty.Select(int.Parse), output);

        Assert.DoesNotContain("45", output.ToString(), StringComparison.Ordinal);
    }
}
