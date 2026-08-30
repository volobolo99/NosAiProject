using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NosAi.Runtime.Configuration;

namespace NosAi.ControlPanel;

public partial class MainWindow : Window
{
    private readonly string _repoRoot;
    private readonly UiLogger _log = new();
    private readonly RuntimeSession _session;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(1) };
    private OperatorSettings _settings;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        _repoRoot = Directory.GetCurrentDirectory();
        _session = new RuntimeSession(_log);
        _settings = OperatorSettings.Load(_repoRoot);
        _log.Written += entry => Dispatcher.BeginInvoke(() => AppendLog(entry));
        _clock.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("HH:mm:ss");
        _poll.Tick += async (_, _) => await RefreshSnapshotAsync();
        _clock.Start();
        _poll.Start();
        LoadSettingsIntoForm();
        BuildSuiteButtons();
        RefreshSetup();
        Loaded += async (_, _) => await AutoStartAsync();
        Closed += async (_, _) => await ShutdownAsync();
    }

    private async Task AutoStartAsync()
    {
        _log.Operator($"Radice progetto: {_repoRoot}");
        RefreshSetup();
        if (!_settings.AutoStartRuntime)
        {
            Status("Auto-avvio disattivato. Premere Avvia runtime.");
            return;
        }

        if (await _session.ProbeExistingAsync(_settings.DashboardPort).ConfigureAwait(true))
        {
            _session.Attach(_settings.DashboardPort, _settings.GuardPort);
            Status("Runtime già in ascolto: collegato in sola osservazione.");
            await RefreshSnapshotAsync();
            return;
        }

        await StartRuntimeAsync();
    }

    private async Task StartRuntimeAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            Status("Avvio runtime…");
            await _session.StartHostedAsync(_settings.ToHostOptions()).ConfigureAwait(true);
            RefreshSetup();
            await RefreshSnapshotAsync();
            Status(_session.Detail ?? "Runtime avviato.");
        }
        catch (Exception ex)
        {
            _log.Error("Avvio runtime fallito.", ex);
            Status($"Avvio fallito: {ex.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    private async void OnStartRuntime(object sender, RoutedEventArgs e) => await StartRuntimeAsync();

    private async void OnStopRuntime(object sender, RoutedEventArgs e)
    {
        await _session.StopAsync();
        ApplySnapshot(SnapshotView.Empty("runtime fermato"));
        Status("Runtime fermato.");
    }

    private async void OnEmergencyStop(object sender, RoutedEventArgs e)
    {
        await _session.EmergencyStopAsync();
        await RefreshSnapshotAsync();
        Status("Arresto di emergenza richiesto. L'esecuzione resta disabilitata in Gate 1.");
    }

    private async Task RefreshSnapshotAsync()
    {
        try
        {
            var snapshot = await _session.CaptureAsync().ConfigureAwait(true);
            ApplySnapshot(snapshot);
        }
        catch (Exception ex)
        {
            _log.Error("Lettura snapshot fallita.", ex);
        }
    }

    private void ApplySnapshot(SnapshotView snapshot)
    {
        OverviewHealth.Text = snapshot.RuntimeStatus;
        OverviewClient.Text = FirstValue(snapshot.Client, "Stato");
        OverviewGuard.Text = FirstValue(snapshot.Guard, "Autenticato");
        OverviewSafety.Text = FirstValue(snapshot.Safety, "Esecuzione");
        OverviewWarning.Text = snapshot.Warning;
        ClientFields.ItemsSource = snapshot.Client;
        GuardFields.ItemsSource = snapshot.Guard;
        HardwareFields.ItemsSource = snapshot.Hardware;
        SidebarState.Text = _session.IsLive ? snapshot.RuntimeStatus.ToUpperInvariant() : "OFFLINE";
        SidebarDetail.Text = _session.Detail ?? snapshot.Warning;
        if (!string.IsNullOrWhiteSpace(snapshot.CapturedAt))
            StatusBarText.Text = $"Aggiornato {snapshot.CapturedAt} · {_session.Kind}";
    }

    private static string FirstValue(IReadOnlyList<DisplayField> fields, string label)
        => fields.FirstOrDefault(f => f.Label == label)?.Value ?? "UNKNOWN";

    private void RefreshSetup() => SetupList.ItemsSource = AutoSetup.Inspect(_repoRoot);

    private void OnNav(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        NavOverview.Style = (Style)FindResource("NavButton");
        NavClient.Style = (Style)FindResource("NavButton");
        NavPhone.Style = (Style)FindResource("NavButton");
        NavPerception.Style = (Style)FindResource("NavButton");
        NavSuites.Style = (Style)FindResource("NavButton");
        NavSettings.Style = (Style)FindResource("NavButton");
        NavLog.Style = (Style)FindResource("NavButton");
        button.Style = (Style)FindResource("NavButtonActive");

        ViewOverview.Visibility = Visibility.Collapsed;
        ViewClient.Visibility = Visibility.Collapsed;
        ViewPhone.Visibility = Visibility.Collapsed;
        ViewPerception.Visibility = Visibility.Collapsed;
        ViewSuites.Visibility = Visibility.Collapsed;
        ViewSettings.Visibility = Visibility.Collapsed;
        ViewLog.Visibility = Visibility.Collapsed;

        if (ReferenceEquals(button, NavOverview)) { ViewOverview.Visibility = Visibility.Visible; PageTitle.Text = "Panoramica"; }
        else if (ReferenceEquals(button, NavClient)) { ViewClient.Visibility = Visibility.Visible; PageTitle.Text = "Client NosTale"; }
        else if (ReferenceEquals(button, NavPhone)) { ViewPhone.Visibility = Visibility.Visible; PageTitle.Text = "Telefono Guard AI"; }
        else if (ReferenceEquals(button, NavPerception)) { ViewPerception.Visibility = Visibility.Visible; PageTitle.Text = "Percezione"; }
        else if (ReferenceEquals(button, NavSuites)) { ViewSuites.Visibility = Visibility.Visible; PageTitle.Text = "Certificazione"; }
        else if (ReferenceEquals(button, NavSettings)) { ViewSettings.Visibility = Visibility.Visible; PageTitle.Text = "Impostazioni"; }
        else { ViewLog.Visibility = Visibility.Visible; PageTitle.Text = "Diario"; }
    }

    private void LoadSettingsIntoForm()
    {
        SettingDashboardPort.Text = _settings.DashboardPort.ToString();
        SettingGuardPort.Text = _settings.GuardPort.ToString();
        SettingTimeout.Text = _settings.OperationTimeoutMs.ToString();
        SettingClientProcess.Text = _settings.ClientProcessName;
        SettingDiscovery.IsChecked = _settings.Discovery;
        SettingLoopback.IsChecked = _settings.GuardLoopbackOnly;
        SettingAutoStart.IsChecked = _settings.AutoStartRuntime;
    }

    private void OnSaveSettings(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(SettingDashboardPort.Text, out var dashboard)
            || !int.TryParse(SettingGuardPort.Text, out var guard)
            || !int.TryParse(SettingTimeout.Text, out var timeout))
        {
            Status("Porte e timeout devono essere numeri.");
            return;
        }

        _settings.DashboardPort = dashboard;
        _settings.GuardPort = guard;
        _settings.OperationTimeoutMs = timeout;
        _settings.ClientProcessName = string.IsNullOrWhiteSpace(SettingClientProcess.Text)
            ? new Gate1HostOptions().ClientProcessName
            : SettingClientProcess.Text.Trim();
        _settings.Discovery = SettingDiscovery.IsChecked == true;
        _settings.GuardLoopbackOnly = SettingLoopback.IsChecked == true;
        _settings.AutoStartRuntime = SettingAutoStart.IsChecked == true;
        try
        {
            _settings.ToHostOptions();
        }
        catch (Exception ex)
        {
            Status($"Impostazioni rifiutate: {ex.Message}");
            return;
        }

        _settings.Save(_repoRoot);
        Status("Impostazioni salvate. Si applicano al prossimo avvio del runtime.");
    }

    private void BuildSuiteButtons()
    {
        SuiteButtons.Children.Clear();
        foreach (var suite in SuiteCatalog.All)
        {
            var button = new Button
            {
                Content = suite.Title,
                ToolTip = suite.Description,
                Style = (Style)FindResource("GhostButton"),
                Margin = new Thickness(0, 0, 8, 8),
                Tag = suite
            };
            button.Click += async (_, _) => await RunSuiteAsync(suite);
            SuiteButtons.Children.Add(button);
        }

        var build = new Button
        {
            Content = "Compila runtime",
            Style = (Style)FindResource("PrimaryButton"),
            Margin = new Thickness(0, 0, 8, 8)
        };
        build.Click += async (_, _) => await RunToolAsync("dotnet", "build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release", "Compilazione runtime");
        SuiteButtons.Children.Add(build);
    }

    private async Task RunSuiteAsync(SuiteAction suite)
    {
        var dll = ResolveRuntimeDll();
        if (dll is null)
        {
            Status("Runtime non compilato. Premere Compila runtime.");
            return;
        }

        await RunToolAsync("dotnet", $"\"{dll}\" {suite.Flag}", suite.Title);
    }

    private async void OnDxgiProbe(object sender, RoutedEventArgs e)
        => await RunSuiteAsync(SuiteCatalog.All.First(s => s.Flag == "--dxgi-probe"));

    private async void OnPairPhone(object sender, RoutedEventArgs e)
        => await RunPythonAsync("-m nosai.phone.deploy", "Abbinamento telefono");

    private async void OnEnrollPhone(object sender, RoutedEventArgs e)
        => await RunPythonAsync("-m nosai.phone.enroll", "Raccolta chiave telefono");

    private async Task RunPythonAsync(string arguments, string title)
    {
        var python = ToolRunner.FindPython();
        if (python is null)
        {
            Status("Python non trovato. Installarlo e riprovare: serve per l'abbinamento del telefono.");
            return;
        }

        await RunToolAsync(python, arguments, title);
        RefreshSetup();
    }

    private async Task RunToolAsync(string fileName, string arguments, string title)
    {
        if (_busy)
        {
            Status("Un'operazione è già in corso.");
            return;
        }

        _busy = true;
        Status($"{title} in corso…");
        _log.Operator($"{title}: {fileName} {arguments}");
        try
        {
            var result = await ToolRunner.RunAsync(fileName, arguments, _repoRoot, line =>
                Dispatcher.BeginInvoke(() => _log.Operator(line))).ConfigureAwait(true);
            Status(result.ExitCode == 0 ? $"{title}: completato." : $"{title}: uscita {result.ExitCode}.");
            if (result.ExitCode != 0)
                _log.Warning($"{title} non è andato a buon fine.", new Dictionary<string, object?> { ["exit"] = result.ExitCode });
        }
        catch (Exception ex)
        {
            _log.Error($"{title} fallito.", ex);
            Status($"{title} fallito: {ex.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    private string? ResolveRuntimeDll()
    {
        var nextToPanel = Path.Combine(AppContext.BaseDirectory, "NosAi.Runtime.dll");
        if (File.Exists(nextToPanel))
            return nextToPanel;
        var release = Path.Combine(_repoRoot, "src", "NosAi.Runtime", "bin", "Release", "net8.0-windows", "NosAi.Runtime.dll");
        return File.Exists(release) ? release : null;
    }

    private void AppendLog(LogEntry entry)
    {
        LogBox.AppendText($"[{entry.At:HH:mm:ss}] {entry.Level,-5} {entry.Message}{Environment.NewLine}");
        LogScroll.ScrollToEnd();
    }

    private void OnClearLog(object sender, RoutedEventArgs e) => LogBox.Clear();

    private void Status(string text)
    {
        StatusBarText.Text = text;
        _log.Operator(text);
    }

    private async Task ShutdownAsync()
    {
        _poll.Stop();
        _clock.Stop();
        if (_session.Kind == SessionKind.Hosted)
            await _session.DisposeAsync();
    }
}
