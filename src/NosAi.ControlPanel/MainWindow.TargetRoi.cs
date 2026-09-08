using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

namespace NosAi.ControlPanel;

public partial class MainWindow
{
    private async void OnCaptureTargetCrop(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            Status("Un'operazione è già in corso.");
            return;
        }

        var dll = ResolveRuntimeDll();
        if (dll is null)
        {
            Status("Runtime non compilato. Vai su Certificazione e premi Compila runtime.");
            return;
        }

        CaptureTargetCropButton.IsEnabled = false;
        try
        {
            DateTime runStarted = DateTime.UtcNow;
            await RunToolAsync("dotnet", $"\"{dll}\" --hud-probe", "Cattura ritaglio bersaglio", pairing: false);

            string path = Path.Combine(WorkspaceLocator.Find(), TargetRoiCalibrationCard.CropRelativePath);
            bool exists = File.Exists(path);
            DateTime writtenUtc = exists ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            TargetCropInfo.Text = TargetRoiCalibrationCard.DescribeCrop(exists, writtenUtc, runStarted);

            if (exists)
            {
                var bmp = new BitmapImage();
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = fs;
                    bmp.EndInit();
                }

                bmp.Freeze();
                TargetCropImage.Source = bmp;
            }
            else
            {
                TargetCropImage.Source = null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("Errore durante la cattura del ritaglio bersaglio.", ex);
            TargetRoiSummary.Text = $"Fallito: {ex.Message}";
        }
        finally
        {
            CaptureTargetCropButton.IsEnabled = true;
        }
    }

    private async void OnRegisterTargetRoi(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            Status("Un'operazione è già in corso.");
            return;
        }

        var dll = ResolveRuntimeDll();
        if (dll is null)
        {
            Status("Runtime non compilato. Vai su Certificazione e premi Compila runtime.");
            return;
        }

        if (!TargetRoiCalibrationCard.TryValidateFractions(
                TargetRoiX.Text, TargetRoiY.Text, TargetRoiW.Text, TargetRoiH.Text,
                out double x, out double y, out double w, out double h, out string? refusal))
        {
            TargetRoiSummary.Text = refusal;
            return;
        }

        RegisterTargetRoiButton.IsEnabled = false;
        try
        {
            string arguments = $"\"{dll}\" --hud-probe --calibrate-target " +
                               $"{x.ToString(CultureInfo.InvariantCulture)} " +
                               $"{y.ToString(CultureInfo.InvariantCulture)} " +
                               $"{w.ToString(CultureInfo.InvariantCulture)} " +
                               $"{h.ToString(CultureInfo.InvariantCulture)}";
            var result = await RunToolAsync("dotnet", arguments, "Registra calibrazione bersaglio", pairing: false);

            string refusedLine = result.Output
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .FirstOrDefault(line => line.Contains("[REFUSED]", StringComparison.Ordinal));
            if (refusedLine is not null)
            {
                TargetRoiSummary.Text = refusedLine;
            }
            else
            {
                TargetRoiSummary.Text = string.IsNullOrWhiteSpace(result.Output)
                    ? "Calibrazione registrata."
                    : result.Output.Trim();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("Errore durante la registrazione della calibrazione del riquadro bersaglio.", ex);
            TargetRoiSummary.Text = $"Fallito: {ex.Message}";
        }
        finally
        {
            RegisterTargetRoiButton.IsEnabled = true;
        }
    }

    private async void OnRereadTargetState(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            Status("Un'operazione è già in corso.");
            return;
        }

        var dll = ResolveRuntimeDll();
        if (dll is null)
        {
            Status("Runtime non compilato. Vai su Certificazione e premi Compila runtime.");
            return;
        }

        RereadTargetStateButton.IsEnabled = false;
        try
        {
            var result = await RunToolAsync("dotnet", $"\"{dll}\" --hud-probe", "Rileggi stato bersaglio", pairing: false);

            string[] lines = result.Output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            int unknownIndex = Array.FindIndex(lines, line => line.Contains("UNKNOWN", StringComparison.OrdinalIgnoreCase));
            if (unknownIndex >= 0)
            {
                TargetRoiSummary.Text = string.Join(Environment.NewLine, lines.Skip(unknownIndex));
            }
            else if (lines.Length > 0)
            {
                TargetRoiSummary.Text = string.Join(Environment.NewLine, lines);
            }
            else
            {
                TargetRoiSummary.Text = "Nessuna risposta dal runtime.";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("Errore durante la lettura dello stato del bersaglio.", ex);
            TargetRoiSummary.Text = $"Fallito: {ex.Message}";
        }
        finally
        {
            RereadTargetStateButton.IsEnabled = true;
        }
    }
}
