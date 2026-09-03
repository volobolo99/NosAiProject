using System.Globalization;
using System.Runtime.Versioning;

namespace NosAi.LiveIntegration;

/// <summary>Where a pointer lives, in order of how long it stays true.</summary>
/// <remarks>
/// The order is the ranking. A pointer inside the client's image survives a
/// restart because the distance from the module base does not move; one inside a
/// resolved base survives as long as that base is re-resolved on every read;
/// one on the heap survives nothing, which is what killed the address a reboot
/// took from this work.
/// </remarks>
public enum AnchorKind
{
    Module = 0,
    PlayerManager = 1,
    PlayerObject = 2,
    Heap = 3,
}

/// <summary>One pointer that reaches a target, and what it is anchored to.</summary>
/// <param name="Offset">
/// Distance from that kind's base, or the absolute address when the kind is
/// <see cref="AnchorKind.Heap"/> and there is no base to be distant from.
/// </param>
/// <param name="Points">The address the pointer holds.</param>
/// <param name="IntoTarget">How far past <see cref="Points"/> the target sits.</param>
public readonly record struct PointerAnchor(
    AnchorKind Kind, IntPtr Holder, long Offset, IntPtr Points, long IntoTarget)
{
    /// <summary>Whether following this again after a restart could still work.</summary>
    public bool IsDurable => Kind != AnchorKind.Heap;

    public string Describe() => Kind switch
    {
        AnchorKind.Heap => string.Create(CultureInfo.InvariantCulture,
            $"0x{Holder.ToInt64():X} (heap) -> +0x{IntoTarget:X}"),
        _ => string.Create(CultureInfo.InvariantCulture,
            $"{Kind}+0x{Offset:X} -> +0x{IntoTarget:X}"),
    };
}

/// <summary>
/// Finds what points at an address, so a calibrated heap address can become a
/// chain from something that is resolved again on every read.
/// </summary>
/// <remarks>
/// <para>
/// The calibrator proves which address holds health. It cannot make that address
/// last: it is heap, and a client restart moves it. What does last is the
/// distance from a base the runtime re-resolves — which is what the spec means
/// by expressing a result as an offset rather than an address, and why the
/// third-party source's own RVA is refused even where its offsets were right.
/// </para>
/// <para>
/// Pointers rarely aim at the field itself; they aim at the record holding it.
/// So the hunt keeps every word whose value lands in a window <i>ending</i> at
/// the target, and reports how far past that value the target sits. That
/// distance is the second half of the chain.
/// </para>
/// <para>
/// Image regions are scanned as well as private ones. A static pointer inside
/// the client's own image is the only kind that survives a restart, and
/// <see cref="MemoryScanner"/> skips exactly those by default.
/// </para>
/// </remarks>
public static class PointerAnchorHunter
{
    public const string Flag = "--anchor-hunt";

    /// <summary>How far before the target a pointer may aim and still be reaching it.</summary>
    /// <remarks>
    /// A record on this client measures 0x78, and the containing object is
    /// larger. 0x1000 is generous enough to catch the object without admitting
    /// every pointer into the heap block.
    /// </remarks>
    public const int DefaultSpan = 0x1000;

    /// <summary>The most holders reported before the hunt is called too broad.</summary>
    public const int MaxHolders = 4096;

    private const int ChunkSize = 64 * 1024;

    public const string NoHolderReason = "anchor_no_pointer_reaches_it";
    public const string OnlyHeapReason = "anchor_only_heap_holders";
    public const string TargetNullReason = "anchor_target_is_null";
    public const string SpanImplausiblePrefix = "anchor_span_implausible";

    /// <summary>Which base a holder belongs to, most durable first.</summary>
    public static AnchorKind Classify(
        IntPtr holder,
        IntPtr moduleBase,
        long moduleSize,
        IntPtr playerManager,
        IntPtr playerObject,
        int baseWindow)
    {
        long at = holder.ToInt64();

        if (moduleSize > 0)
        {
            long start = moduleBase.ToInt64();
            if (at >= start && at < start + moduleSize)
                return AnchorKind.Module;
        }

        if (Within(at, playerManager, baseWindow))
            return AnchorKind.PlayerManager;
        if (Within(at, playerObject, baseWindow))
            return AnchorKind.PlayerObject;

        return AnchorKind.Heap;
    }

    private static bool Within(long at, IntPtr baseAddress, int window)
    {
        if (baseAddress == IntPtr.Zero || window <= 0)
            return false;

        long start = baseAddress.ToInt64();
        return at >= start && at < start + window;
    }

    /// <summary>The offset to report for a holder of that kind.</summary>
    public static long OffsetFor(
        AnchorKind kind, IntPtr holder, IntPtr moduleBase, IntPtr playerManager, IntPtr playerObject) => kind switch
        {
            AnchorKind.Module => holder.ToInt64() - moduleBase.ToInt64(),
            AnchorKind.PlayerManager => holder.ToInt64() - playerManager.ToInt64(),
            AnchorKind.PlayerObject => holder.ToInt64() - playerObject.ToInt64(),
            _ => holder.ToInt64(),
        };

