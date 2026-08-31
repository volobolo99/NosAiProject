using System.Security.Cryptography;
using System.Text;
using NosAi.Runtime.Gate1;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Custody of the runtime identity (ADR-0010).
/// </summary>
/// <remarks>
/// Two properties matter more than the rest and are tested first: the wrapped key
/// survives a restart unchanged — otherwise every paired phone would see an
/// impostor — and an identity that cannot be read is never silently replaced by a
/// fresh one, which would produce that same symptom with no cause given.
/// </remarks>
public sealed class RuntimeIdentityTests : IDisposable
{
    private readonly string _directory;
    private readonly string _legacy;
    private readonly string _protectedFile;

    public RuntimeIdentityTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "nosai-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _legacy = Path.Combine(_directory, "runtime_identity.pem");
        _protectedFile = RuntimeIdentity.ProtectedPathFor(_legacy);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    // ----------------------------------------------------------------- storage

    [WindowsOnlyFact]
    public void AFirstRunWrapsTheKeyAndLeavesNoPlaintext()
    {
        using var identity = RuntimeIdentity.LoadOrCreate(_legacy);

        Assert.True(File.Exists(_protectedFile));
        Assert.False(File.Exists(_legacy));

        // The whole point of the decision: the private key is not readable on disk.
        byte[] stored = File.ReadAllBytes(_protectedFile);
        Assert.DoesNotContain("BEGIN RSA PRIVATE KEY", Encoding.ASCII.GetString(stored), StringComparison.Ordinal);
        Assert.DoesNotContain("BEGIN PRIVATE KEY", Encoding.ASCII.GetString(stored), StringComparison.Ordinal);
    }

    [WindowsOnlyFact]
    public void TheIdentitySurvivesARestartUnchanged()
    {
        // If it did not, every already-paired phone would refuse this PC after a
        // restart, and the operator would see a trust failure with no cause.
        string first;
        using (var identity = RuntimeIdentity.LoadOrCreate(_legacy))
            first = identity.PublicKeyPem;

        using var reloaded = RuntimeIdentity.LoadOrCreate(_legacy);

        Assert.Equal(first, reloaded.PublicKeyPem);
    }

    [WindowsOnlyFact]
    public void ThePublicCompanionIsWrittenBesideTheWrappedKey()
    {
        using var identity = RuntimeIdentity.LoadOrCreate(_legacy);

        string companion = RuntimeIdentity.PublicPathFor(_legacy);
        Assert.True(File.Exists(companion));
        Assert.Equal(identity.PublicKeyPem, File.ReadAllText(companion));
    }

    [WindowsOnlyFact]
    public void TheWrappedAndPublicPathsAreSiblingsOfTheIdentityPath()
    {
        Assert.Equal(_directory, Path.GetDirectoryName(_protectedFile));
        Assert.Equal(_directory, Path.GetDirectoryName(RuntimeIdentity.PublicPathFor(_legacy)));
        Assert.Equal("runtime_identity.dpapi", Path.GetFileName(_protectedFile));
    }

    // --------------------------------------------------------------- migration

    [WindowsOnlyFact]
    public void APlaintextIdentityIsMigratedWithoutChangingTheKey()
    {
        // The property that makes this a migration rather than a re-pair: same key,
        // so the public half the phone pinned still matches.
        using var original = RSA.Create(2048);
        string expected = original.ExportSubjectPublicKeyInfoPem();
        File.WriteAllText(_legacy, original.ExportRSAPrivateKeyPem());

        using var migrated = RuntimeIdentity.LoadOrCreate(_legacy);

        Assert.Equal(expected, migrated.PublicKeyPem);
        Assert.True(File.Exists(_protectedFile));
        Assert.False(File.Exists(_legacy));
        Assert.Null(migrated.UnprotectedRemnantPath);
    }

    [WindowsOnlyFact]
    public void AMigratedIdentityStillLoadsAfterTheMigration()
    {
        using var original = RSA.Create(2048);
        string expected = original.ExportSubjectPublicKeyInfoPem();
        File.WriteAllText(_legacy, original.ExportRSAPrivateKeyPem());

        using (RuntimeIdentity.LoadOrCreate(_legacy)) { }
        using var reloaded = RuntimeIdentity.LoadOrCreate(_legacy);

        Assert.Equal(expected, reloaded.PublicKeyPem);
    }

    [WindowsOnlyFact]
    public void TheWrappedKeyWinsOverALeftoverPlaintext()
    {
        // A stale plaintext must never take precedence: it could be an older key,
        // and adopting it would change the identity behind the operator's back.
        using var wrapped = RuntimeIdentity.LoadOrCreate(_legacy);
        string expected = wrapped.PublicKeyPem;

        using var stray = RSA.Create(2048);
        File.WriteAllText(_legacy, stray.ExportRSAPrivateKeyPem());

        using var reloaded = RuntimeIdentity.LoadOrCreate(_legacy);

        Assert.Equal(expected, reloaded.PublicKeyPem);
        Assert.NotEqual(stray.ExportSubjectPublicKeyInfoPem(), reloaded.PublicKeyPem);
    }

