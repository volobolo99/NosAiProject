using System.Security.Cryptography;
using System.Text;

namespace NosAi.Runtime.Gate1;

/// <summary>
/// The stored runtime identity could not be used, and starting anyway would be
/// worse than not starting.
/// </summary>
/// <remarks>
/// <see cref="Reason"/> is a stable identifier, not prose, so the operator UI and
/// the logs can tell an identity that will not unwrap from one that is the wrong
/// size without parsing an English sentence.
/// </remarks>
public sealed class RuntimeIdentityException : Exception
{
    public RuntimeIdentityException(string reason, string message, Exception? inner = null)
        : base(message, inner) => Reason = reason;

    public string Reason { get; }
}

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
/// <b>Custody (ADR-0010).</b> The private half is wrapped with Windows DPAPI under
/// <see cref="DataProtectionScope.CurrentUser"/> and stored at
/// <see cref="DefaultProtectedPath"/>. A plaintext PEM left by an older build is
/// migrated on first load and then deleted. DPAPI ties the key to the operator's
/// Windows account, not to a TPM: it stops a copied file and another account, and
/// it does not stop code already running as that account. That is a real
/// improvement over a readable file and should not be described as more.
/// </para>
/// </remarks>
public sealed class RuntimeIdentity : IDisposable
{
    /// <summary>
    /// Legacy plaintext location, kept only so an existing one can be migrated.
    /// </summary>
    /// <remarks>
    /// Still the parameter callers pass, and still the name the operator tooling
    /// refers to, so the migration is invisible to everything but this class.
    /// </remarks>
    public const string DefaultPath = "data/runtime_identity.pem";

    /// <summary>Where the wrapped private key lives (ADR-0010).</summary>
    public const string DefaultProtectedPath = "data/runtime_identity.dpapi";

    /// <summary>Public companion of <see cref="DefaultPath"/>, pinned by the phone at pairing.</summary>
    public const string DefaultPublicPath = "data/runtime_public.pem";

    /// <summary>
    /// DPAPI optional entropy. Not a secret, and not pretending to be one.
    /// </summary>
    /// <remarks>
    /// It domain-separates this blob from any other DPAPI data of the same user:
    /// a blob protected for another purpose cannot be swapped in here, and this
    /// one cannot be unwrapped by code that does not know it belongs to Gate 1.
    /// It adds nothing against an attacker who has the source, which is everyone.
    /// </remarks>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NOSAI-RUNTIME-IDENTITY-V1");

    private readonly RSA _key;

    private RuntimeIdentity(RSA key) => _key = key;

    /// <summary>The public half, to be pinned by the phone.</summary>
    public string PublicKeyPem => _key.ExportSubjectPublicKeyInfoPem();

    /// <summary>
    /// A plaintext identity that survived migration and is still readable on disk,
    /// or null when there is none.
    /// </summary>
    /// <remarks>
    /// Migration deletes the old PEM. When the delete fails the runtime still
    /// starts — the wrapped copy is authoritative and the leftover is no worse than
    /// the state before ADR-0010 — but a readable private key is exactly what this
    /// decision removes, so it is surfaced rather than swallowed.
    /// </remarks>
    public string? UnprotectedRemnantPath { get; private init; }

    /// <summary>Where the wrapped key sits, given the legacy identity path.</summary>
    public static string ProtectedPathFor(string privatePath) =>
        SiblingOf(privatePath, DefaultProtectedPath);

    /// <summary>Where the public half of a stored identity is written.</summary>
    public static string PublicPathFor(string privatePath) =>
        SiblingOf(privatePath, DefaultPublicPath);

    private static string SiblingOf(string privatePath, string defaultPath)
    {
        string? directory = Path.GetDirectoryName(privatePath);
        return string.IsNullOrEmpty(directory)
            ? Path.GetFileName(defaultPath)
            : Path.Combine(directory, Path.GetFileName(defaultPath));
    }

