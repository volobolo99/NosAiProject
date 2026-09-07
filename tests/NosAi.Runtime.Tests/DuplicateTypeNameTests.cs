using System.Text.RegularExpressions;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// R1 — un tipo, un posto. Un nome definito due volte in due namespace compila e
/// diverge in silenzio.
/// </summary>
/// <remarks>
/// <para>
/// Il danno non è estetico. Due <c>SafetyGate</c> sono due risposte a « quest'atto
/// è autorizzato » e nulla impedisce loro di divergere; è già successo, e
/// <c>docs/GATE3_PIPELINE.md</c> lo registra: la copia di Gate 6 di
/// <c>SafetyGate.ValidateToken</c> controllava la firma e <b>non</b> la scadenza,
/// mentre quella di Gate 3 controllava entrambe. Lo stesso nome, due
/// comportamenti, e nessun test se ne è accorto per mesi perché leggere un file
/// solo non dava alcun indizio che l'altro esistesse.
/// </para>
/// <para>
/// <b>Il duplicato dichiarato è la parte utile.</b> Questa prova non pretende zero
/// duplicati oggi: ne restano, e sono decisi in
/// <c>docs/PIANO_DI_RIORDINO.md § R1</c>. Ognuno sta in
/// <see cref="Declared"/> con il proprio motivo, quindi il debito è
/// <i>enumerato</i> invece che sottinteso. Un duplicato nuovo, non in lista, fa
/// fallire la prova nominandolo.
/// </para>
/// <para>
/// E la lista non diventa un tappeto: la prova fallisce anche quando una voce
/// <b>non serve più</b>, cioè quando il duplicato che descriveva è sparito. Chi
/// chiude la propagazione toglie la riga, e la lista si accorcia da sola.
/// </para>
/// </remarks>
public sealed class DuplicateTypeNameTests
{
    /// <summary>
    /// I duplicati che esistono oggi, ognuno con la decisione che lo riguarda.
    /// </summary>
    /// <remarks>
    /// La chiave è il nome semplice del tipo; il valore è il motivo per cui è
    /// ancora doppio e chi lo chiude. Aggiungere una voce qui è una decisione, non
    /// una scorciatoia: va scritta prima in <c>PIANO_DI_RIORDINO.md § R1</c>.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> Declared =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // --- tenuti doppi per decisione, non per inerzia -------------------
            // AutonomyPipeline.cs lo scrive per esteso: questi tre non sono la
            // stessa cosa sotto due nomi. Quelli di Gate 3 sono legati a un
            // effettore reale e a un'osservazione classificata, quelli di Gate 6 al
            // suo mondo simulato. Unirli etichetterebbe dati simulati come reali,
            // oppure toglierebbe a Gate 6 la certificazione che è il suo scopo.
            ["ActionExecutionVerifier"] =
                "deciso: Gate 3 verifica sul mondo reale, Gate 6 sul simulato",
            ["AuthorizedActionExecutor"] =
                "deciso: Gate 3 esegue su un effettore reale, Gate 6 sul mondo simulato",
            ["ExecutionResult"] =
                "deciso: quello di Gate 6 dichiara la propria provenienza simulata",

