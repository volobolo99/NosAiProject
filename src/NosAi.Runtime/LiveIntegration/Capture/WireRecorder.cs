using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace NosAi.LiveIntegration.Capture;

/// <summary>What a recording run produced, and why it is not usable when it is not.</summary>
/// <remarks>
/// A file that exists is not evidence. A run that captured nothing writes a
/// well-formed but empty recording, and calling that a success would hand the
/// operator a second source with nothing in it.
/// </remarks>
public readonly record struct RecordingOutcome(long Packets, string Path, string? FailureReason)
{
    public bool Ok => FailureReason is null;
}

/// <summary>
/// Records the game's wire to a <c>.noscap</c>, so a memory reading can be
/// checked against a second source observed at the same moment.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the archived recordings cannot corroborate a live read.
/// Entity ids are per-session and vitals are per-instant, so comparing memory
/// now against a capture taken on another day produces a guaranteed mismatch
/// that says nothing about either source. ADR-0014 asks for two independent
/// observations of the same fact; "the same fact" includes when.
/// </para>
/// <para>
/// Sniff only. <see cref="WinDivertPacketSource"/> opens with
/// <c>FlagSniff | FlagRecvOnly</c>, so packets are copied and never dropped,
/// altered or injected. Recording is observation, and stays on the observation
/// side of ADR-0014.
/// </para>
/// </remarks>
public static class WireRecorder
{
    public const string Flag = "--record-wire";

    /// <summary>Where the replay commands look, so a recording lands where they will find it.</summary>
    public const string DefaultDirectory = "data";

    public const string EndpointMissingReason = "record_endpoint_missing";
    public const string EndpointMalformedPrefix = "record_endpoint_malformed";
    public const string HostNotAnIpPrefix = "record_host_not_an_ip";
    public const string PortImplausiblePrefix = "record_port_implausible";
    public const string DriverUnavailablePrefix = "record_driver_unavailable";
    public const string PathUnwritablePrefix = "record_path_unwritable";
    public const string NoPacketsReason = "record_no_packets";

    /// <summary>
    /// Parses <c>ip:port</c>.
    /// </summary>
    /// <remarks>
    /// An IPv4 literal, not a host name: the WinDivert filter is written against
    /// <c>ip.SrcAddr</c> / <c>ip.DstAddr</c>, so a name that resolved to several
    /// addresses would capture one of them and silently miss the rest. The same
    /// rule already governs <c>--observe-game</c>.
    /// </remarks>
    public static bool TryParseEndpoint(
        string? text, out IPAddress address, out int port, out string? failureReason)
    {
        address = IPAddress.None;
        port = 0;

        if (string.IsNullOrWhiteSpace(text))
        {
            failureReason = EndpointMissingReason;
            return false;
        }

        string trimmed = text.Trim();
        int separator = trimmed.LastIndexOf(':');
        if (separator <= 0 || separator == trimmed.Length - 1)
        {
            failureReason = string.Create(CultureInfo.InvariantCulture, $"{EndpointMalformedPrefix}:{trimmed}");
            return false;
        }

        string host = trimmed[..separator];
        string portText = trimmed[(separator + 1)..];

        if (!IPAddress.TryParse(host, out IPAddress? parsed)
            || parsed.AddressFamily != AddressFamily.InterNetwork)
        {
            failureReason = string.Create(CultureInfo.InvariantCulture, $"{HostNotAnIpPrefix}:{host}");
            return false;
        }

        if (!int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedPort)
            || parsedPort < 1
            || parsedPort > 65535)
        {
            failureReason = string.Create(CultureInfo.InvariantCulture, $"{PortImplausiblePrefix}:{portText}");
            return false;
        }

