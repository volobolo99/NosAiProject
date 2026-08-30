using System.Security.Cryptography;

namespace NosAi.Runtime.Gate1;

/// <summary>
/// The runtime's own RSA-2048 identity, used to prove itself to the phone.
/// </summary>
/// <remarks>
/// <para>
/// Version 1 of the channel had no such thing: the phone proved itself to the PC
/// and nothing proved the PC to the phone. Over USB that was tolerable, since the
/// cable bounds who can answer. On a network it is not — anything on the LAN could
/// reply to a discovery probe first and act as a runtime.
/// </para>
/// <para>
/// The key is persisted so the phone can pin it once and recognise this machine
/// afterwards. A regenerated identity is indistinguishable from an impostor, and
/// correctly refused by an already-paired phone.
/// </para>
/// <para>
/// It lives in a file, not a hardware store: readable by anything with the
/// operator's account or a backup of it. That is a real limitation, recorded in
/// ADR-0008, and this class is the seam where a stronger store replaces it.
/// </para>
/// </remarks>
public sealed class RuntimeIdentity : IDisposable
{
    /// <summary>Where the identity is kept when no path is given.</summary>
    /// <remarks>Alongside the trusted phone key, and gitignored for the same reason.</remarks>
    public const string DefaultPath = "data/runtime_identity.pem";

    /// <summary>Public companion of <see cref="DefaultPath"/>, pinned by the phone at pairing.</summary>
    public const string DefaultPublicPath = "data/runtime_public.pem";

    private readonly RSA _key;

    private RuntimeIdentity(RSA key) => _key = key;

    /// <summary>The public half, to be pinned by the phone.</summary>
    public string PublicKeyPem => _key.ExportSubjectPublicKeyInfoPem();

    /// <summary>
    /// Loads the stored identity, creating one on first use.
    /// </summary>
    /// <param name="path">Defaults to <see cref="DefaultPath"/>.</param>
    /// <remarks>
    /// An unreadable or wrong-sized key file is replaced rather than fatal: the
    /// cost is that already-paired phones refuse the new identity and must be
    /// paired again over USB, which is visible and recoverable. Refusing to start
    /// would not be.
    /// </remarks>
    public static RuntimeIdentity LoadOrCreate(string? path = null)
    {
        string file = path ?? DefaultPath;

        if (File.Exists(file))
        {
            var existing = RSA.Create();
            try
            {
                existing.ImportFromPem(File.ReadAllText(file));
                if (existing.KeySize == 2048)
                {
                    var loaded = new RuntimeIdentity(existing);
                    WritePublicCompanion(file, loaded);
                    return loaded;
                }
            }
            catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
            {
                // Fall through to a fresh identity.
            }

            existing.Dispose();
        }

        var created = RSA.Create(2048);
        try
        {
            string? directory = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(file, created.ExportRSAPrivateKeyPem());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The session still works; the identity just will not survive a restart,
            // and paired phones will refuse the next one. Better than failing to start.
        }

        var createdIdentity = new RuntimeIdentity(created);
        WritePublicCompanion(file, createdIdentity);
        return createdIdentity;
    }

    /// <summary>Where the public half of a stored identity is written.</summary>
    public static string PublicPathFor(string privatePath)
    {
        string? directory = Path.GetDirectoryName(privatePath);
        return string.IsNullOrEmpty(directory)
            ? Path.GetFileName(DefaultPublicPath)
            : Path.Combine(directory, Path.GetFileName(DefaultPublicPath));
    }

    private static void WritePublicCompanion(string privatePath, RuntimeIdentity identity)
    {
        try
        {
            File.WriteAllText(PublicPathFor(privatePath), identity.PublicKeyPem);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Pairing can still export the public half from the private file.
        }
    }

    /// <summary>An in-memory identity, for tests.</summary>
    public static RuntimeIdentity CreateEphemeral() => new(RSA.Create(2048));

    /// <summary>Signs the handshake transcript as the server.</summary>
    public byte[] SignAsServer(ReadOnlySpan<byte> clientNonce, ReadOnlySpan<byte> serverNonce)
        => _key.SignHash(
            SessionTranscript.Compute(HandshakeRole.Server, clientNonce, serverNonce),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

    public void Dispose() => _key.Dispose();
}
