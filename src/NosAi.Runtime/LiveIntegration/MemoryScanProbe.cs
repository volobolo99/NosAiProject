using System.Globalization;
using System.Runtime.Versioning;
using NosAi.Runtime.Security;

namespace NosAi.LiveIntegration;

/// <summary>
/// The console front end for <see cref="MemoryScanner"/>: finds where the client
/// keeps a value the operator can see on screen.
/// </summary>
/// <remarks>
/// <para>
/// A probe rather than a suite, for the same reason <c>--dxgi-probe</c> is one:
/// no test on a build machine can find an offset in a game that is not running.
/// </para>
/// <para>
/// The candidate set is written to disk between invocations because that is what
/// the method requires. One scan proves nothing -- an address is identified by
/// surviving several narrowings across changes the operator makes in game, and
/// each narrowing is a separate run of this command.
/// </para>
/// </remarks>
public static class MemoryScanProbe
{
    /// <summary>Where the candidate set lives between passes.</summary>
    public const string CandidatePath = "data/memory_scan_candidates.txt";

    /// <summary>How many addresses to print; the file holds them all.</summary>
    private const int PrintLimit = 12;

    public static int Run(string[] args, int flagIndex)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Memory scanning needs Windows.");
            return 2;
        }

        if (string.Equals(args[flagIndex], "--memory-dump", StringComparison.OrdinalIgnoreCase))
            return RunDump(args, flagIndex);

        bool narrowing = string.Equals(args[flagIndex], "--memory-narrow", StringComparison.OrdinalIgnoreCase);

        if (!TryReadOperands(args, flagIndex, out int processId, out int value, out string? error))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine("Usage: --memory-scan <pid> <value>              first pass, saves candidates");
            Console.Error.WriteLine("       --memory-narrow <pid> <value>            later pass, narrows them");
            Console.Error.WriteLine("       --memory-dump <pid> <hexAddr> [bytes]    read the struct around an address");
            return 2;
        }

        return Execute(processId, value, narrowing);
    }

    /// <summary>
    /// Prints the 32-bit words around an address, so the fields beside a value
    /// that has already been identified can be read off.
    /// </summary>
    /// <remarks>
    /// Once HP is pinned, MaxHP and MP do not need their own scans: a character's
    /// vitals sit together in one structure, so they are almost always a few words
    /// from each other. Reading the neighbourhood turns two more narrowing sessions
    /// into one look, and the offsets it suggests are still only candidates until a
    /// validity check confirms them.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    private static int RunDump(string[] args, int flagIndex)
    {
        if (flagIndex + 2 >= args.Length
            || !int.TryParse(args[flagIndex + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int processId)
            || !long.TryParse(args[flagIndex + 2].TrimStart('0', 'x', 'X').PadLeft(1, '0'),
                              NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long address))
        {
            Console.Error.WriteLine("Usage: --memory-dump <pid> <hexAddr> [bytes]");
            return 2;
        }

        int length = 128;
        if (flagIndex + 3 < args.Length
            && int.TryParse(args[flagIndex + 3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int requested))
            length = Math.Clamp(requested, 16, 4096);

        using ProcessMemoryReader? reader = ProcessMemoryReader.TryOpen(
            processId, SecurityPrincipal.Operator, out string? failure);
        if (reader is null)
        {
            Console.Error.WriteLine($"Cannot read process {processId}: {failure}");
            return 1;
        }

        // Centred on the address: a vitals struct is as likely to run backwards
        // from the field that was found as forwards.
        long start = address - (length / 2);
        MemoryReadResult read = reader.Read(new IntPtr(start), length);
        if (!read.Ok)
        {
            // The region may begin at the address itself; fall back to reading forward.
            start = address;
            read = reader.Read(new IntPtr(start), length);
            if (!read.Ok)
            {
                Console.Error.WriteLine($"Read failed at 0x{address:X}: {read.FailureReason}");
                return 1;
            }
        }

        Console.WriteLine($"{length} bytes around 0x{address:X} in process {processId}:");
        for (int i = 0; i + 4 <= read.Bytes.Length; i += 4)
        {
            long at = start + i;
            int word = BitConverter.ToInt32(read.Bytes, i);
            string marker = at == address ? "  <-- the address found" : string.Empty;
            Console.WriteLine($"  0x{at:X}  {(at - address >= 0 ? "+" : "-")}{Math.Abs(at - address):D3}  {word,12}  0x{word:X8}{marker}");
        }

        return 0;
    }

    [SupportedOSPlatform("windows")]
    private static int Execute(int processId, int value, bool narrowing)
    {
        // Through the same authorization gate as every other read of this process.
        // A diagnostic is not a reason to go around the policy (ADR-0003).
        using ProcessMemoryReader? reader = ProcessMemoryReader.TryOpen(
            processId, SecurityPrincipal.Operator, out string? failure);

        if (reader is null)
        {
            Console.Error.WriteLine($"Cannot read process {processId}: {failure}");
            if (failure is not null && failure.Contains("access_denied", StringComparison.Ordinal))
                Console.Error.WriteLine("  The client runs at the same integrity level or higher; try an elevated console.");
            return 1;
        }

        MemoryScanner.ScanResult result;
        if (narrowing)
        {
            if (!TryLoadCandidates(out List<IntPtr> candidates, out int passes, out string? loadError))
            {
                Console.Error.WriteLine(loadError);
                return 1;
            }

            Console.WriteLine($"Narrowing {candidates.Count} candidates against {value}...");
            result = MemoryScanner.Narrow(reader, candidates, value, passes);
        }
        else
        {
            Console.WriteLine($"Scanning process {processId} for {value} (private regions)...");
            result = MemoryScanner.Scan(reader, value);
            Console.WriteLine($"  regions={result.RegionsScanned} bytesRead={result.BytesScanned:N0}");
        }

        SaveCandidates(result);

        Console.WriteLine($"Candidates: {result.Addresses.Count}{(result.Truncated ? "+ (capped)" : string.Empty)}  pass={result.Passes}");
        foreach (IntPtr address in result.Addresses.Take(PrintLimit))
            Console.WriteLine($"  0x{address.ToInt64():X}");
        if (result.Addresses.Count > PrintLimit)
            Console.WriteLine($"  ... and {result.Addresses.Count - PrintLimit} more, all in {CandidatePath}");

        Console.WriteLine();
        Console.WriteLine(result.Advice);

        // Non-zero until an address has actually been established, so a script
        // cannot mistake "still narrowing" for "found it".
        return result.IsConclusive ? 0 : 1;
    }

    private static bool TryReadOperands(
        string[] args, int flagIndex, out int processId, out int value, out string? error)
    {
        processId = 0;
        value = 0;
        error = null;

        if (flagIndex + 2 >= args.Length)
        {
            error = "Both a process id and a value are required.";
            return false;
        }

        if (!int.TryParse(args[flagIndex + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out processId)
            || processId <= 0)
        {
            error = $"Not a process id: {args[flagIndex + 1]}";
            return false;
        }

        if (!int.TryParse(args[flagIndex + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            error = $"Not a 32-bit value: {args[flagIndex + 2]}";
            return false;
        }

        return true;
    }

    private static bool TryLoadCandidates(out List<IntPtr> candidates, out int passes, out string? error)
    {
        candidates = new List<IntPtr>();
        passes = 1;
        error = null;

        if (!File.Exists(CandidatePath))
        {
            error = $"No candidate set at {CandidatePath}. Run --memory-scan first.";
            return false;
        }

        foreach (string raw in File.ReadAllLines(CandidatePath))
        {
            string line = raw.Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith("# passes=", StringComparison.Ordinal))
            {
                if (int.TryParse(line["# passes=".Length..], out int parsed))
                    passes = parsed;
                continue;
            }

            if (line.StartsWith('#'))
                continue;

            if (long.TryParse(line, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long address))
                candidates.Add(new IntPtr(address));
        }

        if (candidates.Count == 0)
        {
            error = $"{CandidatePath} holds no addresses. Run --memory-scan again.";
            return false;
        }

        return true;
    }

    private static void SaveCandidates(MemoryScanner.ScanResult result)
    {
        string? directory = Path.GetDirectoryName(CandidatePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var lines = new List<string>(result.Addresses.Count + 2)
        {
            $"# passes={result.Passes}",
            $"# candidates={result.Addresses.Count}"
        };
        lines.AddRange(result.Addresses.Select(a => a.ToInt64().ToString("X", CultureInfo.InvariantCulture)));
        File.WriteAllLines(CandidatePath, lines);
    }
}
