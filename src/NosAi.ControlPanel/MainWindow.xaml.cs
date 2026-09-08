using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using NosAi.LiveIntegration;
using NosAi.Runtime.Configuration;
using NosAi.Runtime.Gate2;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Navigation;
using NosAi.Runtime.Perception;

namespace NosAi.ControlPanel;

public partial class MainWindow : Window
{
    private readonly string _repoRoot;
    private readonly UiLogger _log = new();

    /// <summary>Il catalogo del client, aperto una volta e ricordato.</summary>
    private readonly CatalogueNames _catalogueNames = new();
    private readonly RuntimeSession _session;
    private readonly bool _elevated;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(1) };
    private OperatorSettings _settings;
    private bool _busy;
    private DateTime _lastListenProbeUtc = DateTime.MinValue;
    private bool? _apiListening;
    private bool? _guardListening;
    private SnapshotView _lastSnapshot = SnapshotView.Empty("avvio");
    private FileSystemWatcher? _targetFiles;
    private string? _targetSignature;
    private string? _inventoryPanelRoiSignature;

    public MainWindow()
    {
        InitializeComponent();
        _repoRoot = Directory.GetCurrentDirectory();
        _session = new RuntimeSession(_log);
        _settings = OperatorSettings.Load(_repoRoot);
        _elevated = ElevationInspect.IsElevated();
        ElevationCard.Visibility = _elevated ? Visibility.Collapsed : Visibility.Visible;
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
        WatchTargetFiles();
        ApplyTarget();
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

    private async void OnHalt(object sender, RoutedEventArgs e)
    {
        await _session.HaltAsync();
        await RefreshSnapshotAsync();
        Status("Halt richiesto. Interruttori disarmati, atto in volo abortito.");
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
            EventLogHealth? log = null;
            try { log = await _session.ReadEventLogAsync().ConfigureAwait(true); }
            catch (Exception) { /* shown as unread */ }
            ApplySnapshot(snapshot, log);
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

    private void ApplySnapshot(SnapshotView snapshot, EventLogHealth? eventLog = null)
    {
        OverviewHealth.Text = snapshot.RuntimeStatus;
        OverviewClient.Text = FirstValue(snapshot.Client, "Stato");
        OverviewGuard.Text = FirstValue(snapshot.Guard, "Autenticato");
        OverviewSafety.Text = FirstValue(snapshot.Safety, "Esecuzione");
        // Reports the standing verdict. A terminal one is named, and there is no
        // retry control: the panel asks, it does not decide, and nothing here
        // can mark a session as actuating.
        OverviewAuthority.Text = snapshot.SessionAuthorityLine;
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
            _settings, _session.Kind, _session.Detail, _session.LastFailure, _apiListening, _guardListening, _elevated);
        GameObservationFields.ItemsSource = snapshot.GameObservation;
        HealthFields.ItemsSource = OperatorHealth.From(snapshot, _session.Kind);
        DecisionFields.ItemsSource = DecisionInspect.Inspect(_session.Kind, _session.DescribeDecisions());
        ResilienceFields.ItemsSource = snapshot.Resilience;
        EventLogFields.ItemsSource = EventLogInspect.Inspect(eventLog);
        ApplyMap(snapshot);
        ApplyAround(snapshot);
        ApplyTarget();
        ApplyInventoryPanelRoi();
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

    private void ApplyMap(SnapshotView snapshot)
    {
        MapView map = MapInspect.Observe(_session.Kind, snapshot.MapWorld);
        MapFields.ItemsSource = map.Fields;
        StandingCellText.Text = map.StandingLine;
        StandingCellText.Foreground = map.StandingIsError
            ? (Brush)FindResource("DangerBrush")
            : (Brush)FindResource("TextBrush");
        MapCropGlyphs.Text = map.CropGlyphs;
    }

    private void ApplyAround(SnapshotView snapshot)
    {
        DateTime nowUtc = TimeProvider.System.GetUtcNow().UtcDateTime;
        SurroundingsView around = SurroundingsInspect.Inspect(snapshot.Entities, nowUtc, _catalogueNames.Of);
        SurroundingsSummary.Text = around.Summary;
        SurroundingsFields.ItemsSource = around.Fields;
        CombatFields.ItemsSource = CombatInspect.Inspect(snapshot.HitBy, snapshot.HasTarget, nowUtc).Fields;
        KeybindsFields.ItemsSource = KeybindsInspect.Inspect(Path.Combine(_repoRoot, KeybindMap.RelativePath)).Fields;
    }

    /// <summary>
    /// Candidate file and target-frame ROI, redrawn only when those files change.
    /// The hunt lives on disk, not on the snapshot: a missing file stays
    /// "caccia non iniziata", never a fabricated zero.
    /// </summary>
    private void ApplyTarget()
    {
        string candidatePath = Path.Combine(_repoRoot, TargetIdFinder.CandidatePath);
        string roiPath = Path.Combine(_repoRoot, TargetRoiCalibration.RelativePath);
        string signature = TargetInspect.Signature(candidatePath, roiPath);
        if (signature == _targetSignature)
            return;

        _targetSignature = signature;
        TargetHuntView view = TargetInspect.Inspect(candidatePath, roiPath);
        TargetHuntStatusText.Text = view.HuntStatusLine;
        TargetClearedPassText.Text = view.ClearedPassLine;
        TargetAdviceText.Text = view.AdviceLine;
        TargetRoiText.Text = view.RoiLine;
        TargetFields.ItemsSource = view.Fields;
        TargetHuntStatusText.Foreground = view.HuntKind == TargetHuntKind.NotStarted
            ? (Brush)FindResource("MutedBrush")
            : (Brush)FindResource("TextBrush");
        TargetClearedPassText.Foreground = view.ClearedPassMissing
            ? (Brush)FindResource("WarnBrush")
            : view.HuntKind is TargetHuntKind.NotStarted or TargetHuntKind.Unreadable
                ? (Brush)FindResource("MutedBrush")
                : (Brush)FindResource("LiveBrush");
        TargetRoiText.Foreground = (Brush)FindResource("MutedBrush");
    }

    /// <summary>
    /// The equipment-panel ROI calibration is a versioned real file, not a
    /// snapshot field: read it on the same cadence as the target ROI and draw
    /// which slots are calibrated, when, and against which resolution. Read-only;
    /// nothing here writes the file or executes equip/unequip.
    /// </summary>
    private void ApplyInventoryPanelRoi()
    {
        string path = Path.Combine(_repoRoot, InventoryPanelRoiCalibration.RelativePath);
        string signature = InventoryPanelInspect.Signature(path);
        if (signature == _inventoryPanelRoiSignature)
            return;
        _inventoryPanelRoiSignature = signature;

        InventoryPanelRoiView view = InventoryPanelInspect.Inspect(path);
        InventoryPanelRoiText.Text = view.Summary;
        InventoryPanelRoiText.Foreground = view.Kind == InventoryPanelRoiKind.Calibrated
            ? (Brush)FindResource("LiveBrush")
            : (Brush)FindResource("MutedBrush");
        InventoryPanelRoiFields.ItemsSource = view.Fields;
    }

    /// <summary>
    /// Wakes the target view when the candidate or ROI file changes, instead of
    /// polling those files on their own timer.
    /// </summary>
    private void WatchTargetFiles()
    {
        string data = Path.Combine(_repoRoot, "data");
        try
        {
            if (!Directory.Exists(data))
                return;

            var watcher = new FileSystemWatcher(data)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size
                    | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };
            watcher.Changed += OnTargetFileEvent;
            watcher.Created += OnTargetFileEvent;
            watcher.Deleted += OnTargetFileEvent;
            watcher.Renamed += OnTargetFileEvent;
            _targetFiles = watcher;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _log.Warning($"Osservazione file bersaglio non avviata: {ex.Message}");
        }
    }

    private void OnTargetFileEvent(object sender, FileSystemEventArgs e)
    {
        if (!IsTargetWatchedPath(e.FullPath)
            && !(e is RenamedEventArgs renamed && IsTargetWatchedPath(renamed.OldFullPath)))
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            _targetSignature = null;
            ApplyTarget();
        });
    }

    private static bool IsTargetWatchedPath(string fullPath)
    {
        string name = Path.GetFileName(fullPath);
        return name.Equals("target_candidates.txt", StringComparison.OrdinalIgnoreCase)
            || name.Equals("target-roi.calibration", StringComparison.OrdinalIgnoreCase);
    }

    private static string ModeLabel(SessionKind kind) => kind switch
    {
        SessionKind.Hosted => "OSPITATO",
        SessionKind.Attached => "COLLEGATO",
        _ => "OFFLINE"
    };

    private static string FirstValue(IReadOnlyList<DisplayField> fields, string label)
        => fields.FirstOrDefault(f => f.Label == label)?.Value ?? "UNKNOWN";

    private void RefreshSetup() => SetupList.ItemsSource = AutoSetup.Inspect(_repoRoot, _settings.ObserveGame, _elevated);

    private void OnNav(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        NavOverview.Style = (Style)FindResource("NavButton");
        NavClient.Style = (Style)FindResource("NavButton");
        NavMap.Style = (Style)FindResource("NavButton");
        NavAround.Style = (Style)FindResource("NavButton");
        NavTarget.Style = (Style)FindResource("NavButton");
        NavEquip.Style = (Style)FindResource("NavButton");
        NavPhone.Style = (Style)FindResource("NavButton");
        NavPerception.Style = (Style)FindResource("NavButton");
        NavNetwork.Style = (Style)FindResource("NavButton");
        NavDecision.Style = (Style)FindResource("NavButton");
        NavSecurity.Style = (Style)FindResource("NavButton");
        NavSuites.Style = (Style)FindResource("NavButton");
        NavSettings.Style = (Style)FindResource("NavButton");
        NavLog.Style = (Style)FindResource("NavButton");
        button.Style = (Style)FindResource("NavButtonActive");

        ViewOverview.Visibility = Visibility.Collapsed;
        ViewClient.Visibility = Visibility.Collapsed;
        ViewMap.Visibility = Visibility.Collapsed;
        ViewAround.Visibility = Visibility.Collapsed;
        ViewTarget.Visibility = Visibility.Collapsed;
        ViewEquip.Visibility = Visibility.Collapsed;
        ViewPhone.Visibility = Visibility.Collapsed;
        ViewPerception.Visibility = Visibility.Collapsed;
        ViewNetwork.Visibility = Visibility.Collapsed;
        ViewDecision.Visibility = Visibility.Collapsed;
        ViewSecurity.Visibility = Visibility.Collapsed;
        ViewSuites.Visibility = Visibility.Collapsed;
        ViewSettings.Visibility = Visibility.Collapsed;
        ViewLog.Visibility = Visibility.Collapsed;

        if (ReferenceEquals(button, NavOverview)) { ViewOverview.Visibility = Visibility.Visible; PageTitle.Text = "Panoramica"; }
        else if (ReferenceEquals(button, NavClient)) { ViewClient.Visibility = Visibility.Visible; PageTitle.Text = "Client NosTale"; }
        else if (ReferenceEquals(button, NavMap)) { ViewMap.Visibility = Visibility.Visible; PageTitle.Text = "Mappa"; }
        else if (ReferenceEquals(button, NavAround)) { ViewAround.Visibility = Visibility.Visible; PageTitle.Text = "Attorno"; }
        else if (ReferenceEquals(button, NavTarget))
        {
            _targetSignature = null;
            ApplyTarget();
            ViewTarget.Visibility = Visibility.Visible;
            PageTitle.Text = "Bersaglio";
        }
        else if (ReferenceEquals(button, NavEquip)) { ViewEquip.Visibility = Visibility.Visible; PageTitle.Text = "Equipaggiamento"; ApplyInventoryPanelRoi(); }
        else if (ReferenceEquals(button, NavPhone)) { ViewPhone.Visibility = Visibility.Visible; PageTitle.Text = "Telefono Guard AI"; }
        else if (ReferenceEquals(button, NavPerception))
        {
            ViewPerception.Visibility = Visibility.Visible;
            PageTitle.Text = "Percezione";
            RefreshScreenCalibration();
        }
        else if (ReferenceEquals(button, NavNetwork))
        {
            ViewNetwork.Visibility = Visibility.Visible;
            PageTitle.Text = "Rete";
            _lastListenProbeUtc = DateTime.MinValue;
            _ = RefreshListenThenShowAsync();
        }
        else if (ReferenceEquals(button, NavDecision))
        {
            ViewDecision.Visibility = Visibility.Visible;
            PageTitle.Text = "Decisione";
            DecisionFields.ItemsSource = DecisionInspect.Inspect(_session.Kind, _session.DescribeDecisions());
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
            _settings, _session.Kind, _session.Detail, _session.LastFailure, _apiListening, _guardListening, _elevated);
        GameObservationFields.ItemsSource = _lastSnapshot.GameObservation;
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
        SettingObserveGame.Text = _settings.ObserveGame ?? "";
        SettingRunDecisionLoop.IsChecked = _settings.RunDecisionLoop;
        SettingDecisionIntervalMs.Text = _settings.DecisionIntervalMs.ToString();
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
        var observeGame = SettingObserveGame.Text?.Trim() ?? "";
        if (!int.TryParse(SettingDecisionIntervalMs.Text, out var decisionInterval))
        {
            Status("L'intervallo di decisione deve essere un numero.");
            return;
        }

        if (!OperatorSettings.TryValidate(dashboard, guard, timeout, process, observeGame, decisionInterval, out var invalid))
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
        _settings.ObserveGame = observeGame;
        _settings.RunDecisionLoop = SettingRunDecisionLoop.IsChecked == true;
        _settings.DecisionIntervalMs = decisionInterval;
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
        RefreshSetup();
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

    /// <summary>
    /// Rilegge dai file veri che cosa il repository sa della proiezione
    /// schermo -> mappa. Nessun valore viene tenuto in memoria fra una raccolta e
    /// l'altra: quello che si vede e' quello che c'e' su disco adesso.
    /// </summary>
    private void RefreshScreenCalibration()
    {
        try
        {
            ScreenCalibrationFields.ItemsSource = ScreenCalibrationInspect.Inspect(_repoRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ScreenCalibrationFields.ItemsSource = new[]
            {
                new DisplayField("Calibrazione", $"UNKNOWN · {ex.GetType().Name}", "UNKNOWN")
            };
        }
    }

    private void OnScreenCalibrationRefresh(object sender, RoutedEventArgs e) => RefreshScreenCalibration();

    private void CalibrationSay(string line)
    {
        ScreenCalibrationLog.AppendText(line + Environment.NewLine);
        ScreenCalibrationLog.ScrollToEnd();
    }

    /// <summary>
    /// Raccoglie i campioni di calibrazione dalla finestra, non da un terminale.
    /// </summary>
    /// <remarks>
    /// La radice del repository e' passata esplicitamente: il 2026-09-07 lo stesso
    /// lavoro fatto da riga di comando ha scritto campioni e calibrazione in
    /// <c>C:\WINDOWS\system32\data\</c>, perche' il comando era partito da li',
    /// e i file non esistevano da nessun'altra parte. Un pulsante non puo'
    /// sbagliare cartella.
    /// </remarks>
    private async void OnScreenSamples(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            Status("Un'operazione è già in corso.");
            return;
        }

        _busy = true;
        ScreenSampleButton.IsEnabled = false;
        ScreenCalibrationLog.Text = string.Empty;
        Status("Raccolta campioni: aspetta che il personaggio si FERMI davvero, poi clicca lontano.");
        try
        {
            int code = await Task.Run(() => ScreenProjectionWatcher.Run(
                seconds: 300,
                wanted: ScreenSampleCoach.DefaultWantedSamples,
                repoRoot: _repoRoot,
                report: line => Dispatcher.Invoke(() => CalibrationSay(line)))).ConfigureAwait(true);

            Status(code == 0
                ? "Raccolta campioni conclusa."
                : $"Raccolta campioni rifiutata (codice {code}).");
            _log.Operator($"Raccolta campioni schermo, uscita {code}.");
        }
        catch (Exception ex)
        {
            _log.Error("Raccolta campioni schermo fallita.", ex);
            CalibrationSay($"[ERRORE] {ex.Message}");
            Status($"Raccolta campioni fallita: {ex.Message}");
        }
        finally
        {
            _busy = false;
            ScreenSampleButton.IsEnabled = true;
            RefreshScreenCalibration();
        }
    }

    private async void OnScreenCalibrate(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            Status("Un'operazione è già in corso.");
            return;
        }

        _busy = true;
        Status("Calibrazione schermo in corso…");
        try
        {
            int code = await Task.Run(() => ScreenProjectionProbe.RunSolve(
                _repoRoot, line => Dispatcher.Invoke(() => CalibrationSay(line)))).ConfigureAwait(true);

            Status(code == 0 ? "Calibrazione scritta." : "Calibrazione rifiutata: niente è stato scritto.");
            _log.Operator($"Calibrazione schermo, uscita {code}.");
        }
        catch (Exception ex)
        {
            _log.Error("Calibrazione schermo fallita.", ex);
            CalibrationSay($"[ERRORE] {ex.Message}");
            Status($"Calibrazione fallita: {ex.Message}");
        }
        finally
        {
            _busy = false;
            RefreshScreenCalibration();
        }
    }

    private void OnScreenSamplesClear(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            Status("Un'operazione è già in corso.");
            return;
        }

        // I campioni sono una misura del client reale che costa una sessione
        // all'operatore: si buttano solo dicendolo.
        if (MessageBox.Show(
                "Cancello tutti i campioni raccolti? La calibrazione gia' scritta resta.",
                "Azzera campioni", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        int code = ScreenProjectionProbe.RunClear(_repoRoot, CalibrationSay);
        Status(code == 0 ? "Campioni azzerati." : $"Azzeramento rifiutato (codice {code}).");
        _log.Operator($"Azzeramento campioni schermo, uscita {code}.");
        RefreshScreenCalibration();
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
            var result = await Task.Run(() => PerceptionProbe.Run(_repoRoot, _settings.ClientProcessName)).ConfigureAwait(true);
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

    private async void OnTrainGlyphs(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            Status("Un'operazione è già in corso.");
            return;
        }

        _busy = true;
        Status("Addestramento glifi HUD dal wire…");
        try
        {
            var hp = _lastSnapshot.ObservationLastHp;
            var maxHp = _lastSnapshot.ObservationLastMaxHp;
            var result = await Task.Run(() =>
                PerceptionProbe.TrainFromWire(_repoRoot, _settings.ClientProcessName, hp, maxHp)).ConfigureAwait(true);
            PerceptionFields.ItemsSource = result.Fields;
            PerceptionSummary.Text = result.Summary;
            Status(result.Summary);
            _log.Operator(result.Summary);
        }
        catch (Exception ex)
        {
            _log.Error("Addestramento glifi HUD fallito.", ex);
            PerceptionSummary.Text = ex.Message;
            Status($"Addestramento glifi HUD fallito: {ex.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    private void OnDetectObserveGame(object sender, RoutedEventArgs e)
    {
        if (!_lastSnapshot.ClientProcessId.HasValue || _lastSnapshot.ClientProcessId.Value is not int pid || pid <= 0)
        {
            var reason = string.IsNullOrWhiteSpace(_lastSnapshot.ClientProcessId.FailureReason)
                ? "process_not_attached"
                : _lastSnapshot.ClientProcessId.FailureReason;
            Status($"UNKNOWN · {reason}. Casella invariata: senza PID del client non si legge la tabella TCP.");
            return;
        }

        ClientNetworkObservation observation = ClientNetworkObserver.Observe(pid);
        if (ObserveGameDetector.TrySuggest(observation, out var endpoint, out var status))
            SettingObserveGame.Text = endpoint;
        Status(status);
    }

    /// <summary>
    /// T-12 (AP-07), seconda metà: registra il traffico reale mentre l'operatore
    /// equipaggia/disequipaggia un oggetto, così <see cref="OnAnalyzeEquipWire"/> può
    /// mostrare quale <c>InventoryKind</c> il client invia da equipaggiato. Sniff
    /// only (stessa garanzia di <c>WireRecorder</c>): niente è alterato o iniettato.
    /// </summary>
    /// <summary>
    /// Registra il filo e mette accanto alla cattura il testo che l'operatore ha
    /// letto sullo schermo mentre registrava.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Perche' la nota sta nel pannello e non in un file scritto a mano.</b>
    /// Il 2026-09-07 il catalogo ha acquisito 50 704 voci di testo del client --
    /// missioni, battute di NPC, nomi di mappa -- e il filo porta degli id
    /// (<c>sayi</c>, <c>msgi</c>). Il legame fra i due non si stabilisce dalle
    /// catture che abbiamo, perche' nessuna dice cosa fosse a schermo in quel
    /// momento (T-16). Una coppia -- testo osservato e riga della cattura -- lo
    /// stabilisce; tre lo confermano. La nota va scritta mentre si registra, non
    /// dopo: e' l'unico momento in cui esiste.
    /// </para>
    /// <para>
    /// Nota e cattura prendono lo stesso nome per costruzione, cosi' non possono
    /// separarsi. Il percorso e' quello del repository, passato esplicitamente.
    /// </para>
    /// </remarks>
    private async void OnRecordWireWithNote(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            Status("Un'operazione è già in corso.");
            return;
        }

        string endpoint = SettingObserveGame.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            WireCaptureSummary.Text = "Nessun endpoint rilevato. Vai su Impostazioni e premi \"Rileva endpoint\" (serve il client NosTale aperto e collegato).";
            return;
        }

        if (!int.TryParse(WireCaptureSeconds.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds)
            || seconds <= 0 || seconds > 3600)
        {
            WireCaptureSummary.Text = "Durata non valida: un intero di secondi fra 1 e 3600.";
            return;
        }

        var dll = ResolveRuntimeDll();
        if (dll is null)
        {
            Status("Runtime non compilato. Vai su Certificazione e premi Compila runtime.");
            return;
        }

        string stem = string.Create(CultureInfo.InvariantCulture, $"wire_{DateTime.Now:yyyyMMdd_HHmmss}");
        WireCaptureButton.IsEnabled = false;
        WireCaptureSummary.Text = $"Registrazione in corso ({seconds}s) su data/{stem}.noscap. Provoca i messaggi ORA e scrivi qui sotto cosa compare.";
        try
        {
            var result = await RunToolAsync(
                "dotnet", $"\"{dll}\" --record-wire {endpoint} data/{stem}.noscap --watch {seconds}",
                "Registrazione filo", pairing: false);

            if (result.ExitCode != 0)
            {
                WireCaptureSummary.Text = result.Output.Contains("access_denied_run_elevated", StringComparison.Ordinal)
                    ? "Registrazione non riuscita: serve amministratore. Riavvia il pannello come amministratore e ripeti."
                    : $"Registrazione non riuscita (uscita {result.ExitCode}). Motivo nel Diario.";
                return;
            }

            string note = WireCaptureNote.Text ?? string.Empty;
            string notePath = Path.Combine(_repoRoot, "data", $"{stem}.note.txt");
            await File.WriteAllTextAsync(notePath, string.Create(CultureInfo.InvariantCulture,
                $"cattura: {stem}.noscap{Environment.NewLine}"
                + $"endpoint: {endpoint}{Environment.NewLine}"
                + $"durata_s: {seconds}{Environment.NewLine}"
                + $"scritta: {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}"
                + $"---{Environment.NewLine}{note}{Environment.NewLine}")).ConfigureAwait(true);

            WireCaptureSummary.Text = string.IsNullOrWhiteSpace(note)
                ? $"Cattura scritta in data/{stem}.noscap. La nota e' VUOTA: senza il testo visto a schermo la cattura non chiude T-16."
                : $"Cattura e nota scritte: data/{stem}.noscap e data/{stem}.note.txt.";
            _log.Operator($"Registrazione filo {stem}, nota {(string.IsNullOrWhiteSpace(note) ? "vuota" : "presente")}.");

            // Le due meta' di T-16 esistono entrambe solo adesso: gli id che il filo
            // ha appena detto, e il testo che l'operatore ha appena scritto. Metterle
            // una accanto all'altra qui evita che qualcuno debba rileggere la cattura
            // a mano piu' tardi, quando non ricordera' piu' cosa aveva visto.
            string capturePath = Path.Combine(_repoRoot, "data", $"{stem}.noscap");
            WireMessageRead read = await Task.Run(
                () => WireMessageInspect.Read(capturePath, note, WorldReplayCommand.CatalogLanguage))
                .ConfigureAwait(true);
            WireMessagePairing.Text = WireMessageInspect.Describe(read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("Registrazione filo fallita.", ex);
            WireCaptureSummary.Text = $"Registrazione fallita: {ex.Message}";
        }
        finally
        {
            WireCaptureButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// La registrazione piu' recente che corrisponde al modello, o nulla.
    /// </summary>
    /// <remarks>
    /// Per data di scrittura e non per nome: il nome porta un timestamp, ma
    /// ordinare delle stringhe per far finta di ordinare degli istanti e' il
    /// genere di scorciatoia che regge finche' il formato non cambia.
    /// </remarks>
    private static string? NewestCapture(string directory, string pattern)
    {
        if (!Directory.Exists(directory))
            return null;

        return Directory.EnumerateFiles(directory, pattern)
            .Select(static path => new FileInfo(path))
            .OrderByDescending(static info => info.LastWriteTimeUtc)
            .FirstOrDefault()?.FullName;
    }

    private async void OnRecordEquipWire(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            Status("Un'operazione è già in corso.");
            return;
        }

        string endpoint = SettingObserveGame.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            EquipWireSummary.Text = "Nessun endpoint rilevato. Premi \"Rileva endpoint\" qui sopra (serve il client NosTale aperto e collegato).";
            return;
        }

        var dll = ResolveRuntimeDll();
        if (dll is null)
        {
            Status("Runtime non compilato. Vai su Certificazione e premi Compila runtime.");
            return;
        }

        // Mai su data/equip_test.noscap. Quel file e' una delle cinque catture di
        // riferimento su cui poggia docs/PROTOCOLLO_NOSTALE.md, non e' versionato
        // (data/* e' escluso) e quindi ne esiste una copia sola: un pulsante che
        // lo riscrive cancella la prova su cui il repository si basa. Ogni
        // registrazione prende un nome proprio, e l'analisi legge la piu' recente.
        string captureName = string.Create(CultureInfo.InvariantCulture,
            $"equip_test_{DateTime.Now:yyyyMMdd_HHmmss}.noscap");
        EquipWireSummary.Text = $"Registrazione in corso (30s) su {captureName}: equipaggia e poi disequipaggia un oggetto ORA.";
        EquipWireFields.ItemsSource = null;
        var result = await RunToolAsync(
            "dotnet", $"\"{dll}\" --record-wire {endpoint} data/{captureName} --watch 30",
            "Registrazione equip", pairing: false);
        EquipWireSummary.Text = result switch
        {
            { ExitCode: 0 } => "Registrazione completata. Premi \"Analizza registrazione\".",
            _ when result.Output.Contains("access_denied_run_elevated", StringComparison.Ordinal)
                => "Registrazione non riuscita: serve amministratore. Premi \"Riavvia come amministratore\" qui sopra, poi ripeti.",
            _ => $"Registrazione non riuscita (uscita {result.ExitCode}). Motivo nel Diario."
        };
    }

    /// <summary>Rilegge l'ultima registrazione e mostra solo le righe inventario (kind/slot/vnum/amount/rarity).</summary>
    private async void OnAnalyzeEquipWire(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            Status("Un'operazione è già in corso.");
            return;
        }

        string? path = NewestCapture(Path.Combine(_repoRoot, "data"), "equip_test*.noscap");
        if (path is null)
        {
            EquipWireSummary.Text = "Nessuna registrazione trovata. Premi prima \"Registra equip (30s)\".";
            return;
        }

        var dll = ResolveRuntimeDll();
        if (dll is null)
        {
            Status("Runtime non compilato.");
            return;
        }

        // `path` e' la registrazione piu' recente, ed e' quella che va analizzata.
        // Fino al 2026-09-08 veniva calcolata e poi scartata: il comando riceveva
        // il letterale `data/equip_test.noscap`, cioe' la cattura di riferimento
        // del 2 settembre, mostrata all'operatore come se fosse quella appena
        // registrata. La meta' che scrive era stata corretta, questa no.
        string name = Path.GetFileName(path);
        var lines = new List<string>();
        ToolResult replay = await RunToolAsync(
            "dotnet", $"\"{dll}\" --world-replay data/{name}",
            "Analisi registrazione equip", pairing: false,
            onLine: line =>
            {
                if (line.Contains("kind=", StringComparison.Ordinal))
                    lines.Add(line.Trim());
            });

        // Una rigiocata fallita non e' una registrazione senza inventario: dirlo
        // con lo stesso messaggio manderebbe l'operatore a rifare una cattura
        // che andava bene.
        if (replay.ExitCode != 0)
        {
            EquipWireFields.ItemsSource = Array.Empty<DisplayField>();
            EquipWireSummary.Text =
                $"La rigiocata di data/{name} non e' riuscita (uscita {replay.ExitCode}). Motivo nel Diario: la registrazione potrebbe essere vuota o illeggibile.";
            return;
        }

        // Righe rilette da un file: CACHED, non LIVE. «Wire» non e' nemmeno uno
        // dei cinque valori del vocabolario di provenienza.
        EquipWireFields.ItemsSource = lines.Count > 0
            ? lines.Select((l, i) => new DisplayField($"riga {i + 1}", l, "Cached")).ToArray()
            : Array.Empty<DisplayField>();
        EquipWireSummary.Text = lines.Count > 0
            ? $"{lines.Count} righe inventario da data/{name}. Confronta \"kind=\" per lo stesso vnum prima e dopo l'equip: il valore che cambia (o appare solo da equipaggiato) è l'InventoryKind cercato."
            : $"Nessuna riga inventario (ivn) in data/{name}: l'azione potrebbe non essere stata osservata. Ripeti la registrazione.";
    }

    /// <summary>
    /// Decodifica dal vivo: stessa catena di <c>--world-replay</c>
    /// (<c>NosTaleWorldProtocolDecoder</c>) applicata al filo mentre il client
    /// gioca, non a un file dopo il fatto. Ogni riga (RETE e CLIENT) esce dal
    /// processo mentre il test è in corso e <see cref="RunToolAsync"/> la
    /// scrive nel Diario riga per riga: qui si mostra solo il riepilogo finale,
    /// per non riempire questa card di centinaia di righe.
    /// </summary>
    private async void OnStartLiveDecode(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            Status("Un'operazione è già in corso.");
            return;
        }

        string endpoint = SettingObserveGame.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            LiveDecodeSummary.Text = "Nessun endpoint rilevato. Premi \"Rileva endpoint\" qui sopra (serve il client NosTale aperto e collegato).";
            return;
        }

        var dll = ResolveRuntimeDll();
        if (dll is null)
        {
            Status("Runtime non compilato. Vai su Certificazione e premi Compila runtime.");
            return;
        }

        LiveDecodeSummary.Text = "Decodifica in corso (180s): gioca normalmente, ogni riga RETE/CLIENT appare nel Diario mano a mano che arriva.";
        string? tail = null;
        var result = await RunToolAsync(
            "dotnet", $"\"{dll}\" --live-decode {endpoint} --watch 180",
            "Decodifica dal vivo", pairing: false,
            onLine: line =>
            {
                if (line.StartsWith("frame leggibili", StringComparison.Ordinal))
                    tail = line.Trim();
            });
        LiveDecodeSummary.Text = result switch
        {
            { ExitCode: 0 } when tail is not null => $"Decodifica completata — {tail}. Ogni riga è nel Diario.",
            { ExitCode: 0 } => "Decodifica completata, ma nessun frame leggibile: il client non ha inviato traffico durante la finestra.",
            _ when result.Output.Contains("access_denied_run_elevated", StringComparison.Ordinal)
                => "Decodifica non riuscita: serve amministratore. Premi \"Riavvia come amministratore\" qui sopra, poi ripeti.",
            _ => $"Decodifica non riuscita (uscita {result.ExitCode}). Motivo nel Diario."
        };
    }

    /// <summary>
    /// Chiude questa istanza e ne riapre una nuova con UAC, per i test (come
    /// <see cref="OnRecordEquipWire"/>) che aprono il driver WinDivert e rifiutano
    /// con <c>access_denied_run_elevated</c> a una console non amministratore. Un
    /// solo bottone al posto di richiudere e riaprire a mano da "Esegui come
    /// amministratore".
    /// </summary>
    private void OnRestartElevated(object sender, RoutedEventArgs e)
    {
        string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            Status("Impossibile trovare l'eseguibile corrente: riavvia manualmente come amministratore.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = _repoRoot,
                UseShellExecute = true,
                Verb = "runas"
            });
            Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Status("Riavvio come amministratore annullato dal prompt UAC.");
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

    private async Task<ToolResult> RunToolAsync(
        string fileName, string arguments, string title, bool pairing, Action<string>? onLine = null)
    {
        if (_busy)
        {
            Status("Un'operazione è già in corso.");
            return new ToolResult(-1, "");
        }

        _busy = true;
        Status($"{title} in corso…");
        _log.Operator($"{title}: {fileName} {arguments}");
        try
        {
            var result = await ToolRunner.RunAsync(fileName, arguments, _repoRoot, line =>
                Dispatcher.BeginInvoke(() =>
                {
                    _log.Operator(line);
                    onLine?.Invoke(line);
                })).ConfigureAwait(true);
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
            return result;
        }
        catch (Exception ex)
        {
            _log.Error($"{title} fallito.", ex);
            Status($"{title} fallito: {ex.Message}");
            if (pairing)
                PairingStatus.Text = $"{title} fallito: {ex.Message}";
            return new ToolResult(-1, ex.Message);
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
        _targetFiles?.Dispose();
        _targetFiles = null;
        if (_session.Kind == SessionKind.Hosted)
            await _session.DisposeAsync();
    }
}
