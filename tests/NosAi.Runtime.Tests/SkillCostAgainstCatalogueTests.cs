using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.GameData;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Il catalogo del client dichiara un costo per ogni abilità, e per le due che le
/// registrazioni usano quel costo è zero — il filo lo conferma.
/// </summary>
/// <remarks>
/// <para>
/// <b>Da cosa nasce.</b> Più documenti dichiaravano AP-05 bloccato per «nessun
/// dato reale di danno/costo skill». Il dato c'è: <c>Skill.dat</c> è importato
/// dal 2026-09-07 (1958 abilità) e ogni riga porta un campo <c>COST</c>. Il
/// 2026-09-08, con il campo 5 di <c>su</c> stabilito come vnum dell'abilità, i
/// due lati si possono finalmente confrontare.
/// </para>
/// <para>
/// <b>Quello che questo test stabilisce.</b> Le sette abilità che le
/// registrazioni contengono hanno <c>COST</c> primo valore
/// 0, 0, 5, 7, 8, 15, 7 per 200, 220, 222, 223, 224, 226, 228. Le due a zero sono
/// gli attacchi base (posizione 0 della rispettiva classe), e sono le uniche usate
/// in <c>certificazione</c> (36 volte) e <c>messaggi</c> (34 volte). In quelle due
/// registrazioni l'MP non si muove **mai** dal massimo — un solo valore distinto
/// su 130 e 80 pacchetti <c>stat</c>. Settanta usi senza un punto speso: se quelle
/// due abilità costassero qualcosa si vedrebbe.
/// </para>
/// <para>
/// <b>Quello che questo test NON stabilisce, e va detto.</b> Che il primo valore
/// di <c>COST</c> sia l'MP <i>in generale</i> resta non confermato. Nelle due
/// registrazioni dove l'MP si muove, <c>stat</c> arriva troppo di rado per
/// attribuire un calo a un uso: le transizioni osservate valgono 43, 25, 22 e 30
/// punti su finestre di 90-380 pacchetti, e nel mezzo la rigenerazione risale a
/// scatti di **+24**. Un accostamento c'è — la raffica di 226 (un lancio, undici
/// colpi: è l'unica delle sette con raggio d'area) cade fra due letture che
/// distano esattamente i 15 punti che il catalogo dichiara — ma una coincidenza
/// su una finestra sporca non è una misura. Serve la registrazione descritta in
/// <c>docs/TEST_RIMANDATI.md</c> § T-17.
/// </para>
/// </remarks>
public sealed class SkillCostAgainstCatalogueTests
{
    private const string Certification = "certificazione.noscap";
    private const string Messages = "messaggi.noscap";

    /// <summary>Il vnum dell'unica abilità che ciascuna registrazione usa.</summary>
    private const int BasicAttackInCertification = 220;
    private const int BasicAttackInMessages = 200;

    /// <summary>Decoded wire lines of a recording, in the order the capture holds them.</summary>
    /// <remarks>
    /// Le stesse tre chiamate pubbliche che <c>WireInspectCommand</c> usa: il corpo
    /// dei pacchetti è codificato, e framer più decoder sono ciò che lo rende
    /// leggibile. Qui servono **in ordine e senza filtro per opcode**, che è
    /// esattamente quello che il comando non sa ancora offrire.
    /// </remarks>
    private static List<string> Lines(string path)
    {
        var lines = new List<string>();
        using IPacketSource source = CaptureFile.Open(path);
        var engine = new GameTrafficCaptureEngine(source, NosTaleWorldFramer.Factory(DataSourceKind.Cached));
        engine.FrameProduced += frame =>
        {
            if (frame.Frame.Source == DataSourceKind.Unknown)
                return;
            foreach (string line in NosTaleWorldDecoder.Decode(frame.Frame.Body.Span))
                lines.Add(line);
        };
        engine.Run();
        return lines;
    }

    private static string[] Fields(string line) =>
        line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Il catalogo dà zero agli attacchi base e un costo agli altri: due gruppi, e
    /// non un campo costante che sembrerebbe un costo senza esserlo.
    /// </summary>
    /// <remarks>
    /// Un campo che valesse zero per tutte e sette non direbbe niente, e nemmeno
    /// uno che valesse la stessa cifra per tutte. Qui i valori si dividono
    /// esattamente come la posizione nella classe suggerisce, ed è la prima
    /// ragione per credere che <c>COST</c> sia un costo.
    /// </remarks>
    [NosAiVolumeFact]
    public void Il_catalogo_da_zero_ai_due_attacchi_base_e_un_costo_agli_altri()
    {
        GameReferenceLocation location = GameReferenceLocator.Locate();
        Assert.True(location.Exists, location.FailureReason ?? "nessun motivo riferito");

        using GameReferenceDatabase catalogue = GameReferenceDatabase.OpenExisting(location.Path!);

        Assert.Equal(0, FirstCost(catalogue, BasicAttackInMessages));
        Assert.Equal(0, FirstCost(catalogue, BasicAttackInCertification));

        // Le altre cinque che le registrazioni contengono: nessuna a zero.
        foreach (int vnum in new[] { 222, 223, 224, 226, 228 })
            Assert.True(FirstCost(catalogue, vnum) > 0, $"skill {vnum} dichiara costo zero");
    }

