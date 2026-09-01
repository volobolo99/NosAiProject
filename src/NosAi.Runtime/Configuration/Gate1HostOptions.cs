using System.Net;
using NosAi.Runtime.Perception.Network;

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

    /// <summary>
    /// World-channel endpoint to observe, or <c>null</c> when observation is off.
    /// Absent by default: the runtime must not capture game traffic until the
    /// operator names the host and port.
    /// </summary>
    /// <remarks>
    /// Host is an IP address — the capture filter is IP-based — and the port is
    /// 1..65535. A malformed value fails startup rather than being ignored, so a
    /// mistyped flag cannot look like "observation is simply off".
    /// </remarks>
    public GameEndpoint? ObserveGame { get; init; }

    /// <summary>
    /// Run the Gate 3 decision loop over whatever is being observed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Off by default, and it decides without acting either way: the loop is built
    /// on <see cref="Safety.RuntimeSafetyPolicy.SafeDefault"/>, which binds a
    /// disabled effector, so a cycle runs the whole pipeline through the Safety
    /// Gate and stops before touching the client. Turning it on adds reasoning and
    /// a log, not an input.
    /// </para>
    /// <para>
    /// It is off by default anyway, because a loop planning against
    /// <c>gameplay_provider_not_available</c> would fill the journal with refusals
    /// that say nothing except that observation was never configured.
    /// </para>
    /// </remarks>
    public bool RunDecisionLoop { get; init; }

    /// <summary>How often a decision cycle runs. Ignored unless <see cref="RunDecisionLoop"/>.</summary>
    public int DecisionIntervalMs { get; init; } = 500;

    public void Validate()
    {
        if (DecisionIntervalMs is < 50 or > 60_000)
            throw new InvalidOperationException("DecisionIntervalMs must be between 50 and 60000 milliseconds.");
        if (DashboardPort is < 0 or > 65535)
            throw new InvalidOperationException("DashboardPort must be between 0 and 65535.");
        if (GuardPort is < 0 or > 65535)
            throw new InvalidOperationException("GuardPort must be between 0 and 65535.");
        if (OperationTimeoutMs is < 100 or > 120_000)
            throw new InvalidOperationException("OperationTimeoutMs must be between 100 and 120000 milliseconds.");
        if (string.IsNullOrWhiteSpace(ClientProcessName))
            throw new InvalidOperationException("ClientProcessName is required.");
        if (ObserveGame is { } endpoint)
            Gate1HostOptionsLoader.ValidateObserveGame(endpoint);
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
            ClientProcessName = ReadString(environment, argList, "NOSAI_CLIENT_PROCESS", "--client-process", new Gate1HostOptions().ClientProcessName),
            ObserveGame = ReadObserveGame(environment, argList),
            RunDecisionLoop = HasFlag(argList, "--decide") || IsTruthy(environment, "NOSAI_DECIDE"),
            DecisionIntervalMs = ReadInt(environment, argList, "NOSAI_DECIDE_INTERVAL_MS", "--decide-interval-ms", 500)
        };
        options.Validate();
        return options;
    }

    /// <summary>
    /// Parses <c>host:port</c> for the world-channel observation option.
    /// </summary>
    /// <remarks>
    /// Last-colon split for IPv4; bracket form <c>[addr]:port</c> for IPv6.
    /// Whitespace-only is absence. Anything else is a startup failure.
    /// </remarks>
    public static GameEndpoint ParseObserveGame(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("NOSAI_OBSERVE_GAME / --observe-game requires host:port.");

        string trimmed = raw.Trim();
        string host;
        string portText;
        if (trimmed.StartsWith('['))
        {
            int close = trimmed.IndexOf(']');
            if (close < 2 || close + 1 >= trimmed.Length || trimmed[close + 1] != ':')
                throw new InvalidOperationException("NOSAI_OBSERVE_GAME must be [ipv6]:port or host:port.");
            host = trimmed[1..close].Trim();
            portText = trimmed[(close + 2)..];
        }
        else
        {
            int colon = trimmed.LastIndexOf(':');
            if (colon <= 0 || colon == trimmed.Length - 1)
                throw new InvalidOperationException("NOSAI_OBSERVE_GAME must be host:port.");
            host = trimmed[..colon].Trim();
            portText = trimmed[(colon + 1)..];
        }

        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException("NOSAI_OBSERVE_GAME host must not be empty.");
        if (!int.TryParse(portText, out int port) || port is < 1 or > 65535)
            throw new InvalidOperationException("NOSAI_OBSERVE_GAME port must be an integer between 1 and 65535.");
        if (!IPAddress.TryParse(host, out _))
            throw new InvalidOperationException(
                "NOSAI_OBSERVE_GAME host must be an IP address; the capture filter is IP-based.");

        return new GameEndpoint(host, port);
    }

    internal static void ValidateObserveGame(GameEndpoint endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint.Host))
            throw new InvalidOperationException("NOSAI_OBSERVE_GAME host must not be empty.");
        if (endpoint.Port is < 1 or > 65535)
            throw new InvalidOperationException("NOSAI_OBSERVE_GAME port must be an integer between 1 and 65535.");
        if (!IPAddress.TryParse(endpoint.Host, out _))
            throw new InvalidOperationException(
                "NOSAI_OBSERVE_GAME host must be an IP address; the capture filter is IP-based.");
    }

    private static GameEndpoint? ReadObserveGame(IReadOnlyDictionary<string, string?> environment, string[] args)
    {
        var raw = ReadOptionalString(environment, args, "NOSAI_OBSERVE_GAME", "--observe-game");
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return ParseObserveGame(raw);
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
