#if ANDROID
using System.Security.Cryptography;
using Android.OS;
using Android.Runtime;
using Android.Security.Keystore;
using Java.Security;
using NosAi.GuardClient;

namespace NosAi.GuardAi.App;

/// <summary>
/// A device identity generated inside the Android Keystore (ADR-0010).
/// </summary>
/// <remarks>
/// <para>
/// The private key is created by the Keystore and never enters this process.
/// Signing is performed by the Keystore, so a copy of the app's data — a backup,
/// or a rooted read — yields nothing that can impersonate this device.
/// </para>
/// <para>
/// Generated inside, never imported. An imported key is software-backed on many
/// devices, which would give the appearance of hardware custody without the
/// substance; appearing safer than you are is worse than the readable file this
/// replaces.
/// </para>
/// <para>
/// Signing uses <c>SHA256withRSA</c>, which hashes the message itself. That is
/// why the client signs the transcript <i>message</i> rather than a digest it
/// computed: a Keystore key cannot be handed a pre-computed hash. The resulting
/// signature is byte-identical to the one the runtime already verifies.
/// </para>
/// </remarks>
public sealed class KeystoreDeviceSigner : IDeviceSigner, IDisposable
{
    /// <summary>Keystore alias for the Guard AI device identity.</summary>
    /// <remarks>
    /// Changing it produces a new identity and requires re-pairing every runtime
    /// this device is enrolled with.
    /// </remarks>
    public const string Alias = "nosai.guard.device.v1";

    private const string Provider = "AndroidKeyStore";
    private const string SignatureAlgorithm = "SHA256withRSA";

    /// <summary>
    /// The Keystore API this needs. <c>KeyGenParameterSpec</c> arrived in API 23;
    /// the app's minimum is 21, so the level is checked rather than assumed.
    /// </summary>
    /// <remarks>
    /// Checked with <c>OperatingSystem.IsAndroidVersionAtLeast</c>
    /// rather than by comparing <c>Build.VERSION.SdkInt</c>, because the platform
    /// analyzer understands the former: it then proves the guard covers every
    /// API-23 call below, instead of leaving that to review.
    /// </remarks>
    private const int RequiredApi = 23;

    private readonly IPrivateKey _privateKey;
    private bool _disposed;

    private KeystoreDeviceSigner(IPrivateKey privateKey, string publicKeyPem)
    {
        _privateKey = privateKey;
        PublicKeyPem = publicKeyPem;
    }

    public string PublicKeyPem { get; }

    public DeviceKeyCustody Custody => DeviceKeyCustody.PlatformKeyStore;

