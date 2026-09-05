using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace NosAi.ControlPanel;

public partial class App : Application
{
    private string _repoRoot = Directory.GetCurrentDirectory();

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandled;
        _repoRoot = WorkspaceLocator.Find();
        Directory.SetCurrentDirectory(_repoRoot);
        Directory.CreateDirectory(Path.Combine(_repoRoot, "data"));
        base.OnStartup(e);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, AttachDashboardShortcuts);
    }

    private void AttachDashboardShortcuts()
    {
        if (MainWindow is null) return;
        MainWindow.KeyDown += OnMainWindowKeyDown;
        if (MainWindow.FindName("NavLog") is System.Windows.Controls.Button logButton && logButton.Parent is System.Windows.Controls.Panel panel)
        {
            var cognitiveButton = new System.Windows.Controls.Button
            {
                Content = "🧠 Cervello & Memoria",
                Margin = new Thickness(4, 2, 4, 2),
                Padding = new Thickness(12, 9, 12, 9),
                ToolTip = "Apre l'osservabilità cognitiva e la memoria AI"
            };
            cognitiveButton.Click += (_, _) => OpenCognitiveWindow();
            var index = panel.Children.IndexOf(logButton);
            panel.Children.Insert(Math.Max(0, index), cognitiveButton);
        }
    }

    private void OnMainWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.F9)
        {
            e.Handled = true;
            var window = new PracticalTestCenterWindow(_repoRoot) { Owner = MainWindow };
            window.Show(); window.Activate();
        }
        else if (e.Key == Key.F10)
        {
            e.Handled = true;
            OpenCognitiveWindow();
        }
    }

    private void OpenCognitiveWindow()
    {
        if (MainWindow is null) return;
        var window = new CognitiveMemoryWindow(CognitiveObservabilityRegistry.Reader, _repoRoot) { Owner = MainWindow };
        window.Show(); window.Activate();
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "NosAi Control Panel", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
