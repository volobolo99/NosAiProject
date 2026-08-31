using System.Security.Cryptography;
using NosAi.GuardClient;

namespace NosAi.GuardAi.App;

/// <summary>
/// The device's RSA-2048 identity: created once, reused on every launch.
/// </summary>
/// <remarks>
/// <para>
/// The key used to be generated per launch and held only in memory, so every
/// start produced a new identity and required re-enrolling on the PC. It is now
/// persisted and reused.
/// </para>
/// <para>
/// <b>Custody (ADR-0010).</b> The identity is generated inside the Android
/// Keystore when the device can, so the private key never enters this process and
/// cannot be exported. Where the Keystore is unavailable the key falls back to a
/// file in app-private storage — readable with root or a backup — and the
/// fallback is <b>reported, never hidden</b>: <see cref="IDeviceSigner.Custody"/>
/// says which one was obtained, and the reason is available for the operator to
/// read. A silent fallback would make the phone look protected when it is not,
/// which is worse than the plain file it replaced.
/// </para>
/// <para>
/// The public half is written to the log at startup so the enrollment tool can
/// collect it over ADB (<c>python -m nosai.phone.enroll</c>). Only the public key
/// is logged: it is the half the PC is meant to hold, and publishing it grants
/// nothing — the runtime can verify a signature with it but never produce one.
/// </para>
/// </remarks>
public static class DeviceIdentity
{
    /// <summary>Logcat tag the enrollment tool filters on.</summary>
    public const string LogTag = "NosAiGuardKey";

    private const string KeyFileName = "guard_device_key.pem";

    /// <summary>
    /// Why the platform key store was not used, or null when it was.
    /// </summary>
    /// <remarks>
    /// Set by <see cref="LoadOrCreateSigner"/>. Kept so the operator screen can
    /// state the custody it actually got rather than the one it hoped for.
    /// </remarks>
    public static string? KeyStoreUnavailableReason { get; private set; }

    /// <summary>
    /// Returns the device signer, creating the identity on first use.
    /// </summary>
    /// <param name="directory">
    /// Where the fallback file lives. Ignored when the Keystore is used, which is
    /// the point: there is no file to place.
    /// </param>
    public static IDeviceSigner LoadOrCreateSigner(string? directory = null)
    {
#if ANDROID
        var keystore = KeystoreDeviceSigner.TryLoadOrCreate(out string? reason);
        KeyStoreUnavailableReason = reason;
        if (keystore is not null)
            return keystore;
#else
        KeyStoreUnavailableReason = "keystore_not_available_off_android";
#endif
        return new RsaDeviceSigner(LoadOrCreateFileKey(directory), DeviceKeyCustody.AppPrivateFile, ownsKey: true);
    }

    /// <summary>
    /// The file-backed identity, used only where the platform key store is not.
    /// </summary>
    /// <remarks>
    /// Left intact so a device that cannot reach the Keystore still pairs and
    /// still works. It is the pre-ADR-0010 custody, and it is labelled as such.
    /// </remarks>
    public static RSA LoadOrCreateFileKey(string? directory = null)
    {
        var folder = directory ?? FileSystem.AppDataDirectory;
        var path = Path.Combine(folder, KeyFileName);
        var key = RSA.Create(2048);

        if (File.Exists(path))
        {
            try
            {
                key.ImportFromPem(File.ReadAllText(path));
                if (key.KeySize == 2048)
                    return key;

                // A key of the wrong size would be refused by the runtime with an
                // opaque failure at handshake time. Replace it now instead.
                key.Dispose();
                key = RSA.Create(2048);
            }
            catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
            {
                // An unreadable or corrupt key file must not brick the app: a fresh
                // identity costs one re-enrollment, which the operator can see and act on.
                key.Dispose();
                key = RSA.Create(2048);
            }
        }

        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(path, key.ExportRSAPrivateKeyPem());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Persisting failed; the session still works, it just will not survive a
            // restart. Better than refusing to run.
        }

        return key;
    }

    /// <summary>
    /// Writes the public key to the device log for ADB-based enrollment.
    /// </summary>
    /// <remarks>
    /// Logcat truncates long lines, so the PEM goes out one line at a time between
    /// explicit markers and the enrollment tool reassembles it. The custody is
    /// logged beside it so a pairing run records which one this device gave.
    /// </remarks>
    public static void PublishPublicKey(IDeviceSigner signer)
    {
        var pem = signer.PublicKeyPem;
        var custody = $"custody={signer.Custody}" +
                      (KeyStoreUnavailableReason is null ? "" : $" reason={KeyStoreUnavailableReason}");
#if ANDROID
        Android.Util.Log.Info(LogTag, custody);
        Android.Util.Log.Info(LogTag, "BEGIN_NOSAI_GUARD_PUBLIC_KEY");
        foreach (var line in pem.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
            Android.Util.Log.Info(LogTag, line);
        Android.Util.Log.Info(LogTag, "END_NOSAI_GUARD_PUBLIC_KEY");
#else
        System.Diagnostics.Debug.WriteLine($"[{LogTag}] {custody}");
        System.Diagnostics.Debug.WriteLine($"[{LogTag}] {pem}");
#endif
    }
}
