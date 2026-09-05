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
    private bool _operatorAcknowledged;
    private JsonElement? _lastSnapshot;

    public PracticalTestCenterWindow(string repoRoot)
    {
        InitializeComponent();
        _settings = OperatorSettings.Load(repoRoot);
        BuildTestButtons();
        _poll.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) =>
        {
            _poll.Start();
            await RefreshAsync();
        };
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
                Style = (Style)FindResource(definition.Id is "T1" or "T2" or "T3" or "T4" ? "GhostButton" : "GhostButton"),
                Margin = new Thickness(0, 0, 8, 8),
                Tag = definition
            };
            button.Click += OnRunTest;
            TestButtons.Children.Add(button);
        }
    }

    private async void OnRunTest(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PracticalTestDefinition definition }) return;

        _activeTestId = definition.Id;
        _operatorAcknowledged = false;
        InstructionText.Text = definition.OperatorAction;
        AcknowledgeButton.IsEnabled = !string.IsNullOrWhiteSpace(definition.OperatorAction)
            && !definition.OperatorAction.StartsWith("Nessuna", StringComparison.OrdinalIgnoreCase);

        if (definition.Id is not ("T1" or "T2" or "T3" or "T4"))
        {
            ShowResult(PracticalTestResult.Blocked, "test_not_yet_implemented", "La verifica live concreta di questa capacità non è ancora implementata.");
            AppendRun(definition.Id, PracticalTestResult.Blocked, "test_not_yet_implemented");
            return;
        }

        await RefreshAsync();
        Evaluate(definition);
    }

    private void OnAcknowledge(object sender, RoutedEventArgs e)
    {
        _operatorAcknowledged = true;
        AcknowledgeButton.IsEnabled = false;
        InstructionText.Text = "Azione operatore confermata. Ora osserviamo il risultato dal runtime.";
        if (_activeTestId is { } id && PracticalTestCatalog.All.FirstOrDefault(x => x.Id == id) is { } definition)
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
            var fields = ReadLiveFields(document.RootElement);
            LiveFields.ItemsSource = fields;
            StatusText.Text = $"LIVE · refresh 250 ms target · ultimo snapshot {DateTime.Now:HH:mm:ss.fff}";
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
        Add(fields, root, "client", "attached", "Client attached", "Network/Memory/Screen");
        Add(fields, root, "client", "processDetected", "Processo rilevato", "Local");
        Add(fields, root, "client", "windowDetected", "Finestra rilevata", "Screen");
        Add(fields, root, "client", "windowVisible", "Finestra visibile", "Screen");
        Add(fields, root, "gameObservation", "active", "Osservazione attiva", "Network");
        Add(fields, root, "gameObservation", "endpoint", "Endpoint osservato", "Network");
        Add(fields, root, "gameObservation", "packetsObserved", "Pacchetti osservati", "Network");
        Add(fields, root, "gameObservation", "packetsDecoded", "Pacchetti decodificati", "Network");
        Add(fields, root, "gameObservation", "packetsUndecodable", "Pacchetti non decodificabili", "Network");
        Add(fields, root, "gameObservation", "lastHp", "HP", "Network");
        Add(fields, root, "gameObservation", "lastMaxHp", "Max HP", "Network");
        Add(fields, root, "gameObservation", "lastMp", "MP", "Network");
        return fields;
    }

    private static void Add(List<DisplayField> fields, JsonElement root, string section, string property, string label, string source)
    {
        if (!root.TryGetProperty(section, out var node) || !node.TryGetProperty(property, out var value))
        {
            fields.Add(new DisplayField(label, "UNKNOWN", "Unknown"));
            return;
        }

        string rendered = value.ValueKind switch
        {
            JsonValueKind.Object when value.TryGetProperty("value", out var classified) => classified.ToString(),
            JsonValueKind.String => value.GetString() ?? "",
            _ => value.ToString()
        };
        fields.Add(new DisplayField(label, rendered, source));
    }

    private void Evaluate(PracticalTestDefinition definition)
    {
        if (_lastSnapshot is not JsonElement snapshot)
        {
            ShowResult(PracticalTestResult.Blocked, "runtime_not_live", "Nessun canonical snapshot disponibile.");
            return;
        }

        bool attached = ReadClassifiedBool(snapshot, "client", "attached");
        bool process = ReadClassifiedBool(snapshot, "client", "processDetected");
        bool window = ReadClassifiedBool(snapshot, "client", "windowDetected");
        bool visible = ReadClassifiedBool(snapshot, "client", "windowVisible");
        bool observationActive = ReadClassifiedBool(snapshot, "gameObservation", "active");
        long decoded = ReadClassifiedLong(snapshot, "gameObservation", "packetsDecoded");

        PracticalTestResult result;
        string evidence;
        switch (definition.Id)
        {
            case "T1":
                result = attached && process ? PracticalTestResult.Pass : PracticalTestResult.Unknown;
                evidence = "canonical.client.attached+processDetected";
                break;
            case "T2":
                result = window && visible ? PracticalTestResult.Pass : PracticalTestResult.Unknown;
                evidence = "canonical.client.windowDetected+windowVisible";
                break;
            case "T3":
                result = observationActive ? PracticalTestResult.Pass : PracticalTestResult.Unknown;
                evidence = "canonical.gameObservation.active";
                break;
            case "T4":
                result = decoded > 0 ? PracticalTestResult.Pass : PracticalTestResult.Unknown;
                evidence = "canonical.gameObservation.packetsDecoded";
                break;
            default:
                result = PracticalTestResult.Blocked;
                evidence = "not_implemented";
                break;
        }

        ShowResult(result, evidence, result == PracticalTestResult.Pass ? "Evidenza osservata dal runtime." : "Il runtime non ha fornito evidenza sufficiente; nessuna inferenza privilegiata.");
        AppendRun(definition.Id, result, evidence);
    }

    private static bool ReadClassifiedBool(JsonElement root, string section, string property)
        => ReadClassified(root, section, property) is { } value && bool.TryParse(value, out var result) && result;

    private static long ReadClassifiedLong(JsonElement root, string section, string property)
        => long.TryParse(ReadClassified(root, section, property), out var result) ? result : 0;

    private static string? ReadClassified(JsonElement root, string section, string property)
    {
        if (!root.TryGetProperty(section, out var node) || !node.TryGetProperty(property, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("value", out var classified))
            return classified.ToString();
        return value.ToString();
    }

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
