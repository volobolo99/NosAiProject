using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
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
    private DateTime _lastListenProbeUtc = DateTime.MinValue;
    private bool? _apiListening;
    private bool? _guardListening;
    private SnapshotView _lastSnapshot = SnapshotView.Empty("avvio");

    public MainWindow()
    {
        InitializeComponent();
        _repoRoot = Directory.GetCurrentDirectory();
        _session = new RuntimeSession(_log);
        _settings = OperatorSettings.Load(_repoRoot);
        _log.Written += entry =>
        {
            OperatorLogFile.Append(_repoRoot, entry);
            Dispatcher.BeginInvoke(() => AppendLog(entry));
        };
        _clock.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("HH:mm:ss");
        _poll.Tick += async (_, _) => await RefreshSnapshotAsync();
        _clock.Start();
        _poll.Start();
        LoadSettingsIntoForm();
        BuildSuiteButtons();
        RefreshSetup();
        ApplyMode();
        OverviewWire.Text = ChannelView.WireLabel;
        OverviewPhoneReminder.Text = ChannelView.PhoneReminder;
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
            Status("COLLEGATO: runtime già in ascolto. Questa console osserva; Ferma scollega e non lo spegne.");
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
            Status(_session.Detail is { Length: > 0 }
                ? $"OSPITATO: {_session.Detail}"
                : "OSPITATO: questo processo è il runtime.");
        }
        catch (Exception ex)
        {
            _log.Error("Avvio runtime fallito.", ex);
            _session.NoteFailure(ex.Message);
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
        var wasAttached = _session.Kind == SessionKind.Attached;
        await _session.StopAsync();
        ApplySnapshot(SnapshotView.Empty(wasAttached ? "scollegato" : "runtime fermato"));
        Status(wasAttached
            ? "Scollegato. Il runtime esistente è ancora in ascolto; questa console non lo ha spento."
            : "Runtime ospitato fermato.");
    }

    private async void OnReconnect(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_session.Kind == SessionKind.Hosted)
        {
            Status("Questo processo è già il runtime. Ricollega serve per un runtime esterno.");
            return;
        }

        _busy = true;
        try
        {
            Status("Ricerca runtime in ascolto…");
            if (await _session.ProbeExistingAsync(_settings.DashboardPort).ConfigureAwait(true))
            {
                _session.Attach(_settings.DashboardPort, _settings.GuardPort);
                await RefreshSnapshotAsync();
                Status("Ricollegato. Questa console osserva; Scollega non spegne l'altro processo.");
                return;
            }

            _session.NoteFailure($"nessun runtime su 127.0.0.1:{_settings.DashboardPort}");
            ApplySnapshot(SnapshotView.Empty(_session.LastFailure ?? "offline"));
            Status("Nessun runtime in ascolto. Premere Avvia per ospitarlo in questo processo.");
        }
        finally
        {
            _busy = false;
        }
    }

    private void OnOpenExeFolder(object sender, RoutedEventArgs e)
    {
        var folder = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{folder}\"",
            UseShellExecute = true
        });
        Status($"Cartella exe: {folder}");
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
            await RefreshListenAsync();
            ApplySnapshot(snapshot);
        }
        catch (Exception ex)
        {
            _log.Error("Lettura snapshot fallita.", ex);
        }
    }

    private async Task RefreshListenAsync()
    {
        var networkVisible = ViewNetwork.Visibility == Visibility.Visible;
        if (!networkVisible && DateTime.UtcNow - _lastListenProbeUtc < TimeSpan.FromSeconds(5))
            return;

        var dashboard = _settings.DashboardPort;
        var guard = _settings.GuardPort;
        var result = await Task.Run(() => (LocalPortProbe.CanConnect(dashboard), LocalPortProbe.CanConnect(guard)))
            .ConfigureAwait(true);
        _apiListening = result.Item1;
        _guardListening = result.Item2;
        _lastListenProbeUtc = DateTime.UtcNow;
    }

    private void ApplySnapshot(SnapshotView snapshot)
    {
        OverviewHealth.Text = snapshot.RuntimeStatus;
        OverviewClient.Text = FirstValue(snapshot.Client, "Stato");
        OverviewGuard.Text = FirstValue(snapshot.Guard, "Autenticato");
        OverviewSafety.Text = FirstValue(snapshot.Safety, "Esecuzione");
        OverviewWire.Text = snapshot.WireLabel;
        OverviewSlot.Text = snapshot.SlotLabel;
        OverviewSlotHint.Text = snapshot.SlotHint;
        OverviewPhoneReminder.Text = snapshot.PhoneReminder;
        OverviewRecovery.Text = string.IsNullOrWhiteSpace(_session.LastFailure)
            ? ""
            : $"Ultimo errore: {_session.LastFailure}. Ricollega se un runtime è già in ascolto, Avvia per ospitarlo.";
        OverviewWarning.Text = snapshot.Warning;
        ClientFields.ItemsSource = snapshot.Client;
        GuardFields.ItemsSource = snapshot.Guard;
        HardwareFields.ItemsSource = snapshot.Hardware;
        SecurityFields.ItemsSource = SecurityInspect.Inspect(_repoRoot);
        NetworkFields.ItemsSource = NetworkInspect.Inspect(
            _settings, _session.Kind, _session.Detail, _session.LastFailure, _apiListening, _guardListening);
        HealthFields.ItemsSource = OperatorHealth.From(snapshot, _session.Kind);
        _lastSnapshot = snapshot;
        ApplyMode();
        SidebarState.Text = _session.IsLive ? snapshot.RuntimeStatus.ToUpperInvariant() : "OFFLINE";
        SidebarDetail.Text = _session.Detail ?? snapshot.Warning;
        if (!string.IsNullOrWhiteSpace(snapshot.CapturedAt))
            StatusBarText.Text = $"Aggiornato {snapshot.CapturedAt} · {ModeLabel(_session.Kind)}";
    }

    private void ApplyMode()
    {
        OverviewMode.Text = ModeLabel(_session.Kind);
        OverviewModeHint.Text = _session.Kind switch
        {
            SessionKind.Hosted => "Questo processo è il runtime. Ferma lo spegne. STOP è l'arresto di emergenza (esecuzione già disabilitata in Gate 1).",
            SessionKind.Attached => "Runtime già in ascolto: sola osservazione. Ferma scollega e non lo spegne.",
            _ => "Nessun runtime. Premere Avvia, oppure lasciare l'auto-avvio attivo."
        };
        StopButton.Content = _session.Kind == SessionKind.Attached ? "Scollega" : "Ferma";
    }

    private static string ModeLabel(SessionKind kind) => kind switch
    {
        SessionKind.Hosted => "OSPITATO",
        SessionKind.Attached => "COLLEGATO",
        _ => "OFFLINE"
    };

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
        NavNetwork.Style = (Style)FindResource("NavButton");
        NavSecurity.Style = (Style)FindResource("NavButton");
        NavSuites.Style = (Style)FindResource("NavButton");
        NavSettings.Style = (Style)FindResource("NavButton");
        NavLog.Style = (Style)FindResource("NavButton");
        button.Style = (Style)FindResource("NavButtonActive");

        ViewOverview.Visibility = Visibility.Collapsed;
        ViewClient.Visibility = Visibility.Collapsed;
        ViewPhone.Visibility = Visibility.Collapsed;
        ViewPerception.Visibility = Visibility.Collapsed;
        ViewNetwork.Visibility = Visibility.Collapsed;
        ViewSecurity.Visibility = Visibility.Collapsed;
        ViewSuites.Visibility = Visibility.Collapsed;
        ViewSettings.Visibility = Visibility.Collapsed;
        ViewLog.Visibility = Visibility.Collapsed;

        if (ReferenceEquals(button, NavOverview)) { ViewOverview.Visibility = Visibility.Visible; PageTitle.Text = "Panoramica"; }
        else if (ReferenceEquals(button, NavClient)) { ViewClient.Visibility = Visibility.Visible; PageTitle.Text = "Client NosTale"; }
        else if (ReferenceEquals(button, NavPhone)) { ViewPhone.Visibility = Visibility.Visible; PageTitle.Text = "Telefono Guard AI"; }
        else if (ReferenceEquals(button, NavPerception)) { ViewPerception.Visibility = Visibility.Visible; PageTitle.Text = "Percezione"; }
        else if (ReferenceEquals(button, NavNetwork))
        {
            ViewNetwork.Visibility = Visibility.Visible;
            PageTitle.Text = "Rete";
            _lastListenProbeUtc = DateTime.MinValue;
            _ = RefreshListenThenShowAsync();
        }
        else if (ReferenceEquals(button, NavSecurity)) { ViewSecurity.Visibility = Visibility.Visible; PageTitle.Text = "Sicurezza"; }
        else if (ReferenceEquals(button, NavSuites)) { ViewSuites.Visibility = Visibility.Visible; PageTitle.Text = "Certificazione"; }
        else if (ReferenceEquals(button, NavSettings)) { ViewSettings.Visibility = Visibility.Visible; PageTitle.Text = "Impostazioni"; }
        else { ViewLog.Visibility = Visibility.Visible; PageTitle.Text = "Diario"; }
    }

    private async Task RefreshListenThenShowAsync()
    {
        await RefreshListenAsync().ConfigureAwait(true);
        NetworkFields.ItemsSource = NetworkInspect.Inspect(
            _settings, _session.Kind, _session.Detail, _session.LastFailure, _apiListening, _guardListening);
        HealthFields.ItemsSource = OperatorHealth.From(_lastSnapshot, _session.Kind);
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

        var process = string.IsNullOrWhiteSpace(SettingClientProcess.Text)
            ? new Gate1HostOptions().ClientProcessName
            : SettingClientProcess.Text.Trim();
        if (!OperatorSettings.TryValidate(dashboard, guard, timeout, process, out var invalid))
        {
            Status(invalid);
            return;
        }

        _settings.DashboardPort = dashboard;
        _settings.GuardPort = guard;
        _settings.OperationTimeoutMs = timeout;
        _settings.ClientProcessName = process;
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
        build.Click += async (_, _) => await RunToolAsync("dotnet", "build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release", "Compilazione runtime", pairing: false);
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

        await RunToolAsync("dotnet", $"\"{dll}\" {suite.Flag}", suite.Title, pairing: false);
    }

    private async void OnDxgiProbe(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            Status("Un'operazione è già in corso.");
            return;
        }

        _busy = true;
        Status("Probe DXGI in corso…");
        try
        {
            var result = await Task.Run(() => PerceptionProbe.Run(_repoRoot)).ConfigureAwait(true);
            PerceptionFields.ItemsSource = result.Fields;
            PerceptionSummary.Text = result.Summary;
            Status(result.Summary);
            _log.Operator(result.Summary);
        }
        catch (Exception ex)
        {
            _log.Error("Probe DXGI fallito.", ex);
            PerceptionSummary.Text = ex.Message;
            Status($"Probe DXGI fallito: {ex.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    private async void OnPairPhone(object sender, RoutedEventArgs e)
        => await RunPythonAsync("-m nosai.phone.deploy", "Abbinamento telefono");

    private async void OnEnrollPhone(object sender, RoutedEventArgs e)
        => await RunPythonAsync("-m nosai.phone.enroll", "Raccolta chiave telefono");

    private async Task RunPythonAsync(string arguments, string title)
    {
        var python = ToolRunner.FindPython();
        if (python is null)
        {
            const string missing = "Operazione non eseguita: Python non è nel PATH. Nessuna chiave scritta, nessuna coppia da considerare riuscita.";
            PairingStatus.Text = missing;
            Status(missing);
            return;
        }

        PairingStatus.Text = $"{title} in corso…";
        await RunToolAsync(python, arguments, title, pairing: true);
        RefreshSetup();
    }

    private async Task RunToolAsync(string fileName, string arguments, string title, bool pairing)
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
            if (result.ExitCode == 0)
            {
                Status($"{title}: completato.");
                if (pairing)
                    PairingStatus.Text = $"{title}: riuscito.";
            }
            else
            {
                var failed = $"{title}: non riuscito (uscita {result.ExitCode}).";
                Status(failed);
                if (pairing)
                    PairingStatus.Text = failed + " Nessuna coppia da considerare valida.";
                _log.Warning(failed, new Dictionary<string, object?> { ["exit"] = result.ExitCode });
            }
        }
        catch (Exception ex)
        {
            _log.Error($"{title} fallito.", ex);
            Status($"{title} fallito: {ex.Message}");
            if (pairing)
                PairingStatus.Text = $"{title} fallito: {ex.Message}";
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
        var brush = entry.Level switch
        {
            "ERROR" => (Brush)FindResource("DangerBrush"),
            "WARN" => (Brush)FindResource("WarnBrush"),
            "INFO" => (Brush)FindResource("LiveBrush"),
            _ => (Brush)FindResource("MutedBrush")
        };
        var paragraph = new Paragraph(new Run($"[{entry.At:HH:mm:ss}] {entry.Level,-5} {entry.Message}"))
        {
            Foreground = brush,
            Margin = new Thickness(0, 0, 0, 3)
        };
        LogBox.Document.Blocks.Add(paragraph);
        LogScroll.ScrollToEnd();
    }

    private void OnOpenOperatorLog(object sender, RoutedEventArgs e)
    {
        var path = Path.Combine(_repoRoot, OperatorLogFile.RelativePath);
        if (!File.Exists(path))
        {
            Status($"Diario su disco non ancora scritto: {path}");
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{path}\"",
            UseShellExecute = true
        });
        Status($"Diario: {path}");
    }

    private void OnClearLog(object sender, RoutedEventArgs e)
    {
        LogBox.Document.Blocks.Clear();
        LogBox.Document.Blocks.Add(new Paragraph());
    }

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
