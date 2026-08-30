namespace NosAi.Runtime.Configuration;

public sealed class Gate1HostOptions
{
    /// <summary>
    /// Operator API port for the runtime. It is deliberately NOT 8765: that port
    /// belongs to the Python operator UI (<c>python -m nosai.dashboard.server</c>),
    /// which reads this runtime over <c>NOSAI_RUNTIME_URL</c>. Sharing one default
    /// made whichever process started second fail to bind.
    /// </summary>
    public const int DefaultDashboardPort = 8766;
    public const int DefaultGuardPort = 17471;

    /// <summary>
    /// Where the enrollment tool writes the phone's public key. Loaded when no key
    /// is passed explicitly, so a paired device works without the operator
    /// repeating a flag on every start.
    /// </summary>
    /// <remarks>
    /// Only ever a convenience over an explicit key, never a relaxation: the file
    /// has to exist, it is written by this machine's own enrollment run, and the
    /// runtime logs which key it ended up trusting. With no key at all the channel
    /// still fails closed.
    /// </remarks>
    public const string DefaultTrustedKeyPath = "data/guard_public_key.pem";

    /// <summary>Port for the operator API. 0 selects a free loopback port at bind time.</summary>
    public int DashboardPort { get; init; } = DefaultDashboardPort;
    public int GuardPort { get; init; } = DefaultGuardPort;
    public int OperationTimeoutMs { get; init; } = 5000;
    public string? TrustedGuardPublicKeyPem { get; init; }
    public bool DevEnrollment { get; init; }
    public bool StartDashboard { get; init; } = true;

    /// <summary>Answer LAN discovery probes so the phone can find this runtime.</summary>
    public bool EnableDiscovery { get; init; } = true;

    /// <summary>
    /// Restrict the Guard channel to loopback, which disables the Wi-Fi transport.
    /// </summary>
    /// <remarks>
    /// Off by default so a phone on the same network can connect without the
    /// operator configuring anything. Turn it on where the local network is not
    /// trusted: the channel stays fail-closed either way, but on a shared network
    /// any host can reach the handshake and occupy the single session slot.
    /// See docs/adr/ADR-0007-wifi-transport.md.
    /// </remarks>
    public bool GuardLoopbackOnly { get; init; }

    /// <summary>Where the trusted key came from, for the startup log. Null when there is none.</summary>
    public string? TrustedGuardPublicKeySource { get; init; }
    /// <summary>
    /// Comma-separated executable names for the game client, without extension.
    /// The default covers the shipped NostaleClientX; "NosTale" alone matched no
    /// running process, and this option was never passed to the connector.
    /// </summary>
    public string ClientProcessName { get; init; } = "NostaleClientX,NostaleClient,NosTale";

    public void Validate()
    {
        if (DashboardPort is < 0 or > 65535)
            throw new InvalidOperationException("DashboardPort must be between 0 and 65535.");
        if (GuardPort is < 0 or > 65535)
            throw new InvalidOperationException("GuardPort must be between 0 and 65535.");
        if (OperationTimeoutMs is < 100 or > 120_000)
            throw new InvalidOperationException("OperationTimeoutMs must be between 100 and 120000 milliseconds.");
        if (string.IsNullOrWhiteSpace(ClientProcessName))
            throw new InvalidOperationException("ClientProcessName is required.");
    }
}

public static class Gate1HostOptionsLoader
{
    public static Gate1HostOptions Load(IReadOnlyDictionary<string, string?> environment, IEnumerable<string> args)
    {
        var argList = args.ToArray();
        var (pem, pemSource) = ReadPemWithSource(environment, argList);
        var options = new Gate1HostOptions
        {
            DashboardPort = ReadInt(environment, argList, "NOSAI_DASHBOARD_PORT", "--dashboard-port", Gate1HostOptions.DefaultDashboardPort),
            GuardPort = ReadInt(environment, argList, "NOSAI_GUARD_PORT", "--guard-port", Gate1HostOptions.DefaultGuardPort),
            OperationTimeoutMs = ReadInt(environment, argList, "NOSAI_OPERATION_TIMEOUT_MS", "--timeout-ms", 5000),
            TrustedGuardPublicKeyPem = pem,
            TrustedGuardPublicKeySource = pemSource,
            DevEnrollment = HasFlag(argList, "--dev-enroll") || IsTruthy(environment, "NOSAI_DEV_ENROLL"),
            StartDashboard = !HasFlag(argList, "--no-dashboard"),
            EnableDiscovery = !HasFlag(argList, "--no-discovery"),
            GuardLoopbackOnly = HasFlag(argList, "--guard-loopback-only") || IsTruthy(environment, "NOSAI_GUARD_LOOPBACK_ONLY"),
            ClientProcessName = ReadString(environment, argList, "NOSAI_CLIENT_PROCESS", "--client-process", new Gate1HostOptions().ClientProcessName)
        };
        options.Validate();
        return options;
    }

    private static (string? Pem, string? Source) ReadPemWithSource(
        IReadOnlyDictionary<string, string?> environment, string[] args)
    {
        var inline = ReadOptionalString(environment, args, "NOSAI_GUARD_PUBLIC_KEY_PEM", "--guard-public-key-pem");
        if (!string.IsNullOrWhiteSpace(inline) && inline.Contains("BEGIN", StringComparison.Ordinal))
            return (inline, "inline");

        var path = ReadOptionalString(environment, args, "NOSAI_GUARD_PUBLIC_KEY_PATH", "--guard-public-key-path")
            ?? inline;

        if (!string.IsNullOrWhiteSpace(path))
        {
            // An explicitly named key that is absent is an error, never a silent
            // fall back to some other key: the operator asked for a specific one.
            if (!File.Exists(path))
                throw new InvalidOperationException($"Trusted Guard public key file was not found: {path}");
            return (File.ReadAllText(path), path);
        }

        return File.Exists(Gate1HostOptions.DefaultTrustedKeyPath)
            ? (File.ReadAllText(Gate1HostOptions.DefaultTrustedKeyPath), Gate1HostOptions.DefaultTrustedKeyPath)
            : (null, null);
    }

    private static int ReadInt(IReadOnlyDictionary<string, string?> environment, string[] args, string envName, string flag, int fallback)
    {
        var raw = ReadOptionalString(environment, args, envName, flag);
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;
        if (!int.TryParse(raw, out var value))
            throw new InvalidOperationException($"{envName} must be an integer.");
        return value;
    }

    private static string ReadString(IReadOnlyDictionary<string, string?> environment, string[] args, string envName, string flag, string fallback)
        => ReadOptionalString(environment, args, envName, flag) ?? fallback;

    private static string? ReadOptionalString(IReadOnlyDictionary<string, string?> environment, string[] args, string envName, string flag)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                continue;
            if (i + 1 >= args.Length)
                throw new InvalidOperationException($"{flag} requires a value.");
            return args[i + 1];
        }

        return environment.TryGetValue(envName, out var value) ? value : null;
    }

    private static bool HasFlag(string[] args, string flag)
        => args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    private static bool IsTruthy(IReadOnlyDictionary<string, string?> environment, string envName)
        => environment.TryGetValue(envName, out var value)
           && (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase));
}
