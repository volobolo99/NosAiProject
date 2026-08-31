using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate1;

namespace NosAi.ControlPanel;

/// <summary>
/// Operator-facing channel facts derived from this build and the existing Guard
/// snapshot. Does not invent a wire version from the network: the snapshot has
/// no such field. Phone verification stays a reminder, not a green light.
/// </summary>
internal static class ChannelView
{
    public static string WireLabel { get; } = $"v{WireHeader.CurrentVersion}";

    public const string WireHint =
        "Questo build parla wire v3 (ADR-0009). Un APK v2 viene rifiutato all'header. Non è una lettura dal filo.";

    public const string PhoneReminder =
        "PROMEMORIA TELEFONO (ancora aperto): wire v3 non è stato ripetuto sul dispositivo fisico. " +
        "Reinstallare l'app (Abbina telefono), poi USB e Wi-Fi. Non è Verified. Lo sviluppo continua lo stesso.";

    public static (string Label, string Hint) Slot(bool? connected, bool? authenticated, string? termination)
    {
        if (connected is null && authenticated is null)
            return ("UNKNOWN", "Collegato/autenticato assenti o UNKNOWN nello snapshot.");

        if (connected == true && authenticated == true)
            return ("SESSIONE AUTENTICATA", "Il telefono occupa lo slot dopo handshake riuscito.");

        if (connected == true)
            return ("SLOT OCCUPATO",
                "Un peer è collegato ma non autenticato. Su LAN può escludere il telefono abbinato (una sessione).");

        if (!string.IsNullOrWhiteSpace(termination))
            return ("SLOT LIBERO", $"Nessuna sessione. Ultima chiusura: {termination}");

        return ("SLOT LIBERO", "Nessun peer sul canale Guard.");
    }

    public static bool? Flag(ClassifiedValue<bool> value) => value.HasValue ? value.Value : null;

    public static string? Text(ClassifiedValue<string?> value)
        => value.HasValue && !string.IsNullOrWhiteSpace(value.Value) ? value.Value : null;
}
