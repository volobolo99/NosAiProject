using System.Text.RegularExpressions;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Il registro dei rifiuti che nessun test verifica. Scorre il sorgente di
/// <c>src/NosAi.Runtime/</c>, raccoglie ogni <c>public const string …Reason</c> e
/// verifica che ognuna sia coperta da un test — per nome della costante o per valore
/// della stringa. Un rifiuto senza prova è una promessa.
/// </summary>
/// <remarks>
/// <para>
/// Il valore è la chiave perché è ciò che arriva all'operatore: è il nome sotto il
/// quale il runtime rifiuta, e due dichiarazioni con lo stesso valore sono lo stesso
/// rifiuto due volte.
/// </para>
/// <para>
/// Ogni voce di <see cref="Declared"/> porta il perché non è coperta. Una voce senza
/// motivo non è dichiarata: è nascosta. E l'elenco non è un tappeto: la prova fallisce
/// anche quando una voce dichiarata è nel frattempo diventata coperta, così la lista
/// si accorcia da sola.
/// </para>
/// <para>
/// Il file stesso è escluso dalla scansione di copertura: i suoi valori dichiarati
/// non sono copertura, sono il debito enumerato.
/// </para>
/// </remarks>
public sealed class RefusalReasonRegisterTests
{
    /// <summary>
    /// I rifiuti non coperti oggi, ognuno con il motivo per cui non lo sono.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Declared =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // --- mai prodotti: la costante esiste ma nessun percorso la emette -----
            ["no_active_goal"] =
                "mai prodotto: GoalStack dichiara la costante ma nessun percorso la emette",
            ["not_named_by_active_goal"] =
                "mai prodotto: GoalStack dichiara la costante ma nessun percorso la emette",
            ["step_session_authority_unknown"] =
                "mai prodotto: StepGuardChain.CheckAuthority restituisce direttamente il delegato, la costante non è usata",
            ["no_target_selected"] =
                "mai prodotto come costante: il letterale vive solo in NetworkWorldFeed, la costante non è usata",
            ["unequip_slot_not_resolved"] =
                "mai prodotto: ramo difensivo in UnequipExecutor.Unequip; Confirmed/Load esigono una calibrazione con ogni slot dichiarato e Resolve non può mancare lo slot richiesto (i casi non calibrati sono già rifiutati prima con NotCalibratedReason)",

            // --- irraggiungibili: il ramo vive nel RunWindows privato e la --------
            // --- composizione restituisce sempre un backend gated (ADR-0003) -------
            ["unequip_input_backend_not_gated"] =
                "irraggiungibile: ramo privato RunWindows, CreateSafe restituisce sempre un backend gated",
            ["walk_input_backend_not_gated"] =
                "irraggiungibile: ramo privato RunWindows, CreateSafe restituisce sempre un backend gated",
            ["scout_input_backend_not_gated"] =
                "irraggiungibile: ramo privato RunWindows, CreateSafe restituisce sempre un backend gated",
            ["collect_input_backend_not_gated"] =
                "irraggiungibile: ramo privato RunWindows, CreateSafe restituisce sempre un backend gated",
            ["autoplay_input_backend_not_gated"] =
                "irraggiungibile: ramo privato RunWindows, CreateSafe restituisce sempre un backend gated",
            ["step_input_backend_not_gated"] =
                "irraggiungibile: ramo privato RunWindows, CreateSafe restituisce sempre un backend gated",
            ["engage_input_backend_not_gated"] =
                "irraggiungibile: ramo privato RunWindows, CreateSafe restituisce sempre un backend gated",
            ["recover_input_backend_not_gated"] =
                "irraggiungibile: ramo privato RunWindows, CreateSafe restituisce sempre un backend gated",
            ["click_target_input_backend_not_gated"] =
                "irraggiungibile: ramo privato RunWindows, CreateSafe restituisce sempre un backend gated",
            ["unequip_input_backend_not_gated"] =
                "irraggiungibile: ramo privato RunWindows, CreateSafe restituisce sempre un backend gated",