    /// <summary>
    /// Loads the stored identity, migrating or creating one as needed.
    /// </summary>
    /// <param name="path">
    /// The legacy identity path; defaults to <see cref="DefaultPath"/>. The wrapped
    /// key is its sibling, so callers did not have to change.
    /// </param>
    /// <exception cref="RuntimeIdentityException">
    /// A stored identity exists and cannot be used.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>This no longer replaces an identity it cannot read.</b> The previous
    /// behaviour was to fall through to a fresh key, on the reasoning that
    /// refusing to start is worse than a re-pair. That reasoning was wrong in the
    /// case that matters: a runtime that silently adopts a new identity presents
    /// itself to every paired phone exactly as an impostor would, and the operator
    /// sees a phone that stopped trusting the PC with no cause given. Failing to
    /// start, with the reason named, is the honest outcome — and the remedy is one
    /// deliberate deletion plus a re-pair.
    /// </para>
    /// <para>
    /// Only a genuinely absent identity produces a new one. That is first run.
    /// </para>
    /// </remarks>
    public static RuntimeIdentity LoadOrCreate(string? path = null)
    {
        string legacyFile = path ?? DefaultPath;
        string protectedFile = ProtectedPathFor(legacyFile);

        if (File.Exists(protectedFile))
            return Adopt(Unwrap(protectedFile), legacyFile);

        if (File.Exists(legacyFile))
            return Migrate(legacyFile, protectedFile);

        return Create(legacyFile, protectedFile);
    }

