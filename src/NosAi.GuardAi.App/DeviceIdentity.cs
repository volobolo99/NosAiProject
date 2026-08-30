using System.Security.Cryptography;

namespace NosAi.GuardAi.App;

/// <summary>
/// The device's RSA-2048 identity: created once, reused on every launch.
/// </summary>
/// <remarks>
/// <para>
/// The key used to be generated per launch and held only in memory, so every
/// start produced a new identity and required re-enrolling on the PC. It is now
/// persisted in app-private storage, which survives relaunches.
/// </para>
/// <para>
/// App-private storage is not the Android Key Store: the private key is readable
/// by anything with root or a backup of the app's data, and it is not
/// hardware-backed. That remains an open limitation, recorded in README.md, and
/// this class is the seam where a Key Store-backed implementation replaces it.
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

    public static RSA LoadOrCreate(string? directory = null)
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
    /// explicit markers and the enrollment tool reassembles it.
    /// </remarks>
    public static void PublishPublicKey(RSA key)
    {
        var pem = key.ExportSubjectPublicKeyInfoPem();
#if ANDROID
        Android.Util.Log.Info(LogTag, "BEGIN_NOSAI_GUARD_PUBLIC_KEY");
        foreach (var line in pem.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
            Android.Util.Log.Info(LogTag, line);
        Android.Util.Log.Info(LogTag, "END_NOSAI_GUARD_PUBLIC_KEY");
#else
        System.Diagnostics.Debug.WriteLine($"[{LogTag}] {pem}");
#endif
    }
}