    /// <summary>
    /// The anchor worth reporting, or null when none of them is one.
    /// </summary>
    /// <remarks>
    /// Most durable wins, and among equals the one whose pointer lands closest
    /// to the target: a pointer that aims 8 bytes short is describing the record,
    /// one that aims 0xF00 short is describing something that merely contains it.
    /// </remarks>
    public static PointerAnchor? Best(IReadOnlyList<PointerAnchor> anchors)
    {
        ArgumentNullException.ThrowIfNull(anchors);

        PointerAnchor? best = null;
        foreach (PointerAnchor anchor in anchors)
        {
            if (best is not { } current)
            {
                best = anchor;
                continue;
            }

            if (anchor.Kind < current.Kind
                || (anchor.Kind == current.Kind && anchor.IntoTarget < current.IntoTarget))
            {
                best = anchor;
            }
        }

        return best;
    }

    /// <summary>The named refusal for a hunt that found nothing usable, or null.</summary>
    public static string? Verdict(IReadOnlyList<PointerAnchor> anchors)
    {
        ArgumentNullException.ThrowIfNull(anchors);

        if (anchors.Count == 0)
            return NoHolderReason;

        foreach (PointerAnchor anchor in anchors)
        {
            if (anchor.IsDurable)
                return null;
        }

        return OnlyHeapReason;
    }

    /// <summary>Turns raw holders into anchors against the resolved bases.</summary>
    public static List<PointerAnchor> Anchor(
        IReadOnlyList<(IntPtr Holder, IntPtr Points)> holders,
        IntPtr target,
        IntPtr moduleBase,
        long moduleSize,
        IntPtr playerManager,
        IntPtr playerObject,
        int baseWindow)
    {
        ArgumentNullException.ThrowIfNull(holders);

        var anchors = new List<PointerAnchor>(holders.Count);
        foreach ((IntPtr holder, IntPtr points) in holders)
        {
            AnchorKind kind = Classify(holder, moduleBase, moduleSize, playerManager, playerObject, baseWindow);
            long offset = OffsetFor(kind, holder, moduleBase, playerManager, playerObject);
            anchors.Add(new PointerAnchor(kind, holder, offset, points, target.ToInt64() - points.ToInt64()));
        }

        return anchors;
    }