    /// <summary>The first value of the skill's <c>COST</c> row, as the client file holds it.</summary>
    private static int FirstCost(GameReferenceDatabase catalogue, int vnum)
    {
        IReadOnlyList<NosField>? fields = catalogue.Lookup("skill", vnum);
        Assert.NotNull(fields);

        NosField cost = Assert.Single(fields!, f => f.Name == "COST");
        Assert.NotEmpty(cost.Values);
        return int.Parse(cost.Values[0], System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Nelle due registrazioni dove si usa solo un attacco base, l'MP non si muove
    /// mai — e sono settanta usi.
    /// </summary>
    /// <remarks>
    /// È il riscontro che vale, perché mette a confronto due fonti indipendenti:
    /// quello che il client tiene su disco e quello che il server manda sul filo.
    /// I numeri sono asseriti perché una registrazione che smettesse di
    /// decodificare farebbe passare a vuoto un test che conta zero usi.
    /// </remarks>
    [RecordedCaptureTheory(Certification, Messages)]
    [InlineData(Certification, BasicAttackInCertification, 36)]
    [InlineData(Messages, BasicAttackInMessages, 34)]
    public void Con_il_solo_attacco_base_lmp_non_si_muove(string recording, int skill, int expectedUses)
    {
        List<string> lines = Lines(RecordedCaptureFactAttribute.Resolve(recording)!);

        var skillsUsed = new List<int>();
        var mpSeen = new SortedSet<int>();
        var maxMpSeen = new SortedSet<int>();

        foreach (string[] f in lines.Select(Fields))
        {
            if (f.Length >= 6 && f[0] == "su" && f[1] == "1"
                && int.TryParse(f[5], out int used))
            {
                skillsUsed.Add(used);
            }
            else if (f.Length >= 5 && f[0] == "stat"
                && int.TryParse(f[3], out int mp) && int.TryParse(f[4], out int maxMp))
            {
                mpSeen.Add(mp);
                maxMpSeen.Add(maxMp);
            }
        }

        Assert.Equal(expectedUses, skillsUsed.Count);
        Assert.All(skillsUsed, used => Assert.Equal(skill, used));

        // Un solo valore di MP in tutta la registrazione, e uguale al massimo:
        // settanta usi complessivi senza un punto speso.
        int observedMp = Assert.Single(mpSeen);
        int observedMaxMp = Assert.Single(maxMpSeen);
        Assert.Equal(observedMaxMp, observedMp);
    }

    /// <summary>
    /// Dove l'MP si muove, si muove **anche all'insù**: la rigenerazione esiste, e
    /// per questo un calo osservato non è il costo di un uso.
    /// </summary>
    /// <remarks>
    /// Il test che impedisce la scorciatoia. Chiunque guardi
    /// <c>nostale_live.noscap</c> vede l'MP calare e potrebbe attribuire il calo
    /// all'abilità più vicina; questo test fissa che nella stessa registrazione
    /// l'MP risale, quindi ogni calo osservato è costo <i>meno</i> rigenerazione e
    /// nessuno dei due si legge da solo.
    /// </remarks>
    [RecordedCaptureFact("nostale_live.noscap")]
    public void Dove_lmp_si_muove_risale_anche_e_quindi_un_calo_non_e_un_costo()
    {
        List<string> lines = Lines(RecordedCaptureFactAttribute.Resolve("nostale_live.noscap")!);

        var readings = new List<int>();
        foreach (string[] f in lines.Select(Fields))
        {
            if (f.Length >= 5 && f[0] == "stat" && int.TryParse(f[3], out int mp))
                readings.Add(mp);
        }

        Assert.NotEmpty(readings);

        int rises = 0, falls = 0;
        for (int i = 1; i < readings.Count; i++)
        {
            if (readings[i] > readings[i - 1]) rises++;
            else if (readings[i] < readings[i - 1]) falls++;
        }

        Assert.True(falls > 0, "nessun calo di MP: la registrazione sbagliata");
        Assert.True(rises > 0, "nessuna risalita di MP: allora un calo sarebbe attribuibile");
    }
}
