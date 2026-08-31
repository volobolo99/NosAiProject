using System.Runtime.InteropServices;
using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Security;
using Xunit;
using Xunit.Abstractions;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Reading the client's process memory — permitted by ADR-0014, gated by Safety.
/// </summary>
/// <remarks>
/// The Windows-only tests here perform a real <c>ReadProcessMemory</c> round trip
/// against this test process, so the P/Invoke signatures and the partial-read
/// handling are exercised against the OS rather than against a stand-in.
/// </remarks>
public sealed class ProcessMemoryReaderTests
{
    private readonly ITestOutputHelper _output;

    public ProcessMemoryReaderTests(ITestOutputHelper output) => _output = output;

    // ------------------------------------------------- who may read at all

    [Theory]
    [InlineData(SecurityPrincipal.GuardDevice)]
    [InlineData(SecurityPrincipal.AutonomousAgent)]
    [InlineData(SecurityPrincipal.Subsystem)]
    [InlineData(SecurityPrincipal.Unknown)]
    public void OnlyAPrincipalHoldingTheCapabilityMayOpenAProcess(SecurityPrincipal principal)
    {
        // A stolen or spoofed phone must not be able to make the PC read another
        // process — ADR-0014 lifted the prohibition, not the gate.
        using var reader = ProcessMemoryReader.TryOpen(
            Environment.ProcessId, principal, out string? reason);

        Assert.Null(reader);
        Assert.NotNull(reason);
        Assert.StartsWith("not_authorized:", reason);
    }

    [Fact]
    public void TheOperatorHoldsTheCapability()
    {
        // Not the same claim as "the OS let us in": this pins the policy decision,
        // which is what ADR-0014 changed.
        var decision = new Gate1AuthorizationPolicy().Evaluate(
            SecurityPrincipal.Operator, RuntimeCapability.ReadProcessMemory, TrustTier.Tier1, TrustTier.Tier4);

        Assert.True(decision.Allowed);
    }

    [Fact]
    public void AnImpossibleProcessIdIsRefusedBeforeAnythingIsOpened()
    {
        using var reader = ProcessMemoryReader.TryOpen(0, SecurityPrincipal.Operator, out string? reason);

        Assert.Null(reader);
        Assert.Equal("invalid_process_id", reason);
    }

    [Fact]
    public void ARefusalAlwaysCarriesAReason()
    {
        // Null with no reason would leave the caller unable to tell "not allowed"
        // from "not there".
        using var reader = ProcessMemoryReader.TryOpen(-1, SecurityPrincipal.Operator, out string? reason);

        Assert.Null(reader);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void AuthorizationIsCheckedBeforeTheProcessIsEvenLookedFor()
    {
        // The order matters: an unauthorised caller must not learn whether a pid
        // exists, and no handle should be opened on its behalf.
        using var reader = ProcessMemoryReader.TryOpen(
            999_999_999, SecurityPrincipal.GuardDevice, out string? reason);

        Assert.Null(reader);
        Assert.StartsWith("not_authorized:", reason);
    }

    // --------------------------------------------- the real OS round trip

    [WindowsOnlyFact]
    public void ReadsBackAValueThisProcessActuallyWroteInMemory()
    {
        const int written = 0x0BADC0DE;
        IntPtr address = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(address, written);

            using var reader = ProcessMemoryReader.TryOpen(
                Environment.ProcessId, SecurityPrincipal.Operator, out string? reason);

            Assert.NotNull(reader);
            Assert.Null(reason);

            var result = reader!.Read(address, sizeof(int));

            Evidence.Live(_output, "processo", Environment.ProcessId);
            Evidence.Live(_output, "byteLetti", result.Bytes.Length);
            Evidence.Live(_output, "classificazione", result.Source);
            Evidence.Live(_output, "valoreRiletto", $"0x{BitConverter.ToInt32(result.Bytes):X8}");

            Assert.True(result.Ok, result.FailureReason);
            Assert.Equal(DataSourceKind.Live, result.Source);
            Assert.Equal(written, BitConverter.ToInt32(result.Bytes));
        }
        finally
        {
            Marshal.FreeHGlobal(address);
        }
    }

