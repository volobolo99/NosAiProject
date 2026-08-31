using System.Security.Cryptography;
using NosAi.Runtime.Gate1;

namespace NosAi.GuardClient;

/// <summary>
/// Whatever holds this device's private key and can sign the handshake with it.
/// </summary>
/// <remarks>
/// <para>
/// The client used to take an <see cref="RSA"/> directly, which quietly assumed
/// the private key could be loaded into process memory. A key generated inside
/// Android's Keystore cannot be (ADR-0010), and that is the point of putting it
/// there — so the client asks for a signature instead of asking for the key.
/// </para>
/// <para>
/// Signing is over the transcript <b>message</b>, not a digest computed
/// beforehand: a hardware store hashes the message itself. The result is
/// byte-identical to <c>SignHash</c> over <see cref="SessionTranscript.Compute"/>,
/// so the runtime verifies it unchanged and the wire contract is untouched.
/// </para>
/// </remarks>
public interface IDeviceSigner
{
    /// <summary>The public half, for the runtime to enroll. Never the private half.</summary>
    string PublicKeyPem { get; }

    /// <summary>
    /// Where the private key actually lives, for the operator to see.
    /// </summary>
    /// <remarks>
    /// Reported rather than assumed. A device that could not give hardware custody
    /// must say so plainly instead of looking protected, which would be worse than
    /// the readable file it replaced.
    /// </remarks>
    DeviceKeyCustody Custody { get; }

    /// <summary>
    /// Signs the transcript message with RSASSA-PKCS1-v1_5 over SHA-256.
    /// </summary>
    byte[] Sign(ReadOnlySpan<byte> message);
}

/// <summary>How well the device's private key is protected.</summary>
public enum DeviceKeyCustody
{
    /// <summary>Not established. Never treated as protected.</summary>
    Unknown = 0,

    /// <summary>A file in app-private storage: readable with root or a backup.</summary>
    AppPrivateFile = 1,

    /// <summary>Generated inside the platform key store and non-exportable.</summary>
    PlatformKeyStore = 2
}

/// <summary>
/// An <see cref="IDeviceSigner"/> over an in-process RSA key.
/// </summary>
/// <remarks>
/// Used by the reference client and the tests, and by the phone only when the
/// platform key store is unavailable — in which case the custody it reports says
/// so.
/// </remarks>
public sealed class RsaDeviceSigner : IDeviceSigner
{
    private readonly RSA _key;
    private readonly bool _ownsKey;

    /// <param name="key">
    /// Not disposed unless <paramref name="ownsKey"/> says so: on a phone the key
    /// is owned by whatever created it and must outlive any single session.
    /// </param>
    public RsaDeviceSigner(RSA key, DeviceKeyCustody custody = DeviceKeyCustody.AppPrivateFile, bool ownsKey = false)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.KeySize != 2048)
            throw new ArgumentException("Gate 1 accepts RSA-2048 keys only.", nameof(key));
        _key = key;
        _ownsKey = ownsKey;
        Custody = custody;
    }

    public string PublicKeyPem => _key.ExportSubjectPublicKeyInfoPem();

    public DeviceKeyCustody Custody { get; }

    public byte[] Sign(ReadOnlySpan<byte> message)
        => _key.SignData(message, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

    public void Dispose()
    {
        if (_ownsKey)
            _key.Dispose();
    }
}
