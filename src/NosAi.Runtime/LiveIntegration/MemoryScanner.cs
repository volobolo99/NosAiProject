using System.Runtime.Versioning;

namespace NosAi.LiveIntegration;

/// <summary>
/// Finds the address of a value the game holds, by looking for the value itself.
/// </summary>
/// <remarks>
/// <para>
/// An offset has to come from somewhere. ADR-0012 said an operator derives it by
/// correlating what the client shows against what the machine holds, and ADR-0014
/// left that path open; this is the tool that makes the correlation possible
/// rather than a matter of guessing.
/// </para>
/// <para>
/// <b>One pass is never an answer.</b> A first scan for a number like 742 returns
/// thousands of addresses, nearly all of them unrelated integers that happen to
/// equal it. The address is found by <i>elimination</i>: scan, let the value change
/// in game, narrow to the addresses that changed with it, and repeat until one
/// candidate survives several independent changes. A candidate set that has not
/// been narrowed is not a result, and <see cref="ScanResult.IsConclusive"/> says so.
/// </para>
/// <para>
/// <b>Read-only.</b> There is no write path here and there is not going to be one
/// by accident: ADR-0014 opened reading the client, and altering it is a different
/// capability with a different decision behind it.
/// </para>
/// <para>
/// Private regions only, by default. The same integer inside a mapped image is a
/// constant compiled into the binary, not the character's current state; following
/// one would pin an offset that reads cleanly forever and never means anything.
/// </para>
/// </remarks>
public static class MemoryScanner
{
    /// <summary>Largest chunk read from the target in one call.</summary>
    private const int ChunkSize = 64 * 1024;

    /// <summary>
    /// How many candidates a first scan will carry before giving up on being useful.
    /// </summary>
    /// <remarks>
    /// A cap rather than an unbounded list: a scan for a small number such as 1 or
    /// 100 matches a large part of the address space, and the honest response is to
    /// say the value is too common to locate this way, not to return a million
    /// addresses the operator cannot narrow.
    /// </remarks>
    public const int MaxCandidates = 500_000;

    /// <summary>The addresses that currently hold a value, and whether that means anything yet.</summary>
    /// <param name="Addresses">Every address whose four bytes equal the value scanned for.</param>
    /// <param name="Passes">How many independent scans have narrowed this set.</param>
    /// <param name="RegionsScanned">Committed readable regions examined.</param>
    /// <param name="BytesScanned">Total bytes read.</param>
    /// <param name="Truncated">Whether <see cref="MaxCandidates"/> stopped the scan early.</param>
    public sealed record ScanResult(
        IReadOnlyList<IntPtr> Addresses,
        int Passes,
        int RegionsScanned,
        long BytesScanned,
        bool Truncated)
    {
        /// <summary>
        /// Whether this set identifies one address after surviving enough narrowing
        /// to mean it.
        /// </summary>
        /// <remarks>
        /// Two passes, not one: a single scan that happens to return one address has
        /// not been tested against a change, so nothing has distinguished it from an
        /// unrelated integer that held the same value once.
        /// </remarks>
        public bool IsConclusive => Addresses.Count == 1 && Passes >= 2;

        /// <summary>What the operator should do next, in one line.</summary>
        public string Advice => Addresses.Count switch
        {
            0 => "No address holds that value. Check the number shown in the client, and that this is the right process.",
            1 when Passes >= 2 => "One address survived narrowing. Confirm it once more after another change before trusting it.",
            1 => "One address, but only one pass. Change the value in game and narrow again before trusting it.",
            _ when Truncated => $"{Addresses.Count}+ candidates (capped). That value is too common; change it in game and narrow.",
            _ => $"{Addresses.Count} candidates. Change the value in game and narrow against the new number."
        };
    }

    /// <summary>
    /// First pass: every address in the target holding <paramref name="value"/>.
    /// </summary>
    /// <param name="privateRegionsOnly">
    /// Restrict to MEM_PRIVATE. Leave true unless looking for something that really
    /// does live in a mapped image.
    /// </param>
    [SupportedOSPlatform("windows")]
    public static ScanResult Scan(ProcessMemoryReader reader, int value, bool privateRegionsOnly = true)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var found = new List<IntPtr>();
        byte[] needle = BitConverter.GetBytes(value);
        int regions = 0;
        long bytes = 0;
        bool truncated = false;

        foreach (MemoryRegion region in reader.EnumerateRegions())
        {
            if (privateRegionsOnly && !region.IsPrivate)
                continue;

            regions++;
            long offset = 0;
            while (offset < region.Size)
            {
                int length = (int)Math.Min(ChunkSize, region.Size - offset);
                var address = new IntPtr(region.BaseAddress.ToInt64() + offset);

                MemoryReadResult read = reader.Read(address, length);
                if (!read.Ok)
                {
                    // A region can stop being readable between the query and the
                    // read; that is normal in a live process and not a failure of
                    // the scan. Skip the chunk rather than abandoning the pass.
                    offset += length;
                    continue;
                }

                bytes += length;
                ReadOnlySpan<byte> window = read.Bytes;
                for (int i = 0; i + 4 <= window.Length; i += 4)
                {
                    if (window[i] == needle[0] && window[i + 1] == needle[1]
                        && window[i + 2] == needle[2] && window[i + 3] == needle[3])
                    {
                        found.Add(new IntPtr(address.ToInt64() + i));
                        if (found.Count >= MaxCandidates)
                        {
                            truncated = true;
                            return new ScanResult(found, 1, regions, bytes, truncated);
                        }
                    }
                }

                offset += length;
            }
        }

        return new ScanResult(found, 1, regions, bytes, truncated);
    }

    /// <summary>
    /// Later passes: which of <paramref name="candidates"/> now hold <paramref name="value"/>.
    /// </summary>
    /// <remarks>
    /// This is where an address is actually identified. Each narrowing against a
    /// value the operator changed in game eliminates the integers that merely
    /// coincided with the previous one, and an address that tracks several
    /// independent changes is holding the thing itself.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    public static ScanResult Narrow(
        ProcessMemoryReader reader, IReadOnlyList<IntPtr> candidates, int value, int previousPasses = 1)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(candidates);

        var survivors = new List<IntPtr>();
        long bytes = 0;

        foreach (IntPtr address in candidates)
        {
            MemoryReadResult read = reader.Read(address, sizeof(int));
            if (!read.Ok)
                continue;   // Freed or unmapped since the last pass: it was not the one.

            bytes += sizeof(int);
            if (BitConverter.ToInt32(read.Bytes) == value)
                survivors.Add(address);
        }

        return new ScanResult(survivors, previousPasses + 1, 0, bytes, Truncated: false);
    }
}
