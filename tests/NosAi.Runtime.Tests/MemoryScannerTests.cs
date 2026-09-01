using System.Runtime.InteropServices;
using NosAi.LiveIntegration;
using NosAi.Runtime.Security;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// <see cref="MemoryScanner"/>, the offset-discovery tool the memory provider
/// needs before it can read anything (ADR-0014).
/// </summary>
/// <remarks>
/// <para>
/// No fakes and no simulated address space: these scan the real memory of the
/// process running the tests, through the same <see cref="ProcessMemoryReader"/>
/// and the same authorization gate the runtime uses against the game client. The
/// value looked for is held in real unmanaged memory at an address the test knows,
/// so "did the scan find it" has an answer that does not depend on the scanner.
/// </para>
/// <para>
/// Scanning a whole process is not cheap, so the value scanned for is a
/// distinctive one: a number unlikely to occur by accident keeps the candidate
/// set small and the assertions meaningful.
/// </para>
/// </remarks>
public sealed class MemoryScannerTests
{
    /// <summary>Distinctive enough that unrelated matches are unlikely.</summary>
    private const int Sentinel = 0x5E17A1;

    private const int ChangedSentinel = 0x5E17A2;

    private static ProcessMemoryReader OpenSelf()
    {
        ProcessMemoryReader? reader = ProcessMemoryReader.TryOpen(
            Environment.ProcessId, SecurityPrincipal.Operator, out string? failure);
        Assert.True(reader is not null, $"could not open own process: {failure}");
        return reader!;
    }

    [WindowsOnlyFact]
    public void TheAddressSpaceEnumeratesIntoCommittedReadableRegions()
    {
        using ProcessMemoryReader reader = OpenSelf();

        List<MemoryRegion> regions = reader.EnumerateRegions().Take(200).ToList();

        Assert.NotEmpty(regions);
        Assert.All(regions, r =>
        {
            Assert.True(r.Size > 0, "a region with no size is not a region");
            Assert.NotEqual(IntPtr.Zero, r.BaseAddress);
        });
        // A process always has private data; if none came back, the type filter the
        // scanner depends on is wrong and every scan would examine nothing.
        Assert.Contains(regions, r => r.IsPrivate);
    }

    [WindowsOnlyFact]
    public void RegionsDoNotOverlapAndAdvanceUpwards()
    {
        using ProcessMemoryReader reader = OpenSelf();

        List<MemoryRegion> regions = reader.EnumerateRegions().Take(200).ToList();

        // The walk advances by RegionSize. If it ever failed to, the enumeration
        // would revisit the same region forever and a scan would never finish.
        for (int i = 1; i < regions.Count; i++)
        {
            long previousEnd = regions[i - 1].BaseAddress.ToInt64() + regions[i - 1].Size;
            Assert.True(
                regions[i].BaseAddress.ToInt64() >= previousEnd,
                $"region {i} at 0x{regions[i].BaseAddress.ToInt64():X} overlaps the previous one ending at 0x{previousEnd:X}");
        }
    }

    [WindowsOnlyFact]
    public void AValueHeldInThisProcessIsFoundAtTheAddressHoldingIt()
    {
        // Real unmanaged memory, so the address is stable and known independently
        // of whatever the scanner reports.
        IntPtr slot = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(slot, Sentinel);
            using ProcessMemoryReader reader = OpenSelf();

            MemoryScanner.ScanResult result = MemoryScanner.Scan(reader, Sentinel);

            Assert.Contains(slot, result.Addresses);
            Assert.Equal(1, result.Passes);
            Assert.True(result.RegionsScanned > 0);
            Assert.True(result.BytesScanned > 0);
        }
        finally
        {
            Marshal.FreeHGlobal(slot);
        }
    }

    [WindowsOnlyFact]
    public void NarrowingKeepsTheAddressThatTrackedTheChangeAndDropsTheRest()
    {
        IntPtr tracks = Marshal.AllocHGlobal(sizeof(int));
        IntPtr coincides = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            // Two addresses hold the same value, which is the situation every first
            // scan is actually in. Only one of them changes with the thing being
            // tracked, and that is the whole basis on which an offset is identified.
            Marshal.WriteInt32(tracks, Sentinel);
            Marshal.WriteInt32(coincides, Sentinel);

            using ProcessMemoryReader reader = OpenSelf();
            var candidates = new[] { tracks, coincides };

            Marshal.WriteInt32(tracks, ChangedSentinel);
            MemoryScanner.ScanResult narrowed = MemoryScanner.Narrow(reader, candidates, ChangedSentinel);

            Assert.Equal(new[] { tracks }, narrowed.Addresses);
            Assert.DoesNotContain(coincides, narrowed.Addresses);
            Assert.Equal(2, narrowed.Passes);
        }
        finally
        {
            Marshal.FreeHGlobal(tracks);
            Marshal.FreeHGlobal(coincides);
        }
    }

    [WindowsOnlyFact]
    public void OneAddressAfterOnePassIsNotYetAnAnswer()
    {
        IntPtr slot = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(slot, Sentinel);
            using ProcessMemoryReader reader = OpenSelf();

            MemoryScanner.ScanResult first = MemoryScanner.Narrow(
                reader, new[] { slot }, Sentinel, previousPasses: 0);

            // A single candidate that has never been tested against a change is an
            // integer that once equalled the value, not an identified offset. The
            // probe exits non-zero on exactly this, so a script cannot mistake it
            // for a result.
            Assert.Single(first.Addresses);
            Assert.Equal(1, first.Passes);
            Assert.False(first.IsConclusive);

            MemoryScanner.ScanResult second = MemoryScanner.Narrow(
                reader, first.Addresses, Sentinel, first.Passes);

            Assert.True(second.IsConclusive);
        }
        finally
        {
            Marshal.FreeHGlobal(slot);
        }
    }

    [WindowsOnlyFact]
    public void AnAddressThatStoppedHoldingTheValueIsDropped()
    {
        IntPtr slot = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(slot, Sentinel);
            using ProcessMemoryReader reader = OpenSelf();

            MemoryScanner.ScanResult result = MemoryScanner.Narrow(reader, new[] { slot }, ChangedSentinel);

            Assert.Empty(result.Addresses);
            Assert.Contains("No address holds that value", result.Advice);
        }
        finally
        {
            Marshal.FreeHGlobal(slot);
        }
    }

    [WindowsOnlyFact]
    public void ScanningIsRefusedToAPrincipalThatMayNotReadProcessMemory()
    {
        // The scanner is a diagnostic, and a diagnostic is not a way around
        // ADR-0003: it can only ever read what the policy already allows.
        ProcessMemoryReader? reader = ProcessMemoryReader.TryOpen(
            Environment.ProcessId, SecurityPrincipal.Unknown, out string? failure);

        Assert.Null(reader);
        Assert.NotNull(failure);
        Assert.StartsWith("not_authorized", failure);
    }
}
