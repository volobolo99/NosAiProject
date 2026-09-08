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
        AcknowledgeButton.IsEnabled = true;
        await RefreshAsync();
        Evaluate(definition);
    }

    /// <summary>
    /// The operator confirms they performed <see cref="PracticalTestDefinition.OperatorAction"/>
    /// in the client. Re-evaluates the active test against the latest snapshot
    /// immediately, rather than waiting for the next 250 ms poll tick.
    /// </summary>
    private void OnAcknowledge(object sender, RoutedEventArgs e)
    {
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
            _lastSnapshotAtUtc = DateTime.UtcNow;
            LiveFields.ItemsSource = PracticalTestCenter.ReadLiveFields(document.RootElement);
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

    private void Evaluate(PracticalTestDefinition definition)
    {
        if (_lastSnapshot is not JsonElement snapshot)
        {
            ShowResult(PracticalTestResult.Blocked, "runtime_not_live", "Nessun canonical snapshot disponibile.");
            return;
        }

        PracticalTestVerdict verdict = PracticalTestCenter.Evaluate(definition, snapshot, DateTime.UtcNow, _lastSnapshotAtUtc);
        ShowResult(verdict.Result, verdict.Evidence, verdict.Detail);
        AppendRun(definition.Id, verdict.Result, verdict.Evidence);
    }

    private void ShowResult(PracticalTestResult result, string evidence, string detail)
    {
        ResultText.Text = result.ToString().ToUpperInvariant();
        EvidenceText.Text = $"{evidence} · {detail}";
    }

    private void AppendRun(string testId, PracticalTestResult result, string evidence)
    {
        _runs.Insert(0, new DisplayField(testId, result.ToString().ToUpperInvariant(), evidence));
        if (_runs.Count > 20) _runs.RemoveAt(20);
        RunLog.ItemsSource = null;
        RunLog.ItemsSource = _runs.ToArray();
    }
}