    /// <summary>
    /// Returns a Keystore-backed signer, or null with a reason when this device
    /// cannot provide one.
    /// </summary>
    /// <remarks>
    /// Null rather than an exception: the caller has a decision to make about
    /// falling back, and the reason is what the operator needs to see. Nothing
    /// here reports success it did not get.
    /// </remarks>
    public static KeystoreDeviceSigner? TryLoadOrCreate(out string? unavailableReason)
    {
        unavailableReason = null;

        if (!OperatingSystem.IsAndroidVersionAtLeast(RequiredApi))
        {
            unavailableReason = $"keystore_requires_api_{RequiredApi}_device_has_{(int)Build.VERSION.SdkInt}";
            return null;
        }

        try
        {
            var store = KeyStore.GetInstance(Provider);
            if (store is null)
            {
                unavailableReason = "keystore_provider_missing";
                return null;
            }

            store.Load(null, null);

            if (!store.ContainsAlias(Alias))
                GenerateGuarded();

            // Re-read through the store either way, so the loaded key is always the
            // stored one rather than something held from generation.
            var storedKey = store.GetKey(Alias, null);
            if (storedKey is null)
            {
                unavailableReason = "keystore_private_key_missing";
                return null;
            }

            // A C# type test is not enough, and this is where the whole ADR-0010
            // custody quietly fell back to a file on a real device. An AndroidKeyStore
            // RSA key is android.security.keystore2.AndroidKeyStoreRSAPrivateKey, a
            // class with no managed binding, so .NET Android wraps it in a generic
            // proxy whose managed type implements nothing in particular:
            // `is IPrivateKey` answers false about an object that is a private key.
            // JavaCast builds the interface invoker over the same Java instance
            // instead of interrogating the proxy's C# type.
            IPrivateKey privateKey;
            try
            {
                privateKey = storedKey.JavaCast<IPrivateKey>();
            }
            catch (InvalidCastException)
            {
                // Name the class that turned up, so the next device that fails here
                // says why instead of repeating an unfalsifiable "unavailable".
                string actual = (storedKey as Java.Lang.Object)?.Class?.Name ?? "unknown";
                unavailableReason = $"keystore_private_key_unavailable:{actual}";
                return null;
            }

            var certificate = store.GetCertificate(Alias);
            if (certificate?.PublicKey?.GetEncoded() is not byte[] encoded)
            {
                unavailableReason = "keystore_public_key_unavailable";
                return null;
            }

            // SubjectPublicKeyInfo DER, re-exported through the BCL so the PEM is
            // formatted exactly as the enrollment tool and the runtime expect.
            using var publicKey = RSA.Create();
            publicKey.ImportSubjectPublicKeyInfo(encoded, out _);
            if (publicKey.KeySize != 2048)
            {
                unavailableReason = $"keystore_key_size_{publicKey.KeySize}";
                return null;
            }

            return new KeystoreDeviceSigner(privateKey, publicKey.ExportSubjectPublicKeyInfoPem());
        }
        catch (Java.Lang.Exception ex)
        {
            unavailableReason = $"keystore_failed:{ex.GetType().Name}";
            return null;
        }
        catch (Exception ex) when (ex is CryptographicException or NotSupportedException or InvalidOperationException)
        {
            unavailableReason = $"keystore_failed:{ex.GetType().Name}";
            return null;
        }
    }

    /// <summary>Calls <see cref="Generate"/> behind the version guard.</summary>
    /// <remarks>
    /// The check is repeated here so the analyzer can see it: it does not track
    /// the guard across the call site above, and an unverified guard is exactly
    /// what CA1416 exists to catch.
    /// </remarks>
    private static void GenerateGuarded()
    {
        // Written as a positive guard around the call, not an early throw: that is
        // the shape the platform analyzer recognises, so it verifies the guard
        // instead of taking it on trust.
        if (OperatingSystem.IsAndroidVersionAtLeast(RequiredApi))
            Generate();
        else
            throw new InvalidOperationException($"keystore_requires_api_{RequiredApi}");
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android23.0")]
    private static void Generate()
    {
        var generator = KeyPairGenerator.GetInstance(KeyProperties.KeyAlgorithmRsa, Provider)
            ?? throw new InvalidOperationException("keystore_generator_unavailable");

        // Signature only, SHA-256, PKCS#1 v1.5: exactly what the Gate 1 handshake
        // uses and nothing more. A key that can also decrypt would be a wider
        // capability than this identity needs.
        var spec = new KeyGenParameterSpec.Builder(Alias, KeyStorePurpose.Sign)
            .SetKeySize(2048)!
            .SetDigests(KeyProperties.DigestSha256)!
            .SetSignaturePaddings(KeyProperties.SignaturePaddingRsaPkcs1)!
            // No user authentication requirement: the operator's phone must be able
            // to hold a session while locked, and requiring an unlock would make the
            // channel drop every time the screen turns off.
            .SetUserAuthenticationRequired(false)!
            .Build();

        generator.Initialize(spec);
        generator.GenerateKeyPair();
    }

    public byte[] Sign(ReadOnlySpan<byte> message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var signature = Signature.GetInstance(SignatureAlgorithm)
            ?? throw new InvalidOperationException("keystore_signature_unavailable");

        signature.InitSign(_privateKey);
        signature.Update(message.ToArray());
        return signature.Sign()
            ?? throw new InvalidOperationException("keystore_signature_empty");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        // The key belongs to the Keystore; disposing the handle releases the local
        // reference and leaves the stored key where it is.
        _privateKey.Dispose();
    }
}
#endif