        address = parsed;
        port = parsedPort;
        failureReason = null;
        return true;
    }

    /// <summary>A name that sorts by when it was taken, under the directory the readers scan.</summary>
    public static string DefaultPath(DateTime nowUtc) => Path.Combine(
        DefaultDirectory,
        string.Create(CultureInfo.InvariantCulture, $"nostale_{nowUtc.ToUniversalTime():yyyyMMdd_HHmmss}Z.noscap"));

    /// <summary>
    /// Drains a source into a recording and says whether the result is usable.
    /// </summary>
    /// <remarks>
    /// Separated from <see cref="Run"/> so the outcome rules are exercised
    /// against a scripted source with no driver and no game, the way the rest of
    /// the capture engine already is.
    /// </remarks>
    public static RecordingOutcome RecordFrom(
        IPacketSource source, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        long written;
        try
        {
            written = CaptureFile.Record(source, path, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new RecordingOutcome(0, path, string.Create(CultureInfo.InvariantCulture,
                $"{PathUnwritablePrefix}:{ex.GetType().Name}"));
        }

        // Zero packets is a well-formed file with a header and nothing else. It
        // would be accepted by every reader and corroborate nothing, so it is
        // refused here rather than discovered three commands later.
        return written == 0
            ? new RecordingOutcome(0, path, NoPacketsReason)
            : new RecordingOutcome(written, path, null);
    }

    /// <summary>Opens the driver on one endpoint and records until stopped.</summary>
    /// <param name="endpoint">The game server as <c>ip:port</c>.</param>
    /// <param name="path">Where to write, or null for <see cref="DefaultPath"/>.</param>
    /// <param name="seconds">Stop after this many seconds, or 0 to run until Ctrl+C.</param>
    public static int Run(string? endpoint, string? path = null, int seconds = 0)
    {
        if (!TryParseEndpoint(endpoint, out IPAddress address, out int port, out string? endpointFailure))
        {
            Console.WriteLine($"[REFUSED] {endpointFailure}");
            Console.WriteLine($"Usage: {Flag} <ip>:<port> [file.noscap] [--watch N]");
            return 2;
        }

        string target = string.IsNullOrWhiteSpace(path) ? DefaultPath(DateTime.UtcNow) : path!;

        WinDivertPacketSource? source = WinDivertPacketSource.TryOpen(address, port, out string? driverFailure);
        if (source is null)
        {
            Console.WriteLine($"[REFUSED] {DriverUnavailablePrefix}:{driverFailure}");
            return 1;
        }

        using (source)
        using (var stopping = new CancellationTokenSource())
        {
            if (seconds > 0)
                stopping.CancelAfter(TimeSpan.FromSeconds(seconds));

            // Cancel rather than let the runtime kill the process: Record only
            // flushes when its loop exits, so a hard Ctrl+C would cost the
            // operator the recording they just sat through.
            ConsoleCancelEventHandler onCancel = (_, e) =>
            {
                e.Cancel = true;
                stopping.Cancel();
            };
            Console.CancelKeyPress += onCancel;

            try
            {
                Console.WriteLine("=== recording the wire (sniff only; nothing is altered) ===");
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"server: {address}:{port}"));
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"file:   {Path.GetFullPath(target)}"));
                Console.WriteLine(seconds > 0
                    ? string.Create(CultureInfo.InvariantCulture, $"stop:   after {seconds}s, or Ctrl+C")
                    : "stop:   Ctrl+C");
                Console.WriteLine();
                Console.WriteLine("Play the session you want to certify. Read the memory probes against");
                Console.WriteLine("this file, not an archived one: a second source has to be observed at");
                Console.WriteLine("the same moment as the reading it corroborates.");
                Console.WriteLine();

                RecordingOutcome outcome = RecordFrom(source, target, stopping.Token);
                if (!outcome.Ok)
                {
                    Console.WriteLine($"[REFUSED] {outcome.FailureReason}");
                    return 1;
                }

                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{outcome.Packets} packets -> {Path.GetFullPath(outcome.Path)}"));
                return 0;
            }
            finally
            {
                Console.CancelKeyPress -= onCancel;
            }
        }
    }
}