            // --- richiedono un token Windows reale, non producibile in modo --------
            // --- deterministico da un unit test -----------------------------------
            ["open_process_token_failed"] =
                "richiede l'apertura fallita del token di un processo Windows reale",
            ["token_integrity_unreadable"] =
                "richiede una lettura fallita del label dal token Windows reale",
            ["token_integrity_malformed"] =
                "richiede un SID malformato nel token Windows reale",

            // --- richiedono un client reale, la sua memoria, il filo o il volume ----
            ["client_not_readable"] = "richiede un processo client reale da cui leggere",
            ["keybinds_unavailable"] = "richiede i keybind reali del client",
            ["click_target_entity_not_found"] = "richiede un'entita' reale osservata sul filo (ramo RunWindows)",
            ["click_target_vnum_not_observed"] = "richiede un vnum reale osservato sul filo (ramo RunWindows)",
            ["unequip_equip_feed_unavailable"] =
                "richiede un client reale la cui osservazione di rete non rilevi un'unica connessione TCP di gioco, o la cui sorgente WinDivert non si apra (ramo privato RunWindows)",
            ["click_target_player_position_unreadable"] = "richiede la lettura fallita della posizione dalla memoria del client",
            ["map_id_not_read"] = "richiede la lettura fallita del map_id dalla memoria del client",
            ["map_id_not_on_wire"] = "richiede una cattura reale priva di map_id sul filo",
            ["standing_cell_not_read"] = "richiede la lettura fallita della cella dalla memoria del client",
            ["standing_cell_not_on_wire"] = "richiede una cattura reale priva della cella di posizione",
            ["maps_directory_not_available"] = "richiede la directory mappe assente",
            ["map_grid_not_available"] = "richiede la griglia mappa non disponibile",
            ["map_not_persisted"] = "richiede una mappa non persistita",
            ["target_never_established"] = "richiede un target mai stabilito",
            ["target_entity_id_not_established"] = "richiede un'ipotesi di target non confermata dalla memoria",
            ["anchor_target_is_null"] = "richiede un anchor con target nullo nella memoria",
            ["wire_target_not_observed"] = "richiede un target non osservato sul filo",
            ["wire_unreachable_no_second_source"] = "richiede il filo irraggiungibile senza seconda sorgente",
            ["sweep_wire_unreachable_no_second_source"] = "richiede il filo irraggiungibile nella sweep cooldown",
            ["equipment_never_read:no_observation_channel"] = "richiede un canale di osservazione equipaggiamento non presente",
            ["skill_list_never_read:no_observation_channel"] = "richiede un canale di osservazione skill non presente",
            ["player_vitals_max_zero"] = "richiede vitali reali con massimo a zero",
            ["calibrate_wire_had_no_stat"] = "richiede una calibrazione su memoria senza statistica",
            ["calibrate_hp_and_mp_are_one_address"] = "richiede HP e MP allo stesso indirizzo nella calibrazione reale",
            ["virtual_desktop_metrics_unreadable"] = "richiede la lettura fallita delle metriche del desktop virtuale",
            ["visual_source_threw"] = "richiede una sorgente visuale che lancia un'eccezione",
            ["geometry_stamp_stale"] = "richiede uno stamp di geometria scaduto contro l'orologio reale",
            ["walk_not_active"] = "richiede un controller di camminata non attivo",
            ["actuation_refused_no_decision"] = "richiede una politica di decisione senza decisione",
            ["sweep_no_candidate_is_reachable_from_a_base"] = "richiede una sweep senza candidati raggiungibili",
            ["sweep_every_candidate_reacts_to_any_skill"] = "richiede una sweep in cui ogni candidato reagisce a ogni skill",
            ["unequip_equip_feed_unavailable"] = "richiede il processo client reale e il filo aperto: ramo RunWindows, connessione di gioco o WinDivert non disponibili",

            // --- comandi diagnostici: stato del ledger o della registrazione -------
            ["invalid_max"] = "richiede un argomento --max fuori range nel comando wire-inspect",
            ["missing_path"] = "richiede un percorso di registrazione assente",
            ["context_not_found"] = "richiede un contesto ledger non presente",
            ["outcome_report_ledger_unavailable"] = "richiede il ledger reale su SQLite",