    /// <summary>
    /// One pass over the process for every word pointing into the window that
    /// ends at <paramref name="target"/>.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static List<(IntPtr Holder, IntPtr Points)> FindPointersInto(
        ProcessMemoryReader reader, IntPtr target, int span, out bool truncated)
    {
        ArgumentNullException.ThrowIfNull(reader);

        truncated = false;
        var found = new List<(IntPtr, IntPtr)>();

        long high = target.ToInt64();
        long low = high - span;

        foreach (MemoryRegion region in reader.EnumerateRegions())
        {
            long offset = 0;
            while (offset < region.Size)
            {
                int length = (int)Math.Min(ChunkSize, region.Size - offset);
                var address = new IntPtr(region.BaseAddress.ToInt64() + offset);

                MemoryReadResult read = reader.Read(address, length);
                if (!read.Ok)
                {
                    // A region can stop being readable between the query and the
                    // read. Skipping the chunk is normal in a live process.
                    offset += length;
                    continue;
                }

                ReadOnlySpan<byte> window = read.Bytes;
                for (int i = 0; i + 4 <= window.Length; i += 4)
                {
                    long value = BitConverter.ToUInt32(window[i..(i + 4)]);
                    if (value < low || value > high)
                        continue;

                    found.Add((new IntPtr(address.ToInt64() + i), new IntPtr(value)));
                    if (found.Count >= MaxHolders)
                    {
                        truncated = true;
                        return found;
                    }
                }

                offset += length;
            }
        }

        return found;
    }

    /// <summary>
    /// Every target reached by a pointer, found in one pass rather than one pass
    /// each.
    /// </summary>
    /// <remarks>
    /// <see cref="FindPointersInto"/> walks the whole process for a single
    /// address, which is right when there is one address and hopeless when there
    /// are eighty-six: the cooldown hunt leaves a set of candidates and needs to
    /// know which of them anything points at. The work is the same walk with a
    /// sorted lookup per word, so the cost is one pass regardless of how many
    /// targets are asked about.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    public static Dictionary<long, List<(IntPtr Holder, IntPtr Points)>> FindPointersIntoAny(
        ProcessMemoryReader reader, IReadOnlyList<IntPtr> targets, int span)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(targets);
        if (span <= 0 || span > 0x100000) throw new ArgumentOutOfRangeException(nameof(span));

        long[] sorted = targets.Select(t => t.ToInt64()).Distinct().OrderBy(a => a).ToArray();
        var found = new Dictionary<long, List<(IntPtr, IntPtr)>>();
        if (sorted.Length == 0)
            return found;

        long lowest = sorted[0] - span;
        long highest = sorted[^1];

        foreach (MemoryRegion region in reader.EnumerateRegions())
        {
            long offset = 0;
            while (offset < region.Size)
            {
                int length = (int)Math.Min(ChunkSize, region.Size - offset);
                var address = new IntPtr(region.BaseAddress.ToInt64() + offset);

                MemoryReadResult read = reader.Read(address, length);
                if (!read.Ok)
                {
                    offset += length;
                    continue;
                }

                ReadOnlySpan<byte> window = read.Bytes;
                for (int i = 0; i + 4 <= window.Length; i += 4)
                {
                    long value = BitConverter.ToUInt32(window[i..(i + 4)]);
                    if (value < lowest || value > highest)
                        continue;

                    // The first target at or after this value, then every further
                    // target still inside the span: one pointer can name a record
                    // that holds several candidates.
                    int at = IndexOfFirstAtLeast(sorted, value);
                    for (int t = at; t < sorted.Length && sorted[t] - value <= span; t++)
                    {
                        if (!found.TryGetValue(sorted[t], out List<(IntPtr, IntPtr)>? holders))
                        {
                            holders = new List<(IntPtr, IntPtr)>();
                            found[sorted[t]] = holders;
                        }

                        holders.Add((new IntPtr(address.ToInt64() + i), new IntPtr(value)));
                    }
                }

                offset += length;
            }
        }

        return found;
    }

    /// <summary>Index of the first target at or after <paramref name="value"/>.</summary>
    /// <remarks>
    /// Internal to the batched walk and public only so it can be tested: it runs
    /// once per word over the whole process, so an off-by-one here is a pointer
    /// attributed to the wrong candidate, silently.
    /// </remarks>
    public static int IndexOfFirstAtLeast(long[] sorted, long value)
    {
        var low = 0;
        int high = sorted.Length;
        while (low < high)
        {
            int mid = low + ((high - low) / 2);
            if (sorted[mid] < value)
                low = mid + 1;
            else
                high = mid;
        }

        return low;
    }

    /// <summary>Hunts and prints the anchors for one target against an open session.</summary>
    /// <returns>0 when a durable anchor was found, 1 otherwise.</returns>
    [SupportedOSPlatform("windows")]
    public static int Report(ClientMemorySession session, IntPtr target, int span, string field)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        if (target == IntPtr.Zero)
        {
            Console.WriteLine($"{field}: [REFUSED] {TargetNullReason}");
            return 1;
        }

        if (span <= 0 || span > 0x100000)
        {
            Console.WriteLine($"{field}: [REFUSED] {SpanImplausiblePrefix}:{span}");
            return 1;
        }

        if (!session.TryResolveBases(out IntPtr manager, out IntPtr playerObject, out string? baseFailure))
        {
            // Without the bases only the module can anchor anything, so the hunt
            // still runs; it just has two fewer kinds to report.
            Console.WriteLine($"{field}: bases unresolved ({baseFailure}); only a module anchor can be named");
            manager = IntPtr.Zero;
            playerObject = IntPtr.Zero;
        }

        List<(IntPtr Holder, IntPtr Points)> holders =
            FindPointersInto(session.Reader, target, span, out bool truncated);

        List<PointerAnchor> anchors = Anchor(
            holders, target, session.ModuleBase, session.ModuleSize, manager, playerObject, DefaultSpan);

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{field}: 0x{target.ToInt64():X} <- {anchors.Count} pointer(s) within 0x{span:X}{(truncated ? " (capped)" : string.Empty)}"));

        foreach (PointerAnchor anchor in anchors)
        {
            if (anchor.IsDurable)
                Console.WriteLine($"    {anchor.Describe()}");
        }

        if (Verdict(anchors) is { } why)
        {
            Console.WriteLine($"{field}: [REFUSED] {why}");
            return 1;
        }

        PointerAnchor best = Best(anchors)!.Value;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{field}: ANCHOR {best.Describe()}"));
        Console.WriteLine(
            $"{field}: one chain is not an offset yet. Repeat after a client restart:");
        Console.WriteLine(
            $"{field}: a distance that does not survive one is an address that worked once.");
        return 0;
    }

    /// <summary>Attaches and hunts what points at one address.</summary>
    [SupportedOSPlatform("windows")]
    public static int Run(string? addressText, int span = DefaultSpan)
    {
        if (!TryParseAddress(addressText, out IntPtr target))
        {
            Console.WriteLine($"[REFUSED] {TargetNullReason}");
            Console.WriteLine($"Usage: {Flag} <0xADDRESS> [--window N]");
            return 2;
        }

        if (!ClientMemorySession.TryAttach(out ClientMemorySession? session, out string? attachFailure))
        {
            Console.WriteLine($"[REFUSED] client_not_readable:{attachFailure}");
            return 1;
        }

        using (session)
        {
            Console.WriteLine("=== what points at this address ===");
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"client: pid {session!.ProcessId}, module 0x{session.ModuleBase.ToInt64():X} + 0x{session.ModuleSize:X}"));
            Console.WriteLine();
            return Report(session, target, span, "target");
        }
    }

    /// <summary>A 32-bit address, with or without the 0x.</summary>
    public static bool TryParseAddress(string? text, out IntPtr address)
    {
        address = IntPtr.Zero;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[2..];

        if (!uint.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value)
            || value == 0)
        {
            return false;
        }

        address = new IntPtr(value);
        return true;
    }
}
