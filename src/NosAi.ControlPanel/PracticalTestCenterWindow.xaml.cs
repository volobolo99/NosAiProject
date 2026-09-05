using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NosAi.Core.Testing;

namespace NosAi.ControlPanel;

public partial class PracticalTestCenterWindow : Window
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMilliseconds(700) };
    private readonly OperatorSettings _settings;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly List<DisplayField> _runs = new();
    private string? _activeTestId;
    private JsonElement? _lastSnapshot;
    private DateTime _lastSnapshotAtUtc;

    public PracticalTestCenterWindow(string repoRoot)
    {
        InitializeComponent();
        _settings = OperatorSettings.Load(repoRoot);
        BuildTestButtons();
        _poll.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) => { _poll.Start(); await RefreshAsync(); };
        Closed += (_, _) => _poll.Stop();
    }

    private void BuildTestButtons()
    {
        TestButtons.Children.Clear();
        foreach (var definition in PracticalTestCatalog.All)
        {
            var button = new Button
            {
                Content = $"{definition.Id} · {definition.Name}",
                Style = (Style)FindResource("GhostButton"),
                Margin = new Thickness(0, 0, 8, 8),
                Tag = definition,
                ToolTip = $"Prerequisiti: {definition.Preconditions}\nAzione: {definition.OperatorAction}\nAtteso: {definition.ExpectedObservation}"
            };
            button.Click += OnRunTest;
            TestButtons.Children.Add(button);
        }
    }

    private async void OnRunTest(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PracticalTestDefinition definition }) return;
        _activeTestId = definition.Id;
        InstructionText.Text = $"Prerequisiti: {definition.Preconditions}\nAzione: {definition.OperatorAction}\nAtteso: {definition.ExpectedObservation}";
        await RefreshAsync();
        Evaluate(definition);
    }

    private async Task RefreshAsync()
    {
        if (_settings.DashboardPort <= 0)
        {
            StatusText.Text = "Test Center: porta runtime non configurata.";
            return;
        }

        try
        {
            string json = await Http.GetStringAsync($"http://127.0.0.1:{_settings.DashboardPort}/api/gate1");
            using var document = JsonDocument.Parse(json);
            _lastSnapshot = document.RootElement.Clone();
            _lastSnapshotAtUtc = DateTime.UtcNow;
            LiveFields.ItemsSource = ReadLiveFields(document.RootElement);
            StatusText.Text = $"LIVE · target 250 ms · snapshot {DateTime.Now:HH:mm:ss.fff}";
            if (_activeTestId is { } id && PracticalTestCatalog.All.FirstOrDefault(x => x.Id == id) is { } definition)
                Evaluate(definition);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _lastSnapshot = null;
            LiveFields.ItemsSource = new[]
            {
                new DisplayField("Runtime", "UNKNOWN", "HTTP"),
                new DisplayField("Motivo", $"runtime_unreachable:{ex.GetType().Name}", "Unknown")
            };
            StatusText.Text = "OFFLINE · nessun dato live confermato";
        }
    }

    private static IReadOnlyList<DisplayField> ReadLiveFields(JsonElement root)
    {
        var fields = new List<DisplayField>();
        Add(fields, root, "runtimeStatus", "Runtime", "Local");
        Add(fields, root, "contractVersion", "Contratto", "Local");
        Add(fields, root, "capturedAtUtc", "Snapshot UTC", "Local");
        Add(fields, root, "correlationId", "Correlation ID", "Local");
        Add(fields, root, "client", "attached", "Client attached", "Local");
        Add(fields, root, "client", "processDetected", "Processo rilevato", "Local");
        Add(fields, root, "client", "windowDetected", "Finestra rilevata", "Screen");
        Add(fields, root, "client", "windowVisible", "Finestra visibile", "Screen");
        Add(fields, root, "client", "processResponding", "Processo risponde", "Local");
        Add(fields, root, "client", "networkConnected", "Network connected", "Network");
        Add(fields, root, "client", "serverEndpoint", "Endpoint osservato", "Network");
        Add(fields, root, "gameObservation", "active", "Osservazione attiva", "Network");
        Add(fields, root, "gameObservation", "packetsObserved", "Pacchetti osservati", "Network");
        Add(fields, root, "gameObservation", "packetsDecoded", "Pacchetti decodificati", "Network");
        Add(fields, root, "gameObservation", "packetsUndecodable", "Pacchetti non decodificabili", "Network");
        Add(fields, root, "gameObservation", "lastHp", "HP", "Network");
        Add(fields, root, "gameObservation", "lastMaxHp", "Max HP", "Network");
        Add(fields, root, "gameObservation", "lastMp", "MP", "Network");
        Add(fields, root, "safety", "executionMode", "Execution mode", "Local");
        Add(fields, root, "safety", "sessionActuating", "Session actuating", "Local");
        Add(fields, root, "safety", "sessionAuthorityTerminal", "Authority terminal", "Local");
        Add(fields, root, "guard", "connected", "Guard connected", "Local");
        Add(fields, root, "guard", "authenticated", "Guard authenticated", "Local");
        Add(fields, root, "resilience", "state", "Recovery state", "Local");
        Add(fields, root, "resilience", "failuresInWindow", "Recovery failures", "Local");
        Add(fields, root, "resilience", "halts", "Recovery halts", "Local");
        return fields;
    }

    private static void Add(List<DisplayField> fields, JsonElement root, string property, string label, string source)
        => fields.Add(new DisplayField(label, Render(root, property), source));

    private static void Add(List<DisplayField> fields, JsonElement root, string section, string property, string label, string source)
        => fields.Add(new DisplayField(label, Render(root, section, property), source));

    private static string Render(JsonElement root, string property)
        => root.TryGetProperty(property, out var value) ? Unwrap(value) : "UNKNOWN";

    private static string Render(JsonElement root, string section, string property)
    {
        if (!root.TryGetProperty(section, out var node) || !node.TryGetProperty(property, out var value)) return "UNKNOWN";
        return Unwrap(value);
    }

    private static string Unwrap(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("value", out var classified))
            return classified.ToString();
        return value.ToString();
    }

    private void Evaluate(PracticalTestDefinition definition)
    {
        if (_lastSnapshot is not JsonElement snapshot)
        {
            ShowResult(PracticalTestResult.Blocked, "runtime_not_live", "Nessun canonical snapshot disponibile.");
            return;
        }

        PracticalTestResult result;
        string evidence;
        string detail;
        switch (definition.Id)
        {
            case "T1":
                result = Both(snapshot, "client", "attached", "processDetected") ? PracticalTestResult.Pass : PracticalTestResult.Unknown;
                evidence = "canonical.client.attached+processDetected";
                detail = "Attach osservato dal runtime.";
                break;
            case "T2":
                result = Both(snapshot, "client", "windowDetected", "windowVisible") ? PracticalTestResult.Pass : PracticalTestResult.Unknown;
                evidence = "canonical.client.windowDetected+windowVisible";
                detail = "Questo non certifica ancora un frame WGC reale: richiede provider screen dedicato.";
                break;
            case "T3":
                result = ReadBool(snapshot, "gameObservation", "active") ? PracticalTestResult.Pass : PracticalTestResult.Unknown;
                evidence = "canonical.gameObservation.active";
                detail = "Traffico/observation channel attivo.";
                break;
            case "T4":
                result = ReadLong(snapshot, "gameObservation", "packetsDecoded") > 0 ? PracticalTestResult.Pass : PracticalTestResult.Unknown;
                evidence = "canonical.gameObservation.packetsDecoded";
                detail = "Decodifica osservata; non equivale ancora a WorldState completo.";
                break;
            case "T5":
                result = HasAny(snapshot, "mapWorld", "entities") ? PracticalTestResult.Pass : PracticalTestResult.Blocked;
                evidence = "canonical.map/entities";
                detail = result == PracticalTestResult.Pass ? "Superficie spaziale disponibile." : "Nessun contratto live sufficiente per certificare navigation.";
                break;
            case "T6":
                result = ReadBool(snapshot, "safety", "sessionActuating") && ReadBool(snapshot, "guard", "authenticated") ? PracticalTestResult.Unknown : PracticalTestResult.Blocked;
                evidence = "canonical.guard+safety";
                detail = "La presenza di Guard non prova da sola una decisione di combat verificata.";
                break;
            case "T7":
                result = PracticalTestResult.Blocked;
                evidence = "quest_state_not_published";
                detail = "Serve un contratto quest/interaction osservabile prima di certificare il test.";
                break;
            case "T8":
                result = PracticalTestResult.Blocked;
                evidence = "character_inventory_state_not_published";
                detail = "Serve un contratto character/inventory osservabile.";
                break;
            case "T9":
                result = ReadBool(snapshot, "guard", "authenticated") && ReadBool(snapshot, "safety", "requireGuardApproval") ? PracticalTestResult.Unknown : PracticalTestResult.Blocked;
                evidence = "canonical.guard+safety";
                detail = "Precondizioni presenti, ma la catena completa richiede execution/verification evidence.";
                break;
            case "T10":
                result = HasProperty(snapshot, "resilience", "state") ? PracticalTestResult.Pass : PracticalTestResult.Unknown;
                evidence = "canonical.resilience";
                detail = "Recovery state esposto; la perturbazione fisica resta da eseguire.";
                break;
            case "T11":
                result = HasProperty(snapshot, "hardware", "logicalCores") && HasProperty(snapshot, "hardware", "systemRamMb") ? PracticalTestResult.Pass : PracticalTestResult.Unknown;
                evidence = "canonical.hardware";
                detail = "Profilo hardware letto dal runtime.";
                break;
            case "T12":
                result = HasProperty(snapshot, "safety", "executionMode") && HasProperty(snapshot, "safety", "requireGuardApproval") ? PracticalTestResult.Pass : PracticalTestResult.Unknown;
                evidence = "canonical.safety";
                detail = "Policy safety presente nel canonical snapshot.";
                break;
            case "T13":
                result = HasProperty(snapshot, "guard", "connected") && HasProperty(snapshot, "guard", "authenticated") ? PracticalTestResult.Pass : PracticalTestResult.Unknown;
                evidence = "canonical.guard";
                detail = "Stato Guard osservato; autenticazione reale deve essere presente per certificazione finale.";
                break;
            case "T14":
                result = ReadRootString(snapshot, "runtimeStatus") is { Length: > 0 } status && status != "Failed" ? PracticalTestResult.Pass : PracticalTestResult.Unknown;
                evidence = "canonical.runtimeStatus";
                detail = "Runtime status osservato.";
                break;
            case "T15":
                long ageMs = (long)(DateTime.UtcNow - _lastSnapshotAtUtc).TotalMilliseconds;
                result = ageMs <= 1000 && HasProperty(snapshot, "capturedAtUtc") && HasProperty(snapshot, "correlationId") ? PracticalTestResult.Pass : PracticalTestResult.Unknown;
                evidence = "canonical.capturedAtUtc+correlationId";
                detail = $"Snapshot age lato Dashboard={Math.Max(0, ageMs)} ms.";
                break;
            case "T16":
                result = HasProperty(snapshot, "client", "attached") && HasProperty(snapshot, "gameObservation", "active") ? PracticalTestResult.Pass : PracticalTestResult.Unknown;
                evidence = "canonical.classified-values";
                detail = "Le superfici usate dal Test Center sono classificate; UNKNOWN non viene convertito in zero.";
                break;
            case "T17":
                result = HasProperty(snapshot, "correlationId") ? PracticalTestResult.Unknown : PracticalTestResult.Blocked;
                evidence = "event-log-endpoint-required";
                detail = "Il canonical snapshot non basta per certificare integrità/gap del journal: serve endpoint event-log.";
                break;
            case "T18":
                result = ReadRootString(snapshot, "runtimeStatus") is "Healthy" or "Degraded" ? PracticalTestResult.Unknown : PracticalTestResult.Blocked;
                evidence = "canonical.runtimeStatus";
                detail = "Reconnect/recovery richiede una perturbazione controllata e verifica before/after.";
                break;
            case "T19":
                result = HasProperty(snapshot, "safety", "sessionAuthorityTerminal") && HasProperty(snapshot, "safety", "sessionAuthorityReason") ? PracticalTestResult.Pass : PracticalTestResult.Unknown;
                evidence = "canonical.safety.sessionAuthority";
                detail = "Il pannello osserva l'autorità; non può autorizzare autonomamente l'esecuzione.";
                break;
            case "T20":
                result = PracticalTestResult.Blocked;
                evidence = "certification_requires_physical_e2e";
                detail = "Certificazione finale bloccata finché le evidenze fisiche T1-T19 non sono disponibili nel server privato.";
                break;
            default:
                result = PracticalTestResult.Blocked;
                evidence = "unsupported_test";
                detail = "Test non riconosciuto.";
                break;
        }

        ShowResult(result, evidence, detail);
        AppendRun(definition.Id, result, evidence);
    }

    private static bool Both(JsonElement root, string section, string a, string b) => ReadBool(root, section, a) && ReadBool(root, section, b);

    private static bool ReadBool(JsonElement root, string section, string property)
        => bool.TryParse(ReadClassified(root, section, property), out var value) && value;

    private static long ReadLong(JsonElement root, string section, string property)
        => long.TryParse(ReadClassified(root, section, property), out var value) ? value : 0;

    private static string? ReadClassified(JsonElement root, string section, string property)
    {
        if (!root.TryGetProperty(section, out var node) || !node.TryGetProperty(property, out var value)) return null;
        return Unwrap(value);
    }

    private static bool HasProperty(JsonElement root, string section, string property)
        => root.TryGetProperty(section, out var node) && node.TryGetProperty(property, out _);

    private static bool HasAny(JsonElement root, params string[] properties)
        => properties.Any(root.TryGetProperty);

    private static string? ReadRootString(JsonElement root, string property)
        => root.TryGetProperty(property, out var value) ? value.ToString() : null;

    private void ShowResult(PracticalTestResult result, string evidence, string detail)
    {
        ResultText.Text = result.ToString().ToUpperInvariant();
        EvidenceText.Text = $"{evidence} · {detail}";
    }

    private void AppendRun(string testId, PracticalTestResult result, string evidence)
    {
        _runs.Insert(0, new DisplayField(testId, result.ToString().ToUpperInvariant(), evidence));
        if (_runs.Count > 20) _runs.RemoveAt(_runs.Count - 1);
        RunLog.ItemsSource = null;
        RunLog.ItemsSource = _runs.ToArray();
    }
}
