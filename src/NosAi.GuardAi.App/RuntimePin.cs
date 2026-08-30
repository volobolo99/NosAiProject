namespace NosAi.GuardAi.App;

/// <summary>
/// The runtime's public key, pinned on this device at USB pairing.
/// </summary>
/// <remarks>
/// <para>
/// Wire version 2 requires the phone to verify the runtime before it signs
/// anything. Without this file the handshake is fail-closed: connecting would
/// otherwise mean signing a transcript for whoever answered discovery first.
/// </para>
/// <para>
/// The file is written by <c>python -m nosai.phone.deploy</c> into app-private
/// storage. It is the public half only; losing it costs one re-pair, not a
/// leaked identity.
/// </para>
/// </remarks>
public static class RuntimePin
{
    public const string FileName = "runtime_public.pem";

    public static string? Load(string? directory = null)
    {
        var folder = directory ?? FileSystem.AppDataDirectory;
        var path = Path.Combine(folder, FileName);
        if (!File.Exists(path))
            return null;

        try
        {
            var pem = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(pem) || !pem.Contains("BEGIN PUBLIC KEY", StringComparison.Ordinal))
                return null;
            return pem;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
