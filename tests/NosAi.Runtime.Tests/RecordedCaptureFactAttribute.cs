using System.Linq;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// A fact that runs only where a recorded capture is present under <c>data/</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>data/</c> is gitignored, deliberately: a <c>.noscap</c> is one operator's
/// real session, and the repository does not carry it. A clone therefore has no
/// recording, and a test written against one has nothing to read.
/// </para>
/// <para>
/// <b>The reason this attribute exists rather than an early return.</b> The test
/// it replaces checked for the file and returned when it was missing. Its own
/// remarks said "skipped when the recording is absent", but a bare <c>return</c>
/// is not a skip: xUnit records the test as <b>passed</b>, and the .trx says it
/// executed. On every machine without the recording — which is every machine but
/// one — the suite reported a green test that had read nothing. That is the same
/// defect <see cref="NosAi.Core.Tests.QuiescedMachineFactAttribute"/> was written
/// for, and it is worse here: there the missing thing was a quiet machine, here
/// it is the evidence itself.
/// </para>
/// <para>
/// A skip is never evidence that the real bytes were read. It says the opposite,
/// out loud, and the .trx carries the reason.
/// </para>
/// </remarks>
public sealed class RecordedCaptureFactAttribute : FactAttribute
{
    /// <summary>Overrides where recordings are looked for.</summary>
    public const string DirectoryVariable = "NOSAI_CAPTURE_DIR";

    /// <summary>The recording this fact needs, relative to the data directory.</summary>
    public string Recording { get; }

    /// <param name="recording">File name of the recording, e.g. <c>nostale_combat.noscap</c>.</param>
    public RecordedCaptureFactAttribute(string recording)
    {
        Recording = recording;
        if (Resolve(recording) is null)
            Skip = $"Registrazione assente: {recording} non trovata sotto data/ " +
                   $"(data/ e' gitignored; indicare un'altra cartella con {DirectoryVariable}).";
    }

    /// <summary>The full path of that recording, or null when there is none.</summary>
    public static string? Resolve(string recording)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recording);

        string? configured = Environment.GetEnvironmentVariable(DirectoryVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            string elsewhere = Path.Combine(configured, recording);
            return File.Exists(elsewhere) ? elsewhere : null;
        }

        string? root = RepositoryRoot();
        if (root is null) return null;

        string path = Path.Combine(root, "data", recording);
        return File.Exists(path) ? path : null;
    }

    /// <summary>The directory holding <c>NosAi.sln</c>, or null when not under one.</summary>
    private static string? RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;

        return directory?.FullName;
    }
}

/// <summary>
/// A theory that runs only where every recording it names is present.
/// </summary>
/// <remarks>
/// The theory sibling of <see cref="RecordedCaptureFactAttribute"/>. A theory
/// takes its recording from <c>InlineData</c>, which the attribute cannot see, so
/// the recordings are named here instead and the whole theory is skipped unless
/// all of them are there. Coarser than the fact, and still the right trade: a
/// theory that silently ran zero of its cases would report the same green as one
/// that ran them all.
/// </remarks>
public sealed class RecordedCaptureTheoryAttribute : TheoryAttribute
{
    /// <param name="recordings">File names of the recordings the cases need.</param>
    public RecordedCaptureTheoryAttribute(params string[] recordings)
    {
        ArgumentNullException.ThrowIfNull(recordings);

        string[] missing = recordings
            .Where(r => RecordedCaptureFactAttribute.Resolve(r) is null)
            .ToArray();

        if (missing.Length > 0)
            Skip = $"Registrazioni assenti: {string.Join(", ", missing)} non trovate sotto data/ " +
                   $"(data/ e' gitignored; indicare un'altra cartella con " +
                   $"{RecordedCaptureFactAttribute.DirectoryVariable}).";
    }
}