            // --- ciclo autoplay ----------------------------------------------------
            ["autoplay_cycles_exceeds_max"] = "richiede un ciclo autoplay oltre il massimo configurato",
        };

    /// <summary>
    /// Nessun rifiuto non coperto fuori da ciò che è dichiarato.
    /// </summary>
    [Fact]
    public void NoRefusalReasonIsUncoveredOutsideWhatIsDeclared()
    {
        IReadOnlySet<string> uncovered = UncoveredValues();

        string[] undeclared = uncovered
            .Where(value => !Declared.ContainsKey(value))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            undeclared.Length == 0,
            "Rifiuti non coperti da alcun test e non dichiarati nel registro:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, undeclared));
    }

    /// <summary>
    /// Una voce dichiarata che nel frattempo è coperta va tolta.
    /// </summary>
    [Fact]
    public void EveryDeclaredRefusalReasonIsStillUncovered()
    {
        IReadOnlySet<string> uncovered = UncoveredValues();

        string[] stale = Declared.Keys
            .Where(value => !uncovered.Contains(value))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            stale.Length == 0,
            "Rifiuti dichiarati che ora sono coperti da un test: toglierli dal registro."
            + Environment.NewLine
            + string.Join(Environment.NewLine, stale));
    }

    /// <summary>
    /// I valori dei rifiuti che nessun test tocca, per nome della costante o per
    /// valore della stringa.
    /// </summary>
    private static IReadOnlySet<string> UncoveredValues()
    {
        string[] testTexts = TestSources()
            .Select(File.ReadAllText)
            .ToArray();

        var uncovered = new HashSet<string>(StringComparer.Ordinal);
        var namePatterns = new Dictionary<string, Regex>(StringComparer.Ordinal);

        foreach ((string name, string value, string _) in RefusalConstants())
        {
            bool covered = testTexts.Any(t => t.IndexOf(value, StringComparison.Ordinal) >= 0);
            if (!covered)
            {
                if (!namePatterns.TryGetValue(name, out Regex? pattern))
                    namePatterns[name] = pattern = new Regex(@"\b" + Regex.Escape(name) + @"\b", RegexOptions.Compiled);
                covered = testTexts.Any(t => pattern.IsMatch(t));
            }

            if (!covered)
                uncovered.Add(value);
        }

        return uncovered;
    }

    /// <summary>Riconosce una dichiarazione di rifiuto e ne cattura nome e valore.</summary>
    private static readonly Regex RefusalConstant = new(
        @"public\s+const\s+string\s+(?<name>\w*Reason)\s*=\s*""(?<value>[^""]*)""",
        RegexOptions.Compiled);

    /// <summary>
    /// Ogni costante di rifiuto dichiarata nella produzione, con nome, valore e file.
    /// </summary>
    private static IEnumerable<(string Name, string Value, string File)> RefusalConstants()
    {
        foreach (string file in RuntimeSources())
        {
            string source = File.ReadAllText(file);
            foreach (Match match in RefusalConstant.Matches(source))
                yield return (match.Groups["name"].Value, match.Groups["value"].Value, file);
        }
    }

    /// <summary>I sorgenti del runtime: la sola origine dei rifiuti.</summary>
    private static string[] RuntimeSources()
    {
        string runtime = Path.Combine(RepositoryRoot(), "src", "NosAi.Runtime");
        return Directory
            .EnumerateFiles(runtime, "*.cs", SearchOption.AllDirectories)
            .Where(IsSource)
            .ToArray();
    }

    /// <summary>I sorgenti dei test, escluso questo stesso file.</summary>
    private static string[] TestSources()
    {
        string self = nameof(RefusalReasonRegisterTests) + ".cs";
        return Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot(), "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(IsSource)
            .Where(f => Path.GetFileName(f) != self)
            .ToArray();
    }

    private static bool IsSource(string file) =>
        !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        Assert.True(directory is not null, "Radice del repository non trovata: nessun NosAi.sln sopra l'assembly di test.");
        return directory!.FullName;
    }
}
