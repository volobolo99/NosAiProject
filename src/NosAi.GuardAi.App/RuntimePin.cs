using System.Security.Cryptography;

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
/// The pin lives in app-private storage. Pairing cannot write there directly on a
/// release build — <c>adb run-as</c> only works on a debuggable package, which
/// this is not — so the key is dropped in the app's external files directory and
/// adopted from there on the next launch. That inbox is the only part of this a
/// non-debuggable device makes awkward, and it holds a public key: losing it costs
/// one re-pair, and reading it grants nothing.
/// </para>
/// </remarks>
public static class RuntimePin
{
    public const string FileName = "runtime_public.pem";

    /// <summary>
    /// Returns the pinned runtime key, adopting one left by pairing if present.
    /// </summary>
    /// <remarks>
    /// The inbox copy is consumed on adoption: leaving it would let a later
    /// pairing run be silently overridden by a stale file, and the durable copy in
    /// private storage is the one the app is meant to trust.
    /// </remarks>
    public static string? Load(string? directory = null, string? inboxDirectory = null)
    {
        string folder = directory ?? FileSystem.AppDataDirectory;
        string path = Path.Combine(folder, FileName);

        string? adopted = AdoptFromInbox(folder, inboxDirectory);
        if (adopted is not null)
            return adopted;

        return ReadPem(path);
    }

    /// <summary>Stores a key as the pinned runtime identity.</summary>
    public static bool Save(string pem, string? directory = null)
    {
        if (!IsUsablePublicKey(pem))
            return false;

        try
        {
            string folder = directory ?? FileSystem.AppDataDirectory;
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, FileName), pem);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Where pairing drops the key, since it cannot reach private storage.
    /// </summary>
    /// <remarks>
    /// Null when the platform exposes no external files directory, in which case
    /// there is simply no inbox and the private copy is the only source.
    /// </remarks>
    public static string? InboxDirectory
    {
        get
        {
#if ANDROID
            return Android.App.Application.Context.GetExternalFilesDir(null)?.AbsolutePath;
#else
            return null;
#endif
        }
    }

    private static string? AdoptFromInbox(string privateFolder, string? inboxDirectory)
    {
        string? inbox = inboxDirectory ?? InboxDirectory;
        if (string.IsNullOrEmpty(inbox))
            return null;

        string incoming = Path.Combine(inbox, FileName);
        string? pem = ReadPem(incoming);
        if (pem is null)
            return null;

        try
        {
            Directory.CreateDirectory(privateFolder);
            File.WriteAllText(Path.Combine(privateFolder, FileName), pem);
            File.Delete(incoming);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Adoption failed, but the key is readable and valid: use it for this
            // session rather than refusing to connect over a storage problem.
        }

        return pem;
    }

    private static string? ReadPem(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            string pem = File.ReadAllText(path);
            return IsUsablePublicKey(pem) ? pem : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether the text is an RSA-2048 public key this channel can use.
    /// </summary>
    /// <remarks>
    /// Parsed rather than pattern-matched. A truncated or corrupt file would pass a
    /// header check and then fail at handshake time as "runtime not recognised",
    /// sending the operator to re-pair a device that was never the problem.
    /// </remarks>
    private static bool IsUsablePublicKey(string? pem)
    {
        if (string.IsNullOrWhiteSpace(pem) || !pem.Contains("BEGIN PUBLIC KEY", StringComparison.Ordinal))
            return false;

        try
        {
            using var key = RSA.Create();
            key.ImportFromPem(pem);
            return key.KeySize == 2048;
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return false;
        }
    }
}
