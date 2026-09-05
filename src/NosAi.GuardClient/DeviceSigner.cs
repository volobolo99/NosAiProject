using System.Security.Cryptography;

namespace NosAi.GuardClient;

/// <summary>
/// Where a device's long-term Gate 1 identity key lives (ADR-0010), reported for
/// the operator rather than assumed by anything that signs with it.
/// </summary>
public enum DeviceKeyCustody
{
    /// <summary>
    /// Generated inside the Android Keystore; the private key never enters process
    /// memory and signing is performed by the keystore itself.
    /// </summary>
    PlatformKeyStore,

    /// <summary>
    /// A plain file in app-private storage, used only where the platform key store
    /// is unavailable on this device.
    /// </summary>
    AppPrivateFile,

    /// <summary>
    /// An <see cref="RSA"/> instance handed in directly, with no persistence of its
    /// own. Nothing here wrote the key to disk or a key store, so nothing here can
    /// claim one of the custodies above; this is the PC-side test/tooling path, not
    /// a phone identity.
    /// </summary>
    InMemory
}

/// <summary>
/// Signs the Gate 1 handshake transcript on behalf of a device identity
/// (ADR-0006, ADR-0010).
/// </summary>
/// <remarks>
/// The client asks for a signature rather than for the private key itself, so a
/// key that cannot leave a platform key store
/// (<see cref="DeviceKeyCustody.PlatformKeyStore"/>) never has to.
/// </remarks>
public interface IDeviceSigner
{
    /// <summary>The device's public key, PEM-encoded (SubjectPublicKeyInfo), for enrollment.</summary>
    string PublicKeyPem { get; }

    /// <summary>Where the private half of the key actually lives.</summary>
    DeviceKeyCustody Custody { get; }

    /// <summary>
    /// Signs <paramref name="message"/> and returns the raw signature bytes.
    /// </summary>
    /// <remarks>
    /// Takes the message itself, not a pre-computed digest: a Keystore-backed
    /// signer cannot be handed a hash it did not compute, and hashing here would
    /// make the two implementations sign different things for the same call.
    /// </remarks>
    byte[] Sign(ReadOnlySpan<byte> message);
}

/// <summary>
/// An <see cref="IDeviceSigner"/> backed directly by an in-memory <see cref="RSA"/> key.
/// </summary>
/// <remarks>
/// Signs with SHA-256 and PKCS#1 v1.5 padding, matching the Android Keystore
/// signer byte-for-byte (both are <c>SHA256withRSA</c> over the same message), so
/// the runtime's <c>SessionTranscript</c> verification cannot tell which signer
/// produced a given signature.
/// </remarks>
public sealed class RsaDeviceSigner : IDeviceSigner, IDisposable
{
    private readonly RSA _key;
    private readonly bool _ownsKey;
    private bool _disposed;

    /// <param name="key">
    /// The device's private key. By default this signer only borrows it — the
    /// caller keeps ownership and disposes it, matching the contract
    /// <see cref="GuardAiClient"/> documents for the key it is handed directly.
    /// </param>
    /// <param name="custody">
    /// Where <paramref name="key"/> actually lives. Defaults to
    /// <see cref="DeviceKeyCustody.InMemory"/>: a bare RSA instance carries no
    /// custody information of its own, and reporting anything else would be a
    /// guess this signer has no basis for.
    /// </param>
    /// <param name="ownsKey">Whether this signer disposes <paramref name="key"/> when it is disposed.</param>
    public RsaDeviceSigner(RSA key, DeviceKeyCustody custody = DeviceKeyCustody.InMemory, bool ownsKey = false)
    {
        ArgumentNullException.ThrowIfNull(key);
        // The Gate 1 wire contract is RSA-2048 on both ends (ADR-0006); a weaker
        // key would be accepted here and only refused later at the runtime, well
        // after the caller believed a device identity was established.
        if (key.KeySize != 2048)
            throw new ArgumentException($"Device identity keys must be RSA-2048; got {key.KeySize} bits.", nameof(key));
        _key = key;
        _ownsKey = ownsKey;
        Custody = custody;
        PublicKeyPem = key.ExportSubjectPublicKeyInfoPem();
    }

    public string PublicKeyPem { get; }

    public DeviceKeyCustody Custody { get; }

    public byte[] Sign(ReadOnlySpan<byte> message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _key.SignData(message.ToArray(), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_ownsKey)
            _key.Dispose();
    }
}