    [WindowsOnlyFact]
    public void AValueThatPassesItsValidityCheckIsLive()
    {
        IntPtr address = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(address, 4_212);
            using var reader = ProcessMemoryReader.TryOpen(
                Environment.ProcessId, SecurityPrincipal.Operator, out _);

            var hp = reader!.ReadValidatedInt32(address, v => v is >= 0 and <= 100_000, DateTime.UtcNow);

            Evidence.Live(_output, "valore", hp.Value);
            Evidence.Live(_output, "classificazione", hp.Source, "supera il controllo di plausibilita");

            Assert.Equal(DataSourceKind.Live, hp.Source);
            Assert.Equal(4_212, hp.Value);
        }
        finally
        {
            Marshal.FreeHGlobal(address);
        }
    }

    [WindowsOnlyFact]
    public void AValueThatFailsItsValidityCheckIsUnknownRatherThanPlausible()
    {
        // This is the reason ADR-0012 distrusted memory reads, and the reason the
        // check is mandatory now that ADR-0014 allows them: a stale offset returns
        // four perfectly readable bytes. Reporting 2.1 billion HP as LIVE would be
        // the system claiming certainty it does not have.
        IntPtr address = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(address, int.MaxValue);
            using var reader = ProcessMemoryReader.TryOpen(
                Environment.ProcessId, SecurityPrincipal.Operator, out _);

            var hp = reader!.ReadValidatedInt32(address, v => v is >= 0 and <= 100_000, DateTime.UtcNow);

            Evidence.Live(_output, "byteInMemoria", int.MaxValue);
            Evidence.Unknown(_output, "valorePubblicato", hp.FailureReason ?? "senza motivo");
            Evidence.Live(_output, "classificazione", hp.Source, "leggibile ma non plausibile");

            Assert.Equal(DataSourceKind.Unknown, hp.Source);
            Assert.Null(hp.Value);
            Assert.Contains("value_failed_validity_check", hp.FailureReason);
            Assert.False(hp.HasValue);
        }
        finally
        {
            Marshal.FreeHGlobal(address);
        }
    }

    [WindowsOnlyFact]
    public void UnknownIsNotZero()
    {
        // A failed read reported as 0 HP would read as "the character is dead".
        IntPtr address = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(address, -999);
            using var reader = ProcessMemoryReader.TryOpen(
                Environment.ProcessId, SecurityPrincipal.Operator, out _);

            var hp = reader!.ReadValidatedInt32(address, v => v >= 0, DateTime.UtcNow);

            Assert.Equal(DataSourceKind.Unknown, hp.Source);
            Assert.Null(hp.Value);
        }
        finally
        {
            Marshal.FreeHGlobal(address);
        }
    }

    [WindowsOnlyFact]
    public void AnUnreadableAddressFailsInsteadOfReturningGarbage()
    {
        using var reader = ProcessMemoryReader.TryOpen(
            Environment.ProcessId, SecurityPrincipal.Operator, out _);

        var result = reader!.Read(new IntPtr(0x10), 64);

        Evidence.Unknown(_output, "letturaIndirizzoBasso", result.FailureReason ?? "senza motivo");
        Evidence.Live(_output, "byteRestituiti", result.Bytes.Length, "zero: nessun mezzo valore");

        Assert.False(result.Ok);
        Assert.Equal(DataSourceKind.Unknown, result.Source);
        Assert.Empty(result.Bytes);
        Assert.NotNull(result.FailureReason);
    }

    [WindowsOnlyFact]
    public void ANullAddressIsRefused()
    {
        using var reader = ProcessMemoryReader.TryOpen(
            Environment.ProcessId, SecurityPrincipal.Operator, out _);

        Assert.Equal("null_address", reader!.Read(IntPtr.Zero, 4).FailureReason);
    }

    [WindowsOnlyFact]
    public void AnAbsurdLengthIsRefusedRatherThanAllocated()
    {
        using var reader = ProcessMemoryReader.TryOpen(
            Environment.ProcessId, SecurityPrincipal.Operator, out _);

        Assert.StartsWith("invalid_length", reader!.Read(new IntPtr(0x1000), int.MaxValue).FailureReason);
        Assert.StartsWith("invalid_length", reader.Read(new IntPtr(0x1000), 0).FailureReason);
    }

    [WindowsOnlyFact]
    public void ADisposedReaderRefusesInsteadOfUsingAClosedHandle()
    {
        // Reading through a closed handle is how a use-after-free turns into a
        // wrong number rather than a crash.
        var reader = ProcessMemoryReader.TryOpen(
            Environment.ProcessId, SecurityPrincipal.Operator, out _);
        reader!.Dispose();

        Assert.Equal("reader_disposed", reader.Read(new IntPtr(0x1000), 4).FailureReason);
        reader.Dispose();
    }

    [WindowsOnlyFact]
    public void ReportsWhyItCouldNotOpenAProtectedProcess()
    {
        // 4 is System on Windows: a live pid a normal user cannot open. Whichever
        // way it goes, the answer must name the obstacle instead of looking like a
        // successful read of nothing.
        using var reader = ProcessMemoryReader.TryOpen(4, SecurityPrincipal.Operator, out string? reason);

        if (reader is null)
            Assert.False(string.IsNullOrWhiteSpace(reason));
    }
}
