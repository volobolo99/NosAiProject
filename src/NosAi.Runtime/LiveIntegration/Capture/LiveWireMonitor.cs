using System.Globalization;
using System.Net;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;

namespace NosAi.LiveIntegration.Capture;

/// <summary>
/// Reads the live wire and prints every frame the instant it arrives: the raw
/// tokenised line for every opcode, and the client-state reading for the
/// opcodes <see cref="NosTaleWorldProtocolDecoder"/> knows.
/// </summary>
/// <remarks>
/// <para>
/// Sniff only, exactly like <see cref="WireRecorder"/>: the packet source opens
/// with <c>FlagSniff | FlagRecvOnly</c>, so nothing is dropped, altered or
/// injected. This is Observe and nothing past it — no Guard, no Trust, no
/// Safety, no Execute, because it never acts on the game.
/// </para>
/// <para>
/// Two lines print per readable inbound frame, and they answer two different
/// questions. <c>RETE</c> is the wire's own text, tokenised by
/// <see cref="NosTaleWorldDecoder"/>: printed for every opcode, known or not,
/// because that line is a fact about the bytes and needs no decoder to be
/// true — this is "tutti i dati di rete". <c>CLIENT</c> is the same packet run
/// through the real <see cref="NosTaleWorldProtocolDecoder"/>, printed only
/// when it produced a reading; an opcode outside the twelve it reads (at last
/// count: <c>eq</c>, <c>equip</c>, <c>bf</c>, <c>pairy</c>, <c>sc</c>,
/// <c>dir</c>, <c>eff</c>) or a packet that failed its shape check prints no
/// <c>CLIENT</c> line at all rather than a guessed one — the decoder never
/// invents a reading, and neither does this monitor.
/// </para>
/// <para>
/// An outbound frame is client-&gt;server and, on this wire, encrypted: the
/// framer marks it <see cref="DataSourceKind.Unknown"/> and this prints
/// <c>&lt;non decifrabile&gt;</c> rather than bytes it cannot account for.
/// </para>
/// <para>
/// <see cref="Monitor"/> is the part <see cref="Run"/> hands off to once it has
/// a real <see cref="IPacketSource"/> — driver-backed on the console entry, an
/// <see cref="InMemoryPacketSource"/> in tests. Splitting them is what lets the
/// decode-and-print logic be exercised without a WinDivert driver, the same
/// separation <see cref="WireRecorder.RecordFrom"/> already uses for recording.
/// </para>
/// </remarks>
public static class LiveWireMonitor
{
    public const string Flag = "--live-decode";

    /// <summary>What one monitoring run counted, once the source ended or was cancelled.</summary>
    public readonly record struct Summary(
        long ReadableFrames, long Interpreted, long NotInterpreted, long Undecipherable);

    /// <summary>Opens the driver on one endpoint and prints until stopped.</summary>
    /// <param name="endpoint">The game server as <c>ip:port</c>.</param>
    /// <param name="seconds">Stop after this many seconds, or 0 to run until Ctrl+C.</param>
    public static int Run(string? endpoint, int seconds = 0)
    {
        if (!WireRecorder.TryParseEndpoint(endpoint, out IPAddress address, out int port, out string? endpointFailure))
        {
            Console.WriteLine($"[REFUSED] {endpointFailure}");
            Console.WriteLine($"Usage: {Flag} <ip>:<port> [--watch N]");
            return 2;
        }

        WinDivertPacketSource? source = WinDivertPacketSource.TryOpen(address, port, out string? driverFailure);
        if (source is null)
        {
            Console.WriteLine($"[REFUSED] {WireRecorder.DriverUnavailablePrefix}:{driverFailure}");
            return 1;
        }

        using (source)
        using (var stopping = new CancellationTokenSource())
        {
            if (seconds > 0)
                stopping.CancelAfter(TimeSpan.FromSeconds(seconds));

            // Same shape as WireRecorder.Run: the first Ctrl+C stops the pump
            // cleanly so the summary line at the end still prints; a second one
            // still has to be able to kill the process.
            var interrupts = 0;
            ConsoleCancelEventHandler onCancel = (_, e) =>
            {
                if (Interlocked.Increment(ref interrupts) > 1)
                    return;
                e.Cancel = true;
                stopping.Cancel();
            };
            Console.CancelKeyPress += onCancel;

            Summary summary;
            try
            {
                Console.WriteLine("=== decodifica dal vivo (sniff only; nulla viene alterato) ===");
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"server: {address}:{port}"));
                Console.WriteLine(seconds > 0
                    ? string.Create(CultureInfo.InvariantCulture, $"durata: {seconds}s, oppure Ctrl+C")
                    : "durata: Ctrl+C");
                Console.WriteLine("RETE   = ogni riga grezza vista sul filo, qualunque sia l'opcode.");
                Console.WriteLine("CLIENT = la lettura del decoder reale, solo quando l'opcode e' tra quelli letti.");
                Console.WriteLine();

                summary = Monitor(source, Console.Out, stopping.Token);
            }
            finally
            {
                Console.CancelKeyPress -= onCancel;
            }

