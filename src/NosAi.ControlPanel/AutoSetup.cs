using System.IO;
using NosAi.Runtime.Configuration;
using NosAi.Runtime.Gate1;

namespace NosAi.ControlPanel;

public sealed record SetupItem(string Name, bool Ready, string Detail, string? ActionHint)
{
    public string StatusLabel => Ready ? "PRONTO" : "DA FARE";
}

/// <summary>What the machine already has, so the operator does not have to guess.</summary>
public static class AutoSetup
{
    public static IReadOnlyList<SetupItem> Inspect(string repoRoot, string? observeGame = null, bool? elevated = null)
    {
        var python = ToolRunner.FindPython();
        var runtimeDll = Path.Combine(AppContext.BaseDirectory, "NosAi.Runtime.dll");
        if (!File.Exists(runtimeDll))
            runtimeDll = Path.Combine(repoRoot, "src", "NosAi.Runtime", "bin", "Release", "net8.0-windows", "NosAi.Runtime.dll");

        var phoneKey = Path.Combine(repoRoot, Gate1HostOptions.DefaultTrustedKeyPath);
        var identity = Path.Combine(repoRoot, RuntimeIdentity.DefaultPath);
        var wrapped = Path.Combine(repoRoot, RuntimeIdentity.DefaultProtectedPath);
        var pin = Path.Combine(repoRoot, RuntimeIdentity.DefaultPublicPath);
        var data = Path.Combine(repoRoot, "data");
        var hasIdentity = File.Exists(pin) || File.Exists(wrapped) || File.Exists(identity);

        var items = new List<SetupItem>
        {
            new SetupItem("Cartella dati", Directory.Exists(data), data, Directory.Exists(data) ? null : "Verrà creata all'avvio."),
            new SetupItem("Runtime", File.Exists(runtimeDll), File.Exists(runtimeDll) ? runtimeDll : "non compilato",
                File.Exists(runtimeDll) ? null : "Premere Compila runtime."),
            new SetupItem("Python (telefono)", python is not null, python ?? "non trovato nel PATH",
                python is null ? "Serve per abbinare il telefono." : null),
            new SetupItem("Chiave telefono", File.Exists(phoneKey),
                File.Exists(phoneKey) ? phoneKey : Gate1HostOptions.DefaultTrustedKeyPath,
                File.Exists(phoneKey) ? null : "Premere Abbina telefono con il cavo USB."),
            new SetupItem("Identità runtime", hasIdentity,
                File.Exists(wrapped) ? wrapped : (File.Exists(pin) ? pin : (File.Exists(identity) ? identity : RuntimeIdentity.DefaultProtectedPath)),
                hasIdentity
                    ? (File.Exists(identity) && !File.Exists(wrapped) ? "Al prossimo avvio il runtime avvolge il PEM in DPAPI." : null)
                    : "Si crea da sola al primo avvio del runtime."),
            new SetupItem("Canale Guard (questo build)", true,
                $"wire {NosAi.Runtime.Gate1.WireHeader.CurrentVersion}. Un APK più vecchio viene rifiutato all'header.",
                "Giro v3 sul telefono ancora aperto: non è Verified. Lo sviluppo continua lo stesso.")
        };

        if (!string.IsNullOrWhiteSpace(observeGame) && elevated == false)
        {
            items.Add(new SetupItem(
                "Cattura traffico (WinDivert)",
                false,
                "Questa console non è elevata: WinDivert restituirà access_denied_run_elevated e l'osservazione del gioco non partirà.",
                "Riavviare il Control Panel come amministratore prima di accendere --observe-game."));
        }

        return items;
    }
}
