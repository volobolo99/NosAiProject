using System.Runtime.InteropServices;
using System.Security.Cryptography;
using NosAi.Runtime.Gate1;

namespace NosAi.Runtime.Configuration;

/// <summary>Outcome of a single environment precondition.</summary>
/// <remarks>
/// <see cref="Unknown"/> is deliberately not a synonym for <see cref="Passed"/>.
/// A check that could not run has established nothing, and where the check is
/// required that is exactly as disqualifying as a failure -- see
/// <see cref="EnvironmentReport.IsSatisfied"/>.
/// </remarks>
public enum EnvironmentCheckStatus
{
    Passed,
    Failed,
    Unknown
}

/// <summary>One precondition the runtime depends on, and what was actually observed.</summary>
/// <param name="Name">Stable identifier, e.g. <c>data.directory.writable</c>.</param>
/// <param name="Status">What the check established.</param>
/// <param name="Detail">Human-readable evidence: the path tried, the failure text.</param>
/// <param name="Required">
/// Whether the runtime may boot without this. Required-ness belongs to the
/// instance rather than to the kind of check, because it can depend on the
/// configuration: a trusted Guard key is optional, but one that <em>was</em>
/// configured and cannot be parsed is not.
/// </param>
public sealed record EnvironmentCheck(string Name, EnvironmentCheckStatus Status, string Detail, bool Required);

/// <summary>Every precondition checked for one runtime start.</summary>
public sealed record EnvironmentReport(IReadOnlyList<EnvironmentCheck> Checks)
{
    /// <summary>
    /// True only when every required check actually passed. An
    /// <see cref="EnvironmentCheckStatus.Unknown"/> required check leaves this
    /// false: the runtime fails closed rather than booting on a precondition
    /// nobody managed to confirm.
    /// </summary>
    public bool IsSatisfied => Checks.All(c => !c.Required || c.Status == EnvironmentCheckStatus.Passed);

    /// <summary>The required checks that did not pass, i.e. why the runtime will not boot.</summary>
    public IReadOnlyList<EnvironmentCheck> Blocking =>
        Checks.Where(c => c.Required && c.Status != EnvironmentCheckStatus.Passed).ToArray();

    public override string ToString() =>
        string.Join("; ", Checks.Select(c => $"{c.Name}={c.Status}{(c.Required ? string.Empty : " (advisory)")}: {c.Detail}"));
}

/// <summary>
/// Raised when the runtime is asked to start in an environment that cannot
/// support it. Carries the whole <see cref="EnvironmentReport"/> rather than only
/// the first failure, so the operator sees everything that needs fixing in one
/// pass instead of one restart per problem.
/// </summary>
public sealed class RuntimeEnvironmentException : InvalidOperationException
{
    public EnvironmentReport Report { get; }

    public RuntimeEnvironmentException(EnvironmentReport report)
        : base("The runtime environment does not satisfy its preconditions: " +
               string.Join("; ", report.Blocking.Select(c => $"{c.Name} ({c.Status}) {c.Detail}")))
    {
        Report = report;
    }
}

/// <summary>
/// Validates the environment the runtime is about to boot into, before it boots.
/// </summary>
/// <remarks>
/// <para>
/// Each of these used to surface as an exception thrown from somewhere deep inside
/// <see cref="Gate1BootstrapHost"/> construction: a read-only <c>data</c>
/// directory came out of <see cref="RuntimeIdentity.LoadOrCreate"/> as a raw
/// <see cref="IOException"/>, several frames away from anything that named the
/// directory. Checking up front turns those into one report that names the
/// precondition, the path and the observed failure.
/// </para>
/// <para>
/// The checks are deliberately the cheap, non-racy ones. Port availability is not
/// among them: a port confirmed free here can be taken before the bind a few
/// milliseconds later, so the answer would be worth less than the bind failure
/// that is already handled.
/// </para>
/// </remarks>
public static class RuntimeEnvironmentValidator
{
    /// <summary>Where the runtime keeps identity, trusted keys and the event log.</summary>
    public static string DefaultDataDirectory =>
        Path.GetDirectoryName(RuntimeIdentity.DefaultPath) is { Length: > 0 } directory ? directory : "data";

