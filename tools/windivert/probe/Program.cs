using System.Net;
using NosAi.LiveIntegration.Capture;

// WinDivert capture probe (ADR-0014).
//
// Run ELEVATED. Opening WinDivertPacketSource registers the kernel driver on its
// first use — so this both installs the driver and proves capture, using the
// runtime's own capture components rather than a second copy of the P/Invoke.
//
// It captures in SNIFF (read-only): the game keeps running, nothing is modified
// or injected. With --record it also writes a .noscap file, so the traffic can be
// replayed offline while a NosTale decoder is written, without the driver.
//
// Usage:
//   WinDivertProbe.exe <server-ip> [port] [seconds] [--record <file>]
//   WinDivertProbe.exe 79.110.84.175 4006 15 --record data/session.noscap

if (args.Length < 1)
{
    Console.WriteLine("Uso:");
    Console.WriteLine("  cattura : WinDivertProbe.exe <ip-server> [porta] [secondi] [--record <file>]");
    Console.WriteLine("  analisi : WinDivertProbe.exe --analyze <file.noscap>   (nessun driver)");
    Console.WriteLine("  lettura : WinDivertProbe.exe --world <file.noscap>     (nessun driver)");
    return 1;
}

// Offline reading of a recording as the world channel: framer, decoder and the
// observations they produce. Where --analyze measures the bytes without claiming
// a meaning, this reads them and says what it read — so the numbers can be held
// against the client's own HUD, which is the only independent check there is.
if (args[0] == "--world")
{
    if (args.Length < 2)
    {
        Console.WriteLine("Uso: WinDivertProbe.exe --world <file.noscap>");
        return 1;
    }
    try
    {
        Console.WriteLine(WorldChannelReplay.ReplayFile(args[1]).Describe());
        return 0;
    }
    catch (Exception ex) when (ex is IOException or InvalidDataException)
    {
        Console.WriteLine($"Lettura fallita: {ex.Message}");
        return 2;
    }
}

// Offline analysis of a recording — no driver, no elevation, no game running.
if (args[0] == "--analyze")
{
    if (args.Length < 2)
    {
        Console.WriteLine("Uso: WinDivertProbe.exe --analyze <file.noscap>");
        return 1;
    }
    try
    {
        Console.WriteLine(CaptureAnalyzer.AnalyzeFile(args[1]).Describe());
        return 0;
    }
    catch (Exception ex) when (ex is IOException or InvalidDataException)
    {
        Console.WriteLine($"Analisi fallita: {ex.Message}");
        return 2;
    }
}

var server = IPAddress.Parse(args[0]);
int port = args.Length > 1 && int.TryParse(args[1], out var p) ? p : 4006;
int seconds = args.Length > 2 && int.TryParse(args[2], out var s) ? s : 15;

string? recordPath = null;
for (int i = 3; i < args.Length - 1; i++)
{
    if (args[i] == "--record")
        recordPath = args[i + 1];
}

Console.WriteLine($"Server : {server}:{port}");
Console.WriteLine($"Durata : {seconds}s  (SNIFF, sola lettura)");
if (recordPath is not null)
    Console.WriteLine($"Record : {recordPath}");
Console.WriteLine();

var source = WinDivertPacketSource.TryOpen(server, port, out string? reason);
if (source is null)
{
    Console.WriteLine($"Apertura fallita: {reason}");
    Console.WriteLine(reason switch
    {
        "access_denied_run_elevated" => "  Eseguire come amministratore.",
        "windivert_dll_not_found" or "windivert_driver_not_found" =>
            "  WinDivert.dll / WinDivert64.sys non accanto all'exe.",
        "driver_signature_rejected" or "driver_blocked" =>
            "  Il driver e' rifiutato o bloccato dal sistema.",
        _ => "  Vedere la documentazione WinDivert."
    });
    return 2;
}

using (source)
{
    Console.WriteLine("Driver aperto. WinDivert e' installato e in ascolto.");
    Console.WriteLine();

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));

    if (recordPath is not null)
    {
        long written = CaptureFile.Record(source, recordPath, cts.Token);
        Console.WriteLine($"Registrati {written} pacchetti in {recordPath}.");
        Console.WriteLine("Ora la sessione si puo' rigiocare e decodificare offline, senza driver.");
        return 0;
    }

    var engine = new GameTrafficCaptureEngine(source);
    long shown = 0;
    engine.FrameProduced += frame =>
    {
        if (shown++ < 8)
        {
            string dir = frame.Direction == StreamDirection.Outbound ? "PC  -> server" : "server -> PC ";
            Console.WriteLine($"  {dir}  {frame.Frame.Body.Length,5} byte  [{frame.Frame.Source}]");
        }
    };

    var summary = engine.Run(cts.Token);
    Console.WriteLine();
    Console.WriteLine($"Pacchetti letti  : {summary.PacketsRead}");
    Console.WriteLine($"PC   -> server   : {summary.OutboundBytes} byte");
    Console.WriteLine($"server -> PC     : {summary.InboundBytes} byte");
    Console.WriteLine($"Frame            : {summary.FramesProduced} (UNKNOWN: {summary.UnknownFrames})");
    Console.WriteLine();
    Console.WriteLine(summary.PacketsRead > 0
        ? "Cattura reale confermata. Il driver resta installato per gli usi successivi."
        : "Nessun pacchetto nella finestra: verificare IP/porta e che il client fosse connesso.");
}

return 0;
