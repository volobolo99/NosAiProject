using System.IO;
using System.Text.Json;
using NosAi.Runtime.Configuration;

namespace NosAi.ControlPanel;

/// <summary>
/// Operator preferences for the next runtime start. Not security policy:
/// Guard/Trust/Safety still decide what is allowed.
/// </summary>
public sealed class OperatorSettings
{
    public const string RelativePath = "data/control_panel.json";

    public int DashboardPort { get; set; } = Gate1HostOptions.DefaultDashboardPort;
    public int GuardPort { get; set; } = Gate1HostOptions.DefaultGuardPort;
    public int OperationTimeoutMs { get; set; } = 5000;
    public bool Discovery { get; set; } = true;
    public bool GuardLoopbackOnly { get; set; }
    public string ClientProcessName { get; set; } = new Gate1HostOptions().ClientProcessName;
    public bool AutoStartRuntime { get; set; } = true;

    public static OperatorSettings Load(string repoRoot)
    {
        var path = Path.Combine(repoRoot, RelativePath);
        if (!File.Exists(path))
            return new OperatorSettings();

        try
        {
            var loaded = JsonSerializer.Deserialize<OperatorSettings>(File.ReadAllText(path));
            return loaded ?? new OperatorSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new OperatorSettings();
        }
    }

    /// <summary>Same bounds as <see cref="Gate1HostOptions"/>, before we write the file.</summary>
    public static bool TryValidate(int dashboardPort, int guardPort, int timeoutMs, string processName, out string error)
    {
        if (dashboardPort is < 0 or > 65535)
        {
            error = "La porta API deve essere tra 0 e 65535.";
            return false;
        }

        if (guardPort is < 0 or > 65535)
        {
            error = "La porta Guard deve essere tra 0 e 65535.";
            return false;
        }

        if (timeoutMs is < 100 or > 120_000)
        {
            error = "Il timeout deve essere tra 100 e 120000 ms.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(processName))
        {
            error = "Il nome processo client è obbligatorio.";
            return false;
        }

        error = "";
        return true;
    }

    public void Save(string repoRoot)
    {
        var path = Path.Combine(repoRoot, RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public Gate1HostOptions ToHostOptions()
    {
        var args = new List<string>
        {
            "--dashboard-port", DashboardPort.ToString(),
            "--guard-port", GuardPort.ToString(),
            "--timeout-ms", OperationTimeoutMs.ToString(),
            "--client-process", ClientProcessName
        };
        if (!Discovery)
            args.Add("--no-discovery");
        if (GuardLoopbackOnly)
            args.Add("--guard-loopback-only");

        return Gate1HostOptionsLoader.Load(ReadEnvironment(), args);
    }

    private static Dictionary<string, string?> ReadEnvironment()
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key)
                result[key] = entry.Value as string;
        }

        return result;
    }
}
