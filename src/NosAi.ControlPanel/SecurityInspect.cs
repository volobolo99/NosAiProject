using System.IO;
using NosAi.Runtime.Configuration;
using NosAi.Runtime.Gate1;

namespace NosAi.ControlPanel;

/// <summary>
/// Observes identity files and this build. Does not wrap keys: ADR-0010 lives in RuntimeIdentity.
/// </summary>
internal static class SecurityInspect
{
    public static IReadOnlyList<DisplayField> Inspect(string repoRoot)
    {
        var plaintext = Path.Combine(repoRoot, RuntimeIdentity.DefaultPath);
        var pin = Path.Combine(repoRoot, RuntimeIdentity.DefaultPublicPath);
        var wrapped = Path.Combine(repoRoot, RuntimeIdentity.DefaultProtectedPath);
        var phone = Path.Combine(repoRoot, Gate1HostOptions.DefaultTrustedKeyPath);

        var custody = File.Exists(wrapped)
            ? "Privata runtime in DPAPI (account Windows). Non è un TPM. Telefono: Keystore lato app."
            : File.Exists(plaintext)
                ? "PEM in chiaro ancora presente: il runtime la avvolge al prossimo avvio (ADR-0010)."
                : "Nessuna identità privata. Si crea al primo avvio del runtime.";

        return
        [
            new DisplayField("Wire (questo build)", ChannelView.WireLabel, "DERIVED"),
            new DisplayField("Identità runtime (PEM)", File.Exists(plaintext) ? plaintext : "assente", File.Exists(plaintext) ? "LIVE" : "UNKNOWN"),
            new DisplayField("Identità DPAPI", File.Exists(wrapped) ? wrapped : "assente", File.Exists(wrapped) ? "LIVE" : "UNKNOWN"),
            new DisplayField("Pin pubblico", File.Exists(pin) ? pin : "assente", File.Exists(pin) ? "LIVE" : "UNKNOWN"),
            new DisplayField("Chiave telefono (pubblica)", File.Exists(phone) ? phone : "assente — abbinare via USB", File.Exists(phone) ? "LIVE" : "UNKNOWN"),
            new DisplayField("Custodia", custody, "DERIVED"),
            new DisplayField("Esecuzione", "disabilitata in Gate 1 (Safety, non questa console)", "DERIVED")
        ];
    }
}
