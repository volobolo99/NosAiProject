using System;
using System.Globalization;
using System.IO;
using System.Windows;

namespace NosAi.ControlPanel;

public partial class MainWindow
{
    private async void OnCheckKeybinds(object sender, RoutedEventArgs e)
    {
        if (_busy) { Status("Un'operazione è già in corso."); return; }
        var dll = ResolveRuntimeDll();
        if (dll is null) { Status("Runtime non compilato. Vai su Certificazione e premi Compila runtime."); return; }

        CheckKeybindsButton.IsEnabled = false;
        try
        {
            CombatChainElevation.Text = CombatChainCard.DescribeElevation(ElevationInspect.IsElevated());
            var result = await RunToolAsync(dll, "--keybinds-check", "Controllo keybinds abilità", false);
            CombatChainOutput.Text = result.Output;
            CombatChainSummary.Text = "Sotto serve l'intento skill.<id> confermato dal report, non il vnum.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("Esecuzione di --keybinds-check fallita.", ex);
            CombatChainSummary.Text = $"Fallito: {ex.Message}";
        }
        finally
        {
            CheckKeybindsButton.IsEnabled = true;
        }
    }

    private async void OnCombatReport(object sender, RoutedEventArgs e)
    {
        if (_busy) { Status("Un'operazione è già in corso."); return; }
        var dll = ResolveRuntimeDll();
        if (dll is null) { Status("Runtime non compilato. Vai su Certificazione e premi Compila runtime."); return; }

        CombatReportButton.IsEnabled = false;
        try
        {
            var result = await RunToolAsync(dll, "--combat-report", "Report di combattimento", false);
            CombatChainOutput.Text = result.Output;
            CombatChainSummary.Text = CombatChainCard.SummariseCombatReport(result.Output);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("Esecuzione di --combat-report fallita.", ex);
            CombatChainSummary.Text = $"Fallito: {ex.Message}";
        }
        finally
        {
            CombatReportButton.IsEnabled = true;
        }
    }

    private async void OnEngageTarget(object sender, RoutedEventArgs e)
    {
        if (_busy) { Status("Un'operazione è già in corso."); return; }
        var dll = ResolveRuntimeDll();
        if (dll is null) { Status("Runtime non compilato. Vai su Certificazione e premi Compila runtime."); return; }

        if (!CombatChainCard.TryValidateEngage(EngageTargetId.Text, EngageSkillId.Text, EngageRounds.Text, out int rounds, out string? refusal))
        {
            CombatChainSummary.Text = refusal!;
            return;
        }

        EngageTargetButton.IsEnabled = false;
        try
        {
            CombatChainOutput.Text = string.Empty;
            var arguments = string.Format(
                CultureInfo.InvariantCulture,
                "--engage {0} {1} --watch {2}",
                EngageTargetId.Text.Trim(),
                EngageSkillId.Text.Trim(),
                rounds);
            await RunToolAsync(dll, arguments, "Engage bersaglio con watch", false, line => CombatChainOutput.AppendText(line + Environment.NewLine));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("Esecuzione di --engage fallita.", ex);
            CombatChainSummary.Text = $"Fallito: {ex.Message}";
        }
        finally
        {
            EngageTargetButton.IsEnabled = true;
        }
    }
}
