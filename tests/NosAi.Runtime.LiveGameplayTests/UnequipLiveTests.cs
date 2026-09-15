using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NosAi.ControlPanel;
using NosAi.Core.WorldModel;
using NosAi.LiveIntegration;
using NosAi.Runtime.Tactical;
using Xunit;

namespace NosAi.Runtime.LiveGameplayTests;

// Esegue un'azione REALE sul personaggio connesso (toglie l'arma equipaggiata):
// non e' un test contro un doppio, e' NosAiProject che agisce nel gioco vero.
// Riusa per intero la catena di produzione gia' autorizzata da ADR-0003
// (UnequipCommand --unequip -> GatedInputBackend -> CommitPointValidator ->
// ActuationAuthority.Commanded -> UnequipExecutor): nessuna nuova via di
// attuazione, nessun bypasso del gate.
public sealed class UnequipLiveTests
{
    [Fact]
    public async Task Unequip_EmitsAndVerifiesViaWire()
    {
        string? repoRoot = FindRepoRoot();
        Assert.True(repoRoot is not null,
            "Radice del repository non trovata risalendo da AppContext.BaseDirectory.");

        bool clientRunning = RealClientConnector.DefaultProcessNames
            .Any(name => Process.GetProcessesByName(name).Length > 0);
        Assert.True(clientRunning,
            $"Nessun client NosTale in esecuzione (nomi attesi: {string.Join(", ", RealClientConnector.DefaultProcessNames)}). " +
            "Apri il gioco e accedi con un personaggio prima di questo test: NosAiProject clicca da solo, " +
            "ma non puo' avviare e autenticare il client al posto tuo.");

        string dllPath = Path.Combine(repoRoot!, "src", "NosAi.Runtime", "bin", "Release", "net8.0-windows", "NosAi.Runtime.dll");
        Assert.True(File.Exists(dllPath),
            $"Runtime non compilato in Release: {dllPath} non esiste. Esegui prima 'dotnet build src/NosAi.Runtime -c Release'.");

        string arguments = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"\"{dllPath}\" {UnequipCommand.Flag} {EquipmentSlot.Weapon} {UnequipCommand.GestureOption} single {UnequipCommand.ArmInputOption}");
        ToolResult result = await ToolRunner.RunAsync("dotnet", arguments, repoRoot!);

        Assert.True(result.Output.Contains("scope: emitted", StringComparison.Ordinal),
            "Il click autonomo non e' stato emesso: il comando si e' rifiutato prima o al posto dell'azione. " +
            $"Codice di uscita {result.ExitCode}. Output:\n{result.Output}\n" +
            "Cause comuni: terminale non elevato (WinDivert richiede amministratore), calibrazione del pannello " +
            "inventario assente (InventoryPanelRoiCalibration), finestra del client non trovata o non in primo piano.");
    }

    private static string? FindRepoRoot()
    {
        string? current = AppContext.BaseDirectory;
        while (current is not null && !File.Exists(Path.Combine(current, "NosAi.sln")))
        {
            current = Directory.GetParent(current)?.FullName;
        }
        return current;
    }
}