    public static EnvironmentReport Validate(Gate1HostOptions options, string? dataDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new EnvironmentReport(new[]
        {
            CheckPlatform(),
            CheckDataDirectory(dataDirectory ?? DefaultDataDirectory),
            CheckTrustedGuardKey(options)
        });
    }

    /// <summary>
    /// The runtime is Windows-only, and not incidentally: ADR-0010 puts the runtime
    /// identity in DPAPI, and capture and input go through Desktop Duplication and
    /// SendInput. Anywhere else the identity would have to fall back to a readable
    /// key on disk, which is the thing ADR-0010 removes.
    /// </summary>
    private static EnvironmentCheck CheckPlatform()
    {
        bool windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        return new EnvironmentCheck(
            "platform.windows",
            windows ? EnvironmentCheckStatus.Passed : EnvironmentCheckStatus.Failed,
            windows
                ? RuntimeInformation.OSDescription
                : $"{RuntimeInformation.OSDescription}: DPAPI key custody (ADR-0010) and the capture and input backends require Windows.",
            Required: true);
    }

    /// <summary>
    /// The data directory has to be writable, not merely present: the runtime
    /// identity, the trusted Guard key and the durable event log are all written
    /// there. Writability is established by actually writing, because on Windows an
    /// ACL can deny the write to a directory whose attributes look fine.
    /// </summary>
    private static EnvironmentCheck CheckDataDirectory(string dataDirectory)
    {
        const string name = "data.directory.writable";
        try
        {
            Directory.CreateDirectory(dataDirectory);
            string probe = Path.Combine(dataDirectory, $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllBytes(probe, Array.Empty<byte>());
            File.Delete(probe);
            return new EnvironmentCheck(name, EnvironmentCheckStatus.Passed,
                $"{Path.GetFullPath(dataDirectory)} is writable.", Required: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return new EnvironmentCheck(name, EnvironmentCheckStatus.Failed,
                $"{dataDirectory}: {ex.GetType().Name}: {ex.Message}", Required: true);
        }
        catch (Exception ex)
        {
            // The write neither succeeded nor failed for a reason this knows how to
            // read, so nothing was established. Required, so it still blocks.
            return new EnvironmentCheck(name, EnvironmentCheckStatus.Unknown,
                $"{dataDirectory}: {ex.GetType().Name}: {ex.Message}", Required: true);
        }
    }

    /// <summary>
    /// A trusted Guard key is optional -- with none at all the channel still fails
    /// closed at the handshake, and this check is advisory. One that was configured
    /// and cannot be parsed is a different matter: the operator asked for a specific
    /// key, and booting past that would leave the runtime trusting nothing while
    /// looking configured.
    /// </summary>
    private static EnvironmentCheck CheckTrustedGuardKey(Gate1HostOptions options)
    {
        const string name = "guard.trusted_key";
        string? pem = options.TrustedGuardPublicKeyPem;
        string source = options.TrustedGuardPublicKeySource ?? "none";

        if (string.IsNullOrWhiteSpace(pem))
            return new EnvironmentCheck(name, EnvironmentCheckStatus.Unknown,
                "No trusted Guard public key is configured; the Guard channel fails closed until one is enrolled.",
                Required: false);

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            return new EnvironmentCheck(name, EnvironmentCheckStatus.Passed,
                $"Trusted Guard public key loaded from {source} ({rsa.KeySize}-bit).", Required: true);
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            return new EnvironmentCheck(name, EnvironmentCheckStatus.Failed,
                $"Trusted Guard public key from {source} could not be parsed: {ex.Message}", Required: true);
        }
    }
}
