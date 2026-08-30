using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace NosAi.ControlPanel;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandled;
        var root = WorkspaceLocator.Find();
        Directory.SetCurrentDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "data"));
        base.OnStartup(e);
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
