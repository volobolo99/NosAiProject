using NosAi.Adapter;
using NosAi.Core;
using Xunit;

namespace NosAi.Core.Tests;

[Trait("Category", "Gate1")]
public sealed class Win32ProcessAdapterTests
{
    private static string NonExistentProcessName => $"nosai-gate1-test-{Guid.NewGuid():N}";

    [Fact]
    public void AttachToANonExistentProcessFailsClosedWithoutThrowing()
    {
        using var adapter = new Win32ProcessAdapter();
        var options = new ProcessAttachOptions(NonExistentProcessName, "whatever.dll", "00", TimeoutMs: 50);

        bool attached = adapter.TryAttach(options, out FaultCode fault);

        Assert.False(attached);
        Assert.Equal(FaultCode.AttachFailed, fault);
        Assert.False(adapter.IsAttached);
        Assert.Equal(0, adapter.ProcessId);
    }

    [Fact]
    public void ReadRegionBeforeAttachThrowsInsteadOfReturningAPlausibleValue()
    {
        using var adapter = new Win32ProcessAdapter();

        Assert.Throws<InvalidOperationException>(() => adapter.ReadRegion((nuint)0x1000, new byte[16]));
    }

    [Fact]
    public void DisposeIsIdempotentAndSafeWithoutEverAttaching()
    {
        var adapter = new Win32ProcessAdapter();

        adapter.Dispose();
        adapter.Dispose();
    }

    [Fact]
    public void UsingAnAdapterAfterDisposeThrows()
    {
        var adapter = new Win32ProcessAdapter();
        adapter.Dispose();

        var options = new ProcessAttachOptions(NonExistentProcessName, "whatever.dll", "00", TimeoutMs: 50);
        Assert.Throws<ObjectDisposedException>(() => adapter.TryAttach(options, out _));
    }
}
