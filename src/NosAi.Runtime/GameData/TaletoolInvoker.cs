using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace NosAi.Runtime.GameData;

/// <summary>
/// One file a directory scan found, exactly as the scanner classified it --
/// decoded or not. A file this project has no decoder for still gets a row,
/// with <see cref="ArchiveType"/> null or "unsupported": the point of an
/// inventory is to notice a client update touched it, not to explain it.
/// </summary>
public sealed record ClientInventoryEntry(
    string File,
    string? ArchiveType,
    string? Details,
    string? Error);

/// <summary>The outcome of one scan attempt.</summary>
public sealed record TaletoolScanResult(
    bool Ok,
    string? FailureReason,
    IReadOnlyList<ClientInventoryEntry> Files)
{
    public static TaletoolScanResult Failed(string reason) =>
        new(false, reason, Array.Empty<ClientInventoryEntry>());
}

/// <summary>
/// Runs the vendored <c>taletool</c> executable as a separate process and
/// reads its classification of a client data directory.
/// </summary>
/// <remarks>
/// <para>
/// <b>A subprocess, never a linked dependency.</b> <c>taletool.exe</c>
/// (<c>third_party/taletool/</c>) is AGPL-3.0-or-later; this class starts it
/// as an external OS process and reads its stdout, the same relationship
/// this project already has with <c>WinDivert</c>'s signed driver. Nothing
/// here compiles or links against taletool's own code.
/// </para>
/// <para>
/// <b>Absence is a named result, not an exception.</b> A machine without
/// the executable -- or without a client to scan -- gets a
/// <see cref="TaletoolScanResult"/> whose <see cref="TaletoolScanResult.Ok"/>
/// is false and whose <see cref="TaletoolScanResult.FailureReason"/> says
/// why, the same convention <see cref="ReferenceImporter"/> already uses
/// for a missing client directory.
/// </para>
/// </remarks>
public static class TaletoolInvoker
{
    /// <summary>Overrides where the executable is found.</summary>
    public const string ExecutableVariable = "NOSAI_TALETOOL_PATH";

    private const string DefaultExecutableName = "taletool.exe";

    /// <summary>
    /// The executable path, or null when neither the override variable nor
    /// the running process's own directory has it.
    /// </summary>
    public static string? ResolveExecutablePath()
    {
        string? configured = Environment.GetEnvironmentVariable(ExecutableVariable);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        string? processDirectory = Path.GetDirectoryName(Environment.ProcessPath);
        if (processDirectory is not null)
        {
            string beside = Path.Combine(processDirectory, DefaultExecutableName);
            if (File.Exists(beside))
                return beside;
        }

        return null;
    }

    /// <summary>
    /// Classifies every file under <paramref name="dataDirectory"/> via
    /// <c>taletool scan --json --show-unsupported</c>.
    /// </summary>
    public static TaletoolScanResult Scan(string dataDirectory, string? executablePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        string? exe = executablePath ?? ResolveExecutablePath();
        if (exe is null)
            return TaletoolScanResult.Failed($"taletool_not_found:{ExecutableVariable}");

        if (!Directory.Exists(dataDirectory))
            return TaletoolScanResult.Failed($"data_dir_not_found:{dataDirectory}");

        var startInfo = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("scan");
        startInfo.ArgumentList.Add("--data-dir");
        startInfo.ArgumentList.Add(dataDirectory);
        startInfo.ArgumentList.Add("--json");
        startInfo.ArgumentList.Add("--show-unsupported");

        string stdout;
        string stderr;
        int exitCode;
        try
        {
            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("process_start_returned_null");
            stdout = process.StandardOutput.ReadToEnd();
            stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            exitCode = process.ExitCode;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return TaletoolScanResult.Failed($"process_start_failed:{ex.GetType().Name}:{ex.Message}");
        }

        if (exitCode != 0)
            return TaletoolScanResult.Failed($"exit_code_{exitCode}:{stderr.Trim()}");

        IReadOnlyList<ClientInventoryEntry>? entries = ParseScanJson(stdout);
        if (entries is null)
            return TaletoolScanResult.Failed("unparsable_json_output");

        return new TaletoolScanResult(true, null, entries);
    }

    /// <summary>
    /// Parses <c>taletool scan --json</c>'s array output. Internal so the
    /// test suite can exercise it against a captured transcript without
    /// running the real executable.
    /// </summary>
    internal static IReadOnlyList<ClientInventoryEntry>? ParseScanJson(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return null;

            var entries = new List<ClientInventoryEntry>();
            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                if (!element.TryGetProperty("file", out JsonElement fileElement)
                    || fileElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                entries.Add(new ClientInventoryEntry(
                    fileElement.GetString()!,
                    ReadOptionalString(element, "archive_type"),
                    ReadOptionalString(element, "details"),
                    ReadOptionalString(element, "error")));
            }

            return entries;
        }
    }

    private static string? ReadOptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
