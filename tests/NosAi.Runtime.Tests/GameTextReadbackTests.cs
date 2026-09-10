using NosAi.Runtime.GameData;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// La tabella <c>text</c> si rilegge, e le 7 445 righe di <c>conststring</c>
/// smettono di essere solo contate.
/// </summary>
/// <remarks>
/// <para>
/// <b>Da cosa nasce.</b> Fino al 2026-09-08 <see cref="GameReferenceDatabase"/>
/// aveva su quella tabella soltanto <c>ImportText</c> e <c>TextCount</c>:
/// scrittura e conteggio, nessuna lettura. I nomi delle entità la raggiungono
/// per un'altra strada — <c>DisplayName</c> parte da un vnum e passa per
/// <c>entity.name_key</c> — che le tabelle di solo testo non hanno, perché non
/// descrivono nessuna entità. Il risultato era che il catalogo importava i
/// messaggi del client e nessuna riga di codice poteva chiedergliene uno.
/// </para>
/// <para>
/// <b>Perché serve adesso.</b> T-16 resta aperto su un punto solo: il filo porta
/// un id di messaggio (<c>sayi</c> campo 4) e il catalogo porta il testo, e il
/// legame fra i due non è stabilito — verificato due volte che l'id non indicizza
/// <c>conststring</c>, né direttamente né a scarto costante. La prova che lo
/// stabilirebbe è una coppia osservata: cosa c'era a schermo e quale riga della
/// cattura. Per confrontarle bisogna poter cercare il testo per quello che dice,
/// ed è ciò che <c>SearchText</c> fa — <b>senza stabilire nessun legame</b>:
/// restituisce righe, decide chi legge.
/// </para>
/// </remarks>
public sealed class GameTextReadbackTests
{
    private static GameReferenceDatabase WithText(params (string Key, string Value)[] rows)
    {
        GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        db.ImportText("IT", "conststring", rows.ToDictionary(r => r.Key, r => r.Value));
        return db;
    }

    [Fact]
    public void Una_chiave_presente_restituisce_il_suo_testo()
    {
        using GameReferenceDatabase db = WithText(("10666", "Hai raccolto [%s]:"));

        Assert.Equal("Hai raccolto [%s]:", db.TextValue("IT", "conststring", "10666"));
    }

    /// <summary>
    /// Chiave assente è <c>null</c>, non stringa vuota.
    /// </summary>
    /// <remarks>
    /// «Questa tabella non ha quella chiave» e «quella chiave è un testo vuoto»
    /// sono risposte diverse, e chi cerca il testo di un id del filo deve poterle
    /// distinguere: la prima dice di continuare a cercare, la seconda no.
    /// </remarks>
    [Fact]
    public void Una_chiave_assente_e_nulla_e_non_vuota()
    {
        using GameReferenceDatabase db = WithText(("10666", "Hai raccolto [%s]:"));

        Assert.Null(db.TextValue("IT", "conststring", "975"));
        Assert.Null(db.TextValue("IT", "conststring", ""));
    }

    /// <summary>La lingua e la tabella fanno parte della chiave, e separano.</summary>
    [Fact]
    public void Un_altra_lingua_o_un_altra_tabella_non_rispondono()
    {
        using GameReferenceDatabase db = WithText(("1", "OK"));

        Assert.Equal("OK", db.TextValue("IT", "conststring", "1"));
        Assert.Null(db.TextValue("EN", "conststring", "1"));
        Assert.Null(db.TextValue("IT", "monster", "1"));
    }

    [Fact]
    public void La_ricerca_trova_per_sottostringa_e_ordina_dal_piu_corto()
    {
        using GameReferenceDatabase db = WithText(
            ("3099", "è raccolto."),
            ("10665", "[%s] raccolta:"),
            ("10666", "Hai raccolto [%s]:"),
            ("1", "OK"));

        IReadOnlyList<GameTextRow> found = db.SearchText("IT", "conststring", "raccolt", 10);

        Assert.Equal(3, found.Count);
        // Dal più corto: chi legge una nota scritta a mano vuole prima le righe
        // che dicono solo quella cosa, non i paragrafi che la contengono.
        Assert.Equal("3099", found[0].Key);
        Assert.DoesNotContain(found, r => r.Key == "1");
    }

    /// <summary>
    /// Il limite è rispettato, ed è un parametro perché un risultato solo non è
    /// una conferma.
    /// </summary>
    [Fact]
    public void Il_limite_e_rispettato_e_zero_non_cerca()
    {
        using GameReferenceDatabase db = WithText(
            ("1", "raccolto a"), ("2", "raccolto ab"), ("3", "raccolto abc"));

        Assert.Equal(2, db.SearchText("IT", "conststring", "raccolto", 2).Count);
        Assert.Empty(db.SearchText("IT", "conststring", "raccolto", 0));
        Assert.Empty(db.SearchText("IT", "conststring", "", 10));
    }