            // --- scoperti da questa prova, mai enumerati prima -----------------
            // Nessuno dei due è nel mandato di R1, che nomina VerificationResult,
            // TrustTier e SafetyGate. Stanno qui perché una prova che li tacesse
            // sarebbe una prova che nasconde ciò che ha trovato. Position2D è
            // chiuso da R2: tre definizioni intere sono MapPoint, la quarta è
            // TelegraphPoint.
            ["CaptureFrame"] =
                "scoperto da R1: due definizioni di un fotogramma catturato (Capture, Perception); da decidere",
            // --- visti solo da quando la scansione legge tutta la produzione ---
            // Nessuno di questi era visibile finché il controllo interrogava il
            // solo assembly NosAi.Runtime. Erano la categoria peggiore: in ognuno
            // il gemello stava in un namespace che ModuleReachability dichiara
            // irraggiungibile, quindi scrivere `using` su quello e usare il nome
            // compilava, girava e prendeva in silenzio la copia che nessuno chiama.
            //
            // Cinque sono usciti il 2026-09-07 (Q-111), rinominati e non rimossi:
            // NosAi.Core.Planning e NosAi.Core.Safety sono strati puri che
            // CLAUDE.md nomina nel proprio flusso canonico (HTN/GOAP, recovery
            // reattivo), quindi cancellarli avrebbe tolto una capacita' richiesta
            // insieme alle sue prove. GoalId, GoalStack e RankedAction di
            // NosAi.Core.Planning sono ora PlannerGoalId, PlannerGoalStack e
            // PlannerRankedAction; RecoveryState e RecoveryController di
            // NosAi.Core.Safety sono RetryBudgetState e RetryBudgetController --
            // nomi che dicono a che livello stanno, cosi' nessuno li raggiunge
            // per sbaglio credendo di avere in mano quelli autoritativi del
            // runtime. Resta aperto se quegli strati vadano poi attaccati o tolti.
            ["Goal"] =
                "vivo in entrambi: NosAi.Core.WorldModel (il contratto del World Model) e "
                + "NosAi.Runtime.Autonomy (l'obiettivo dello stack di Gate 3). Non e' un gemello morto, "
                + "e' un vero scontro di nomi fra due tipi in uso -- ha gia' prodotto un CS0104 in "
                + "GameplayObservationProjector, che lo aggira con tre alias using. Da decidere: "
                + "rinominare uno dei due, o dichiarare che gli alias sono la risposta",

            // Nota: SequenceGuard stava qui, ed e' uscito il 2026-09-07. Era il
            // caso peggiore che questa prova abbia trovato -- due politiche
            // anti-replay diverse sotto un nome, sui due capi dello stesso canale
            // Gate 1 -- e non e' stato risolto unificandole ma nominandole:
            // MonotonicSequenceGuard e SlidingWindowSequenceGuard, come R1 aveva
            // gia' fatto con i due SafetyGate. Quale politica il canale debba
            // imporre resta aperto, ed e' fissato da SequenceGuardPolicyTests
            // perche' cambiarla sia una decisione e non una sorpresa.

            // --- stesso concetto definito due volte, senza divergenza nota ----
            ["WorldState"] =
                "stesso concetto due volte: il record di NosAi.Core e quello di NosAi.Runtime.WorldModel; "
                + "da decidere quale e' canonico",
            ["MapBounds"] =
                "stesso concetto due volte: il record struct di NosAi.Core.WorldModel e quello di "
                + "NosAi.LiveIntegration; da decidere",

            // --- stesso sostantivo, concetti diversi ---------------------------
            ["NoiseHandshakeState"] =
                "atteso: in NosAi.Runtime.Security e' la macchina a stati dell'handshake (sealed class, "
                + "IDisposable), in NosAi.Security e' l'etichetta dello stato (enum : byte). Due cose "
                + "diverse che condividono un sostantivo, non una duplicazione",

            // --- doppi per la stessa ragione documentata altrove ---------------
            ["DataSourceKind"] =
                "deciso da ADR-0026: una dichiarazione per bounded context, perché NosAi.Core non ha "
                + "dipendenze e ospita più domini che non devono importarsi a vicenda",
            ["ClassifiedValue"] =
                "deciso da ADR-0026 insieme a DataSourceKind: ne è l'involucro e lo accompagna",
            ["CognitiveObservabilityRegistry"] =
                "atteso: il Control Panel è un'applicazione, e questa è la metà WPF di un bridge la cui "
                + "metà runtime porta lo stesso nome di proposito",
            ["CognitiveRuntimeTraceBridge"] = "atteso: l'altra metà dello stesso bridge",
            ["InventorySlot"] =
                "atteso: lo slot di NosAi.Economy.Inventory accanto all'ActionTarget.InventorySlot con "
                + "cui il runtime indirizza un atto — concetti diversi che condividono un sostantivo",