    // -------------------------------------------------------------- fail closed

    [WindowsOnlyFact]
    public void AnIdentityThatWillNotUnwrapIsNeverSilentlyReplaced()
    {
        // The regression that matters. The previous implementation fell through to
        // a fresh key here, which presents to every paired phone exactly as an
        // impostor does.
        File.WriteAllBytes(_protectedFile, RandomNumberGenerator.GetBytes(256));

        var refused = Assert.Throws<RuntimeIdentityException>(() => RuntimeIdentity.LoadOrCreate(_legacy));

        Assert.Equal("identity_unwrap_failed", refused.Reason);
        // The remedy has to be in the message: the operator cannot guess that the
        // file is bound to a Windows account.
        Assert.Contains("Windows account", refused.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pair the phone again", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    [WindowsOnlyFact]
    public void ABlobThatUnwrapsButIsNotAKeyIsRefused()
    {
        // Entropy repeated from RuntimeIdentity on purpose: changing it invalidates
        // every stored identity, so a silent change should fail a test rather than
        // a paired phone.
        byte[] entropy = Encoding.UTF8.GetBytes("NOSAI-RUNTIME-IDENTITY-V1");
        byte[] notAKey = System.Security.Cryptography.ProtectedData.Protect(
            Encoding.UTF8.GetBytes("this is not a key"), entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_protectedFile, notAKey);

        var refused = Assert.Throws<RuntimeIdentityException>(() => RuntimeIdentity.LoadOrCreate(_legacy));

        Assert.Equal("identity_corrupt", refused.Reason);
    }

    [WindowsOnlyFact]
    public void ACorruptPlaintextIsRefusedRatherThanMigratedOrReplaced()
    {
        File.WriteAllText(_legacy, "-----BEGIN RSA PRIVATE KEY-----\nnot base64\n-----END RSA PRIVATE KEY-----");

        var refused = Assert.Throws<RuntimeIdentityException>(() => RuntimeIdentity.LoadOrCreate(_legacy));

        Assert.Equal("identity_corrupt", refused.Reason);
        // Nothing was written: a half-migrated state would be worse than none.
        Assert.False(File.Exists(_protectedFile));
    }

    [WindowsOnlyFact]
    public void AWrongSizedPlaintextIsRefused()
    {
        using var weak = RSA.Create(1024);
        File.WriteAllText(_legacy, weak.ExportRSAPrivateKeyPem());

        var refused = Assert.Throws<RuntimeIdentityException>(() => RuntimeIdentity.LoadOrCreate(_legacy));

        Assert.Equal("identity_wrong_key_size", refused.Reason);
        Assert.False(File.Exists(_protectedFile));
    }

    [WindowsOnlyFact]
    public void TheEntropyIsBoundToThisPurpose()
    {
        // A DPAPI blob of the same user protected for something else must not be
        // usable here, and vice versa.
        using var identity = RuntimeIdentity.LoadOrCreate(_legacy);
        byte[] stored = File.ReadAllBytes(_protectedFile);

        Assert.Throws<CryptographicException>(() =>
            System.Security.Cryptography.ProtectedData.Unprotect(
                stored, Encoding.UTF8.GetBytes("SOME-OTHER-PURPOSE"), DataProtectionScope.CurrentUser));
    }

    // ------------------------------------------------------------------ signing

    [WindowsOnlyFact]
    public void AReloadedIdentityProducesSignaturesTheOldPublicKeyVerifies()
    {
        // The end-to-end property: a phone that pinned the key before the restart
        // still accepts the proof after it.
        string publicPem;
        using (var before = RuntimeIdentity.LoadOrCreate(_legacy))
            publicPem = before.PublicKeyPem;

        using var after = RuntimeIdentity.LoadOrCreate(_legacy);

        byte[] clientNonce = SessionTranscript.CreateNonce();
        byte[] serverNonce = SessionTranscript.CreateNonce();
        using var clientEphemeral = EphemeralKeyExchange.Create();
        using var serverEphemeral = EphemeralKeyExchange.Create();

        byte[] proof = after.SignAsServer(clientNonce, serverNonce, clientEphemeral.PublicKey, serverEphemeral.PublicKey);

        using var pinned = RSA.Create();
        pinned.ImportFromPem(publicPem);
        Assert.True(SessionTranscript.Verify(
            pinned, HandshakeRole.Server, clientNonce, serverNonce,
            clientEphemeral.PublicKey, serverEphemeral.PublicKey, proof));
    }
}