            Console.WriteLine();
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"frame leggibili: {summary.ReadableFrames}  interpretati: {summary.Interpreted}  non interpretati: {summary.NotInterpreted}  non decifrabili: {summary.Undecipherable}"));
            return 0;
        }
    }

    /// <summary>
    /// Pumps <paramref name="source"/> through the world-channel decoder,
    /// writing every line to <paramref name="output"/> as it is produced.
    /// </summary>
    /// <remarks>
    /// No driver, no console, no elevation: a finite source such as
    /// <see cref="InMemoryPacketSource"/> runs this to completion on its own,
    /// which is what lets the decode-and-print behaviour be pinned in a test.
    /// </remarks>
    public static Summary Monitor(IPacketSource source, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);

        var decoder = new NosTaleWorldProtocolDecoder();
        string host = source.ServerAddress.ToString();
        int port = source.ServerPort;
        long frames = 0, interpreted = 0, notInterpreted = 0, undecipherable = 0;

        var engine = new GameTrafficCaptureEngine(source, NosTaleWorldFramer.Factory(DataSourceKind.Live));
        engine.FrameProduced += frame =>
        {
            string time = frame.TimestampUtc.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            string dir = frame.Direction == StreamDirection.Inbound ? "IN " : "OUT";

            if (frame.Frame.Source == DataSourceKind.Unknown)
            {
                Interlocked.Increment(ref undecipherable);
                output.WriteLine($"[{time}] {dir} <non decifrabile>");
                output.Flush();
                return;
            }

            Interlocked.Increment(ref frames);
            foreach (string line in NosTaleWorldDecoder.Decode(frame.Frame.Body.Span))
                output.WriteLine($"[{time}] {dir} RETE   {line}");

            if (frame.Direction == StreamDirection.Inbound)
            {
                var packet = new ObservedPacket(
                    frame.TimestampUtc, NetworkDirection.Inbound, host, port,
                    frame.Frame.Body, frame.Frame.Source);
                DecodedObservations observed = decoder.Decode(packet);
                if (observed.IsEmpty)
                {
                    Interlocked.Increment(ref notInterpreted);
                }
                else
                {
                    Interlocked.Increment(ref interpreted);
                    output.WriteLine($"[{time}] {dir} CLIENT {Describe(observed)}");
                }
            }

            output.Flush();
        };

        engine.Run(cancellationToken);
        return new Summary(frames, interpreted, notInterpreted, undecipherable);
    }

    /// <summary>Every field <see cref="DecodedObservations"/> carries, on one line.</summary>
    private static string Describe(DecodedObservations o)
    {
        var parts = new List<string>();

        if (o.Vitals is { } vitals)
        {
            string maxMp = vitals.MaxMp is { } mm ? mm.ToString(CultureInfo.InvariantCulture) : "?";
            parts.Add($"vitals hp={vitals.Hp}/{vitals.MaxHp} mp={vitals.Mp}/{maxMp}");
        }

        foreach (EntitySighting s in o.Sightings)
        {
            string hp = s.HpRatio is { } ratio ? ratio.ToString("0.00", CultureInfo.InvariantCulture) : "assente";
            parts.Add($"sighting id={s.EntityId} kind={s.Kind} pos={s.X},{s.Y} hp={hp}");
        }

        foreach (GameEvent e in o.Events)
            parts.Add($"event {e.Kind} id={e.EntityId} {e.Descriptor}");

        if (o.PlayerEntityId is { } playerId)
            parts.Add($"player_id={playerId}");
        if (o.PlayerMovementSpeed is { } speed)
            parts.Add($"speed={speed}");
        if (o.PlayerAttackedAtUtc is not null)
            parts.Add("player_attacked");
        if (o.PlayerHit is { } hit)
            parts.Add($"hit_by id={hit.By.EntityId} type={hit.By.EntityType}");
        if (o.SkillReady is { } ready)
            parts.Add($"skill_ready slot={ready.Slot}");
        if (o.InventorySlot is { } slot)
            parts.Add($"inventory kind={slot.InventoryKind} slot={slot.Slot} vnum={slot.Vnum} amount={slot.Amount} rarity={slot.Rarity}");
        if (o.Pickup is { } pickup)
            parts.Add($"pickup takerType={pickup.TakerType} takerId={pickup.TakerId} dropId={pickup.DropId}");
        if (o.GroundItem is { } ground)
            parts.Add($"ground vnum={ground.Vnum} dropId={ground.DropId} pos={ground.X},{ground.Y} amount={ground.Amount}");
        if (o.PlayerTarget is { } target)
            parts.Add($"target id={target.Target.EntityId} type={target.Target.EntityType}");

        return parts.Count > 0 ? string.Join("  ", parts) : "(vuoto)";
    }
}