            // Un punto d'ingresso per eseguibile è normale; ne resta attivo uno
            // solo, fissato da StartupObject nel .csproj. Dichiarato perché la
            // prova lo vede e tacerlo insegnerebbe a ignorare l'elenco.
            ["Program"] =
                "atteso: un Main per eseguibile, uno solo attivo via StartupObject",
        };

    /// <summary>
    /// Nessun nome nuovo definito due volte, e nessuna voce dichiarata di troppo.
    /// </summary>
    [Fact]
    public void NoTypeNameIsDefinedTwiceOutsideWhatIsDeclared()
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> duplicates = FindDuplicates();

        string[] undeclared = duplicates.Keys
            .Where(name => !Declared.ContainsKey(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            undeclared.Length == 0,
            "Nomi definiti in più namespace e non dichiarati in PIANO_DI_RIORDINO.md § R1:"
            + Environment.NewLine
            + string.Join(
                Environment.NewLine,
                undeclared.Select(name => $"  {name}: {string.Join(", ", duplicates[name])}")));
    }

    /// <summary>
    /// Una voce dichiarata che non descrive più un duplicato va tolta.
    /// </summary>
    /// <remarks>
    /// È ciò che impedisce alla lista di sopravvivere al debito che elencava. Senza
    /// questa prova, la riga resterebbe lì a dire che un problema esiste molto dopo
    /// che qualcuno l'ha risolto, e la prossima persona la leggerebbe come vera.
    /// </remarks>
    [Fact]
    public void EveryDeclaredDuplicateStillExists()
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> duplicates = FindDuplicates();

        string[] stale = Declared.Keys
            .Where(name => !duplicates.ContainsKey(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            stale.Length == 0,
            "Voci dichiarate che non descrivono più un duplicato: toglierle da questo "
            + "elenco e da PIANO_DI_RIORDINO.md § R1." + Environment.NewLine
            + string.Join(Environment.NewLine, stale.Select(name => $"  {name}")));
    }

    /// <summary>
    /// La copia di <c>VerificationResult</c> di Gate 6 è sparita, e non torna.
    /// </summary>
    /// <remarks>
    /// Nominata a parte perché è la sola unificazione che R1 ha applicato sul
    /// tipo: TrustTier è stato assorbito sulla canonica Autonomy, i due
    /// SafetyGate sono stati rinominati (non unificati), questa è un fatto. Se qualcuno
    /// ridichiara il tipo dentro Gate 6, questa prova lo dice prima che le due
    /// definizioni abbiano il tempo di divergere.
    /// </remarks>
    [Fact]
    public void Gate6NoLongerDeclaresItsOwnVerificationResult()
    {
        string[] namespaces = NamespacesDeclaring("VerificationResult");

        Assert.DoesNotContain("NosAi.Runtime.Gate6", namespaces);
        Assert.Contains("NosAi.Runtime.Gate3", namespaces);
    }

    /// <summary>
    /// I nomi definiti in più di un namespace, col loro elenco di namespace.
    /// </summary>
    /// <remarks>
    /// Solo i tipi pubblici, e solo quelli dichiarati direttamente in un namespace:
    /// un tipo annidato porta il nome del suo contenitore e non può collidere con
    /// un altro namespace. I tipi generati dal compilatore sono esclusi perché
    /// nessuno li scrive e nessuno può deduplicarli.
    /// </remarks>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> FindDuplicates()
    {
        var byName = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        foreach ((string name, string ns) in RuntimeTypes())
        {
            if (!byName.TryGetValue(name, out SortedSet<string>? namespaces))
                byName[name] = namespaces = new SortedSet<string>(StringComparer.Ordinal);
            namespaces.Add(ns);
        }

        return byName
            .Where(pair => pair.Value.Count > 1)
            .ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)pair.Value.ToArray(),
                StringComparer.Ordinal);
    }

    private static string[] NamespacesDeclaring(string typeName) => RuntimeTypes()
        .Where(t => t.Name == typeName)
        .Select(t => t.Namespace)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(ns => ns, StringComparer.Ordinal)
        .ToArray();

    /// <summary>
    /// Ogni tipo pubblico dichiarato nella produzione, col proprio namespace.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Legge il sorgente, non gli assembly caricati. La versione precedente
    /// interrogava <c>typeof(DataSourceKind).Assembly</c>, cioè il solo
    /// <c>NosAi.Runtime</c>: non vedeva <c>NosAi.Core</c>, e quindi non poteva
    /// vedere i due duplicati che contavano di più: un secondo <c>GoalStack</c> in
    /// <c>NosAi.Core.Planning</c> accanto al <c>NosAi.Runtime.Autonomy.GoalStack</c>
    /// che <c>Gate3Runtime</c> compone, e un secondo <c>RecoveryController</c> in
    /// <c>NosAi.Core.Safety</c> accanto a quello che la stessa classe tiene. Quei
    /// due nomi non collidono più dal 2026-09-07 (Q-111): i gemelli di
    /// <c>NosAi.Core</c> ora si chiamano <c>PlannerGoalStack</c> e
    /// <c>RetryBudgetController</c>, ed è questa prova ad averli trovati.
    /// È lo stesso difetto che <c>ModuleReachabilityTests</c> aveva
    /// e che è stato corretto allo stesso modo: un controllo ristretto a un
    /// progetto è cieco su tutto ciò che gli sta fuori.
    /// </para>
    /// <para>
    /// La reflection non poteva bastare nemmeno allargandola: <c>NosAi.ControlPanel</c>
    /// è un'applicazione WPF che il progetto di test non referenzia, quindi il suo
    /// assembly non è caricabile qui. Il sorgente sì.
    /// </para>
    /// <para>
    /// Stesso insieme di file che legge <c>ModuleReachabilityTests</c> — tutto
    /// <c>src/</c> tranne il progetto congelato da ADR-0025 — così i due registri
    /// non possono discordare su cosa esiste.
    /// </para>
    /// </remarks>
    private static IEnumerable<(string Name, string Namespace)> RuntimeTypes()
    {
        foreach (string file in ProductionSources())
        {
            string source = File.ReadAllText(file);
            Match ns = Regex.Match(source, @"^\s*namespace\s+([\w.]+)", RegexOptions.Multiline);
            if (!ns.Success) continue;

            foreach (Match type in PublicType.Matches(source))
                yield return (type.Groups[1].Value, ns.Groups[1].Value);
        }
    }

    /// <summary>Riconosce una dichiarazione di tipo pubblico e ne cattura il nome.</summary>
    /// <remarks>
    /// <c>record struct</c> e <c>readonly record struct</c> sono riconosciuti prima
    /// di <c>record</c> nudo, altrimenti il nome catturato è la parola
    /// <c>struct</c>: la prima stesura di questa scansione riportava un tipo
    /// chiamato «struct» dichiarato in novanta file.
    /// </remarks>
    private static readonly Regex PublicType = new(
        @"^\s*public\s+(?:(?:sealed|abstract|static|partial|readonly|unsafe)\s+)*"
        + @"(?:record\s+struct|record\s+class|class|record|struct|interface|enum)\s+(\w+)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static string[] ProductionSources()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        Assert.True(directory is not null, "Radice del repository non trovata: nessun NosAi.sln sopra l'assembly di test.");

        string src = Path.Combine(directory!.FullName, "src");
        return Directory
            .EnumerateDirectories(src)
            .Where(d => Path.GetFileName(d) != "NosAi.GuardAi.App")
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();
    }
}
