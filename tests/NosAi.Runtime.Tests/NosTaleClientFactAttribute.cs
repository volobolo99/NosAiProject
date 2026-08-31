using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// A fact that runs only where a real NosTale installation is present.
/// </summary>
/// <remarks>
/// <para>
/// The archive reader's layouts were derived from real client files, so the tests
/// that matter most read those same files. That is real-environment verification
/// and it cannot be faked: no fixture proves a format holds against the bytes the
/// game actually ships.
/// </para>
/// <para>
/// Where no client is installed — CI, or another machine — these skip with a
/// reason instead of failing. The synthetic tests still cover the parsing rules on
/// every machine; what is excused here is only the part that needs the real data.
/// A skip is never evidence that the real files were read.
/// </para>
/// </remarks>
public sealed class NosTaleClientFactAttribute : FactAttribute
{
    /// <summary>Where a stock installation keeps its archives.</summary>
    public const string DefaultDataDirectory = @"C:\Program Files (x86)\Nostale\NostaleData";

    /// <summary>Overrides the location, for an installation elsewhere.</summary>
    public const string DirectoryVariable = "NOSAI_NOSTALE_DATA";

    public NosTaleClientFactAttribute()
    {
        if (ResolveDirectory() is null)
            Skip = $"Nessuna installazione NosTale trovata: attesa in {DefaultDataDirectory} " +
                   $"oppure indicata da {DirectoryVariable}.";
    }

    /// <summary>The archive directory, or null when there is none to read.</summary>
    public static string? ResolveDirectory()
    {
        string? configured = Environment.GetEnvironmentVariable(DirectoryVariable);
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return configured;

        return Directory.Exists(DefaultDataDirectory) ? DefaultDataDirectory : null;
    }
}
