using System;
using System.Globalization;
using System.IO;
using System.Windows;

namespace NosAi.ControlPanel;

public partial class MainWindow
{
    private void OnReloadKeybinds(object sender, RoutedEventArgs e)
    {
        if (_busy) { Status("Un'operazione è già in corso."); return; }

        KeybindReloadButton.IsEnabled = false;
        try
        {
            string path = Path.Combine(WorkspaceLocator.Find(), KeybindConfirmCard.RelativePath);
            if (!File.Exists(path))
            {
                KeybindSummary.Text = $"{KeybindConfirmCard.RelativePath} non esiste: non c'è alcun keybind da mostrare.";
                return;
            }

            string json = File.ReadAllText(path);
            KeybindOutput.Text = KeybindConfirmCard.Describe(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("Lettura di data/keybinds.json fallita.", ex);
            KeybindSummary.Text = $"Fallito: {ex.Message}";
        }
        finally
        {
            KeybindReloadButton.IsEnabled = true;
        }
    }

    private void OnConfirmKeybind(object sender, RoutedEventArgs e)
    {
        if (_busy) { Status("Un'operazione è già in corso."); return; }

        KeybindConfirmButton.IsEnabled = false;
        try
        {
            string path = Path.Combine(WorkspaceLocator.Find(), KeybindConfirmCard.RelativePath);
            if (!File.Exists(path))
            {
                KeybindSummary.Text = $"{KeybindConfirmCard.RelativePath} non esiste: nessun keybind da confermare.";
                return;
            }

            string json = File.ReadAllText(path);
            if (!KeybindConfirmCard.TryConfirm(
                    json,
                    KeybindIntent.Text,
                    KeybindVirtualKey.Text,
                    KeybindObservation.Text,
                    out string updatedJson,
                    out string? refusal))
            {
                // Un rifiuto non tocca il disco: il motivo dice qual è il campo colpevole.
                KeybindSummary.Text = refusal ?? "Conferma rifiutata.";
                return;
            }

            string intent = KeybindIntent.Text.Trim();
            string observation = KeybindObservation.Text.Trim();
            if (!int.TryParse(KeybindVirtualKey.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int virtualKey))
            {
                KeybindSummary.Text = "Tasto non valido: impossibile riepilogare la conferma.";
                return;
            }

            File.WriteAllText(path, updatedJson);

            KeybindSummary.Text = KeybindConfirmCard.SummariseConfirmation(intent, virtualKey, observation);
            _log.Operator($"Keybind confermato: {intent} sul tasto {virtualKey}. Osservazione: {observation}.");
            KeybindOutput.Text = KeybindConfirmCard.Describe(updatedJson);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("Salvataggio della conferma keybind fallito.", ex);
            KeybindSummary.Text = $"Fallito: {ex.Message}";
        }
        finally
        {
            KeybindConfirmButton.IsEnabled = true;
        }
    }
}
