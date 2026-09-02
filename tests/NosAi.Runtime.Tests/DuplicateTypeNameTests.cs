using System.Reflection;
using NosAi.Runtime.Contracts;
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
            // R1: NON un duplicato. Contracts.VerificationResult(bool, string) è
            // l'esito del verificatore della pipeline agente (IAgentVerifier), che
            // non ha implementazioni e il cui unico consumatore non viene mai
            // costruito. Gate3.VerificationResult è l'esito del ciclo Gate 3, a
            // quattro esiti e con provenienza. La copia di Gate 6 è stata unificata
            // su quella di Gate 3.
            ["VerificationResult"] =
                "R1: canonica NosAi.Runtime.Gate3.VerificationResult; quella di Contracts appartiene alla pipeline agente da rinominare o cancellare",

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
            // Nessuno dei tre è nel mandato di R1, che nomina VerificationResult,
            // TrustTier e SafetyGate. Stanno qui perché una prova che li tacesse
            // sarebbe una prova che nasconde ciò che ha trovato.
            ["Position2D"] =
                "scoperto da R1: quattro definizioni di un punto sul piano (Events, Raids, Gate2, Gate6); da decidere",
            ["CaptureFrame"] =
                "scoperto da R1: due definizioni di un fotogramma catturato (Capture, Perception); da decidere",
            ["ScreenPoint"] =
                "scoperto da R1: due definizioni di un punto sullo schermo (Raids, Humanizer); da decidere",

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

        foreach (Type type in RuntimeTypes())
        {
            string ns = type.Namespace ?? "<globale>";
            if (!byName.TryGetValue(type.Name, out SortedSet<string>? namespaces))
                byName[type.Name] = namespaces = new SortedSet<string>(StringComparer.Ordinal);
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
        .Select(t => t.Namespace ?? "<globale>")
        .Distinct(StringComparer.Ordinal)
        .OrderBy(ns => ns, StringComparer.Ordinal)
        .ToArray();

    private static IEnumerable<Type> RuntimeTypes() =>
        typeof(DataSourceKind).Assembly
            .GetTypes()
            .Where(t => t.IsPublic && !t.IsNested && !IsCompilerGenerated(t));

    private static bool IsCompilerGenerated(Type type) =>
        type.Name.Contains('<', StringComparison.Ordinal)
        || type.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is not null;
}
