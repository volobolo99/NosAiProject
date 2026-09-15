using System;
using NosAi.LiveIntegration;
using Xunit;

namespace NosAi.Runtime.Tests;

public sealed class TargetMemorySourceTests
{
    [Fact]
    public void Read_null_process_id_returns_null_without_calling_attach()
    {
        var callCount = 0;
        TryAttachClientMemorySession fakeAttach = (out ClientMemorySession? session, out string? failureReason, int processId) =>
        {
            callCount++;
            session = null;
            failureReason = null;
            return false;
        };

        var source = new TargetMemorySource(fakeAttach);
        TargetPointerReading? result = source.Read(null);

        Assert.Null(result);
        Assert.Equal("client_process_not_attached", source.FailureReason());
        Assert.Equal(0, callCount);
    }

    [Fact]
    public void Read_with_a_failed_attach_returns_null_and_names_the_reason()
    {
        TryAttachClientMemorySession fakeAttach = (out ClientMemorySession? session, out string? failureReason, int processId) =>
        {
            session = null;
            failureReason = "attach_failure";
            return false;
        };

        var source = new TargetMemorySource(fakeAttach);
        TargetPointerReading? result = source.Read(1234);

        Assert.Null(result);
        Assert.Equal("attach_failure", source.FailureReason());
    }

    [Fact]
    public void Read_after_dispose_throws()
    {
        var source = new TargetMemorySource();
        source.Dispose();

        Assert.Throws<ObjectDisposedException>(() => source.Read(1234));
    }

    [Fact]
    public void Dispose_twice_does_not_throw()
    {
        var source = new TargetMemorySource();
        source.Dispose();
        source.Dispose();
    }

    // The success path (a real attach followed by a real read) and the
    // re-attach-on-different-pid path are not testable in isolation:
    // ClientMemorySession is sealed with a private constructor, so nothing
    // here can fabricate one. Both stay live-only verifications.
}
