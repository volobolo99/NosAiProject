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
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, AttachTestCenterShortcut);
    }

    private void AttachTestCenterShortcut()
    {
        if (MainWindow is null) return;
        MainWindow.KeyDown += OnMainWindowKeyDown;
    }

    private void OnMainWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F9 || Keyboard.Modifiers != ModifierKeys.Control) return;
        e.Handled = true;
        var window = new PracticalTestCenterWindow(_repoRoot) { Owner = MainWindow };
        window.Show();
        window.Activate();
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            e.Exception.Message,
            "NosAi Control Panel",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