    /// <summary>
    /// I caratteri jolly di SQL non escono dal testo cercato.
    /// </summary>
    /// <remarks>
    /// La nota che l'operatore scrive nel pannello è testo libero e può contenere
    /// <c>%</c> o <c>_</c> senza volerli dire: cercare «100%» non deve diventare
    /// cercare qualunque cosa, che è ciò che <c>LIKE</c> farebbe senza
    /// <c>ESCAPE</c>.
    /// </remarks>
    [Fact]
    public void Il_percento_scritto_dall_operatore_e_un_percento()
    {
        using GameReferenceDatabase db = WithText(
            ("1", "Recuperi il 100% della vita"),
            ("2", "Nessun simbolo qui"),
            ("3", "Slot _ vuoto"));

        IReadOnlyList<GameTextRow> percent = db.SearchText("IT", "conststring", "100%", 10);
        Assert.Single(percent);
        Assert.Equal("1", percent[0].Key);

        // Senza ESCAPE l'underscore vale «un carattere qualunque» e "Slot _"
        // pescherebbe anche "Slot A".
        IReadOnlyList<GameTextRow> underscore = db.SearchText("IT", "conststring", "Slot _", 10);
        Assert.Single(underscore);
        Assert.Equal("3", underscore[0].Key);
    }

    /// <summary>Il conteggio per tabella non è il conteggio della lingua.</summary>
    /// <remarks>
    /// Serve a poter dire «<c>conststring</c> ha 7445 righe» senza confonderlo con
    /// le 58 149 dell'italiano intero: due numeri veri che descrivono cose diverse.
    /// </remarks>
    [Fact]
    public void Il_conteggio_per_tabella_e_quello_per_lingua_sono_due_numeri()
    {
        using GameReferenceDatabase db = WithText(("1", "OK"), ("2", "Va bene"));
        db.ImportText("IT", "monster", new Dictionary<string, string> { ["zts1e"] = "Volpe piccola" });

        Assert.Equal(2, db.TextCount("IT", "conststring"));
        Assert.Equal(1, db.TextCount("IT", "monster"));
        Assert.Equal(3, db.TextCount("IT"));
        Assert.Equal(0, db.TextCount("IT", "nessuna_tabella"));
    }

    /// <summary>
    /// Sul catalogo reale: le chiavi che l'import del 2026-09-08 ha portato si
    /// rileggono, e quelle che il filo nomina continuano a non esserci.
    /// </summary>
    /// <remarks>
    /// <para>
    /// I quattro valori sono quelli misurati quando <c>conststring</c> è stato
    /// importato: 10666 e 3099 esistono, 975 e 654 — gli id che <c>sayi</c> porta
    /// in <c>messaggi.noscap</c> e <c>nostale_combat.noscap</c> — no.
    /// </para>
    /// <para>
    /// <b>Le due assenze sono il punto.</b> Sono ciò che tiene aperto T-16: se un
    /// giorno qualcuno facesse indicizzare l'id del filo dalla tabella con una
    /// regola inventata, questo test lo direbbe. Quando il legame si chiuderà su
    /// una coppia osservata, si aggiorna scrivendo il valore vero — non si
    /// cancella.
    /// </para>
    /// </remarks>
    [NosAiVolumeFact]
    public void Sul_catalogo_reale_le_chiavi_note_si_rileggono_e_gli_id_del_filo_no()
    {
        GameReferenceLocation location = GameReferenceLocator.Locate();
        Assert.True(location.Exists, location.FailureReason ?? "nessun motivo riferito");
        Assert.NotNull(location.Path);

        using GameReferenceDatabase db = GameReferenceDatabase.OpenExisting(location.Path!);

        Assert.Contains("raccolto", db.TextValue("IT", "conststring", "10666") ?? "");
        Assert.Contains("raccolto", db.TextValue("IT", "conststring", "3099") ?? "");

        Assert.Null(db.TextValue("IT", "conststring", "975"));
        Assert.Null(db.TextValue("IT", "conststring", "654"));

        // E la ricerca trova le righe che parlano di raccogliere: e' il gesto che
        // il pannello fara' con la nota dell'operatore.
        IReadOnlyList<GameTextRow> found = db.SearchText("IT", "conststring", "raccolt", 20);
        Assert.NotEmpty(found);
    }
}