    private static RSA Unwrap(string protectedFile)
    {
        byte[] wrapped;
        try
        {
            wrapped = File.ReadAllBytes(protectedFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new RuntimeIdentityException(
                "identity_unreadable",
                $"The runtime identity at '{protectedFile}' could not be read. " +
                "Fix the file permissions, or delete it and pair the phone again.",
                ex);
        }

        byte[] plain;
        try
        {
            plain = ProtectedData.Unprotect(wrapped, Entropy, DataProtectionScope.CurrentUser);
        }
        catch (PlatformNotSupportedException ex)
        {
            throw new RuntimeIdentityException(
                "identity_store_unavailable",
                "Windows DPAPI is not available on this platform, so the runtime identity " +
                "cannot be unwrapped. The Gate 1 runtime is a Windows component (ADR-0010).",
                ex);
        }
        catch (CryptographicException ex)
        {
            // DPAPI reports the same failure for "protected for another Windows
            // account" and "the bytes are damaged", so the reason says what is
            // actually known and the message names both causes. Guessing the more
            // likely one would send the operator down the wrong path half the time.
            throw new RuntimeIdentityException(
                "identity_unwrap_failed",
                $"The runtime identity at '{protectedFile}' could not be unwrapped. Either it was " +
                "protected for a different Windows account (ADR-0010 uses DPAPI CurrentUser), or the " +
                "file is damaged. Run the runtime as the account that created it, or delete the file " +
                "and pair the phone again — a new identity looks like an impostor to an already-paired " +
                "phone, so it is never created for you silently.",
                ex);
        }

        try
        {
            var key = RSA.Create();
            try
            {
                key.ImportRSAPrivateKey(plain, out _);
                if (key.KeySize != 2048)
                {
                    key.Dispose();
                    throw new RuntimeIdentityException(
                        "identity_wrong_key_size",
                        $"The stored runtime identity is {key.KeySize}-bit; Gate 1 accepts RSA-2048 only. " +
                        $"Delete '{protectedFile}' and pair the phone again.");
                }
                return key;
            }
            catch (CryptographicException ex)
            {
                key.Dispose();
                throw new RuntimeIdentityException(
                    "identity_corrupt",
                    $"The stored runtime identity at '{protectedFile}' unwrapped but is not a usable " +
                    "RSA key. Delete it and pair the phone again.",
                    ex);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static RuntimeIdentity Migrate(string legacyFile, string protectedFile)
    {
        var key = RSA.Create();
        try
        {
            key.ImportFromPem(File.ReadAllText(legacyFile));
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            key.Dispose();
            throw new RuntimeIdentityException(
                "identity_corrupt",
                $"The runtime identity at '{legacyFile}' could not be read, so it cannot be migrated " +
                "to protected storage. Delete it and pair the phone again.",
                ex);
        }

        if (key.KeySize != 2048)
        {
            int size = key.KeySize;
            key.Dispose();
            throw new RuntimeIdentityException(
                "identity_wrong_key_size",
                $"The runtime identity at '{legacyFile}' is {size}-bit; Gate 1 accepts RSA-2048 only. " +
                "Delete it and pair the phone again.");
        }

        // The key material is unchanged, so the public half is unchanged, so every
        // already-paired phone keeps working. That is the whole point of migrating
        // rather than regenerating.
        Store(key, protectedFile);

        string? remnant = null;
        try
        {
            File.Delete(legacyFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Reported, not fatal. The readable copy is what this decision removes,
            // so it must be visible — but refusing to start would be an outage over
            // a condition no worse than yesterday's, and the wrapped copy is read
            // first from now on, so the leftover is stale rather than authoritative.
            _ = ex;
            remnant = legacyFile;
        }

        return Adopt(key, legacyFile, remnant);
    }

    private static RuntimeIdentity Create(string legacyFile, string protectedFile)
    {
        var key = RSA.Create(2048);
        Store(key, protectedFile);
        return Adopt(key, legacyFile);
    }

    /// <summary>Wraps and writes the private key. Failing to store it is fatal.</summary>
    /// <remarks>
    /// An identity that cannot be persisted is one that changes on every restart,
    /// which breaks pairing on the next start rather than this one — a failure that
    /// would surface far from its cause.
    /// </remarks>
    private static void Store(RSA key, string protectedFile)
    {
        byte[] plain = key.ExportRSAPrivateKey();
        try
        {
            byte[] wrapped;
            try
            {
                wrapped = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            }
            catch (PlatformNotSupportedException ex)
            {
                throw new RuntimeIdentityException(
                    "identity_store_unavailable",
                    "Windows DPAPI is not available on this platform, so the runtime identity cannot " +
                    "be stored. The Gate 1 runtime is a Windows component (ADR-0010).",
                    ex);
            }

            try
            {
                string? directory = Path.GetDirectoryName(protectedFile);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllBytes(protectedFile, wrapped);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new RuntimeIdentityException(
                    "identity_not_written",
                    $"The runtime identity could not be written to '{protectedFile}'. Without it the " +
                    "identity changes on the next start and every paired phone refuses this PC.",
                    ex);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static RuntimeIdentity Adopt(RSA key, string legacyFile, string? remnant = null)
    {
        var identity = new RuntimeIdentity(key) { UnprotectedRemnantPath = remnant };
        WritePublicCompanion(legacyFile, identity);
        return identity;
    }

    private static void WritePublicCompanion(string privatePath, RuntimeIdentity identity)
    {
        try
        {
            File.WriteAllText(PublicPathFor(privatePath), identity.PublicKeyPem);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only a convenience copy: pairing can export the public half from the
            // loaded identity, so its absence costs nothing the session needs.
        }
    }

    /// <summary>An in-memory identity, for tests.</summary>
    public static RuntimeIdentity CreateEphemeral() => new(RSA.Create(2048));

    /// <summary>Signs the handshake transcript as the server.</summary>
    /// <remarks>
    /// The transcript covers both ephemeral key-agreement keys (ADR-0009), so
    /// this one signature also authenticates the key exchange the session payload
    /// is encrypted under.
    /// </remarks>
    public byte[] SignAsServer(
        ReadOnlySpan<byte> clientNonce,
        ReadOnlySpan<byte> serverNonce,
        ReadOnlySpan<byte> clientEphemeral,
        ReadOnlySpan<byte> serverEphemeral)
        => _key.SignHash(
            SessionTranscript.Compute(HandshakeRole.Server, clientNonce, serverNonce, clientEphemeral, serverEphemeral),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

    public void Dispose() => _key.Dispose();
}
