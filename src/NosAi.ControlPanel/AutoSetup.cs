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
    public static IReadOnlyList<SetupItem> Inspect(string repoRoot)
    {
        var python = ToolRunner.FindPython();
        var runtimeDll = Path.Combine(AppContext.BaseDirectory, "NosAi.Runtime.dll");
        if (!File.Exists(runtimeDll))
            runtimeDll = Path.Combine(repoRoot, "src", "NosAi.Runtime", "bin", "Release", "net8.0-windows", "NosAi.Runtime.dll");

        var phoneKey = Path.Combine(repoRoot, Gate1HostOptions.DefaultTrustedKeyPath);
        var identity = Path.Combine(repoRoot, RuntimeIdentity.DefaultPath);
        var pin = Path.Combine(repoRoot, RuntimeIdentity.DefaultPublicPath);
        var data = Path.Combine(repoRoot, "data");

        return
        [
            new SetupItem("Cartella dati", Directory.Exists(data), data, Directory.Exists(data) ? null : "Verrà creata all'avvio."),
            new SetupItem("Runtime", File.Exists(runtimeDll), File.Exists(runtimeDll) ? runtimeDll : "non compilato",
                File.Exists(runtimeDll) ? null : "Premere Compila runtime."),
            new SetupItem("Python (telefono)", python is not null, python ?? "non trovato nel PATH",
                python is null ? "Serve per abbinare il telefono." : null),
            new SetupItem("Chiave telefono", File.Exists(phoneKey),
                File.Exists(phoneKey) ? phoneKey : Gate1HostOptions.DefaultTrustedKeyPath,
                File.Exists(phoneKey) ? null : "Premere Abbina telefono con il cavo USB."),
            new SetupItem("Identità runtime", File.Exists(identity) || File.Exists(pin),
                File.Exists(pin) ? pin : (File.Exists(identity) ? identity : RuntimeIdentity.DefaultPublicPath),
                File.Exists(identity) || File.Exists(pin) ? null : "Si crea da sola al primo avvio del runtime.")
        ];
    }
}
