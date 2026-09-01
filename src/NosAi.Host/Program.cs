using NosAi.Host;
using NosAi.Storage;

Dictionary<string, string?> arguments = ParseArguments(args);

if (!arguments.TryGetValue("--gate", out string? gateValue) || gateValue != "1")
{
    Console.Error.WriteLine("Only --gate 1 is implemented so far: docs/ROADMAP_ESECUTIVA.md S:10 forbids opening a later Gate, even in scaffolding, before this one is certified.");
    return 1;
}

if (!arguments.TryGetValue("--attach", out string? processName) || string.IsNullOrWhiteSpace(processName))
{
    Console.Error.WriteLine("--attach <processName> is required.");
    return 1;
}

if (!arguments.TryGetValue("--module-sha256", out string? moduleSha256) || string.IsNullOrWhiteSpace(moduleSha256))
{
    // Deliberately not defaulted to an empty/skip value: an attach that cannot
    // verify the module it found is not a weaker verification, it is none.
    Console.Error.WriteLine("--module-sha256 <hex> is required (the known-good hash of --expected-module's on-disk file).");
    return 1;
}

string expectedModule = arguments.GetValueOrDefault("--expected-module") ?? processName;
var journalOptions = new SqliteJournalOptions();
string sessionId = arguments.GetValueOrDefault("--session-id") ?? Guid.NewGuid().ToString("N");
string? journalDbOverride = arguments.GetValueOrDefault("--journal-db");

int listenPort = -1;
string listenAddress = "0.0.0.0";
if (arguments.ContainsKey("--listen"))
{
    string? listenValue = arguments["--listen"];
    listenPort = 17480;
    if (!string.IsNullOrWhiteSpace(listenValue) && !int.TryParse(listenValue, out listenPort))
    {
        Console.Error.WriteLine("--listen expects a TCP port, or no value for the default 17480.");
        return 1;
    }
}

if (arguments.TryGetValue("--bind", out string? bindValue) && !string.IsNullOrWhiteSpace(bindValue))
    listenAddress = bindValue;

byte[]? capbacRootKey = null;
if (arguments.TryGetValue("--capbac-root-key", out string? capbacHex) && !string.IsNullOrWhiteSpace(capbacHex))
    capbacRootKey = Convert.FromHexString(capbacHex);

var hostOptions = new HostOptions(
    ProcessName: processName,
    ExpectedModule: expectedModule,
    ModuleSha256: moduleSha256,
    AttachTimeoutMs: int.TryParse(arguments.GetValueOrDefault("--attach-timeout-ms"), out int parsedTimeout) ? parsedTimeout : 2000,
    JournalOptions: journalOptions,
    SessionId: sessionId,
    VerifyChainOnStart: arguments.ContainsKey("--verify-chain"),
    ListenPort: listenPort,
    ListenAddress: listenAddress,
    CapabilityRootKey: capbacRootKey);

NosAiHost host;
try
{
    host = journalDbOverride is not null
        ? NosAiHost.ComposeWithJournalPath(hostOptions, journalDbOverride)
        : NosAiHost.Compose(hostOptions);
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"Bootstrap failed: {ex.Message}");
    return 1;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

host.Dashboard.FramePublished += frame =>
{
    Console.WriteLine($"t={frame.UnixMillis} stage={frame.Stage} status={frame.Status} fault={frame.Fault} frames={host.Dashboard.AcceptedFrameCount} connected={host.Dashboard.PeerConnected}");
};

await using (host)
{
    if (listenPort >= 0)
    {
        Console.WriteLine($"capbacRootKey={Convert.ToHexString(host.CapabilityRootKey)}");
        Console.WriteLine("Listening until Ctrl+C. A mobile initiator completes Noise_XX then presents a CapBAC token.");
        Console.WriteLine("T-06/T-07 (docs/TEST_RIMANDATI.md) still require a real phone and a real target process; this process only makes those runs possible.");
    }

    HostBootstrapResult result = await host.RunAsync(cts.Token);

    Console.WriteLine($"session={sessionId}");
    Console.WriteLine($"attached={result.Attached} fault={result.AttachFault} journaledSequence={result.JournaledSequence}");
    Console.WriteLine($"frames={host.Dashboard.AcceptedFrameCount} sessions={host.Dashboard.CompletedSessionCount}");

    if (result.ChainIntact is bool intact)
    {
        Console.WriteLine(intact
            ? "chain=intact"
            : $"chain=BROKEN firstBrokenSequence={result.ChainFirstBrokenSequence}");
    }
}

return 0;

static Dictionary<string, string?> ParseArguments(string[] args)
{
    var result = new Dictionary<string, string?>(StringComparer.Ordinal);

    for (int i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal))
            continue;

        bool hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
        result[args[i]] = hasValue ? args[++i] : null;
    }

    return result;
}
