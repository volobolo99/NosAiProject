using System.Text;
using NosAi.Core;
using NosAi.Security;
using Xunit;

namespace NosAi.Core.Tests;

[Trait("Category", "Gate1")]
public sealed class CapabilityValidatorTests
{
    private static readonly byte[] RootKey = Encoding.UTF8.GetBytes("gate1-capbac-root-key-for-tests-only");
    private const uint ScopeObserve = 0b0001;
    private const uint ScopeExecute = 0b0010;

    [Fact]
    public void ValidTokenWithinWindowAndScopeIsGranted()
    {
        var validator = new HmacCapabilityValidator(RootKey);
        CapabilityToken token = CapabilityToken.Issue(subjectId: 1, scope: ScopeObserve | ScopeExecute, notBeforeUnixMs: 1000, notAfterUnixMs: 5000, RootKey);

        CapabilityVerdict verdict = validator.Validate(token, PipelineStage.Guard, ScopeObserve, nowUnixMs: 2000);

        Assert.True(verdict.Granted);
        Assert.Equal(FaultCode.None, verdict.Fault);
        Assert.Equal(ScopeObserve, verdict.EffectiveScope);
    }

    [Fact]
    public void RequestingScopeBeyondTheTokensGrantIsDenied()
    {
        var validator = new HmacCapabilityValidator(RootKey);
        CapabilityToken token = CapabilityToken.Issue(1, ScopeObserve, 1000, 5000, RootKey);

        CapabilityVerdict verdict = validator.Validate(token, PipelineStage.Guard, ScopeObserve | ScopeExecute, nowUnixMs: 2000);

        Assert.False(verdict.Granted);
        Assert.Equal(FaultCode.ScopeDenied, verdict.Fault);
        Assert.Equal(0u, verdict.EffectiveScope);
    }

    [Fact]
    public void TamperedMacIsRejected()
    {
        var validator = new HmacCapabilityValidator(RootKey);
        CapabilityToken token = CapabilityToken.Issue(1, ScopeObserve, 1000, 5000, RootKey);
        byte[] tamperedMac = token.Mac.ToArray();
        tamperedMac[0] ^= 0xFF;
        token = token with { Mac = tamperedMac };

        CapabilityVerdict verdict = validator.Validate(token, PipelineStage.Guard, ScopeObserve, nowUnixMs: 2000);

        Assert.False(verdict.Granted);
        Assert.Equal(FaultCode.ScopeDenied, verdict.Fault);
    }

    [Fact]
    public void TokenSignedWithADifferentRootKeyIsRejected()
    {
        var validator = new HmacCapabilityValidator(RootKey);
        byte[] otherRootKey = Encoding.UTF8.GetBytes("a completely different root key");
        CapabilityToken token = CapabilityToken.Issue(1, ScopeObserve, 1000, 5000, otherRootKey);

        CapabilityVerdict verdict = validator.Validate(token, PipelineStage.Guard, ScopeObserve, nowUnixMs: 2000);

        Assert.False(verdict.Granted);
        Assert.Equal(FaultCode.ScopeDenied, verdict.Fault);
    }

    [Fact]
    public void TokenNotYetValidBeyondSkewToleranceIsRejectedWithTimeout()
    {
        var validator = new HmacCapabilityValidator(RootKey);
        CapabilityToken token = CapabilityToken.Issue(1, ScopeObserve, notBeforeUnixMs: 10_000, notAfterUnixMs: 20_000, RootKey);

        CapabilityVerdict verdict = validator.Validate(token, PipelineStage.Guard, ScopeObserve, nowUnixMs: 10_000 - HmacCapabilityValidator.ClockSkewToleranceMs - 1);

        Assert.False(verdict.Granted);
        Assert.Equal(FaultCode.Timeout, verdict.Fault);
    }

    [Fact]
    public void TokenExpiredBeyondSkewToleranceIsRejectedWithTimeout()
    {
        var validator = new HmacCapabilityValidator(RootKey);
        CapabilityToken token = CapabilityToken.Issue(1, ScopeObserve, notBeforeUnixMs: 1000, notAfterUnixMs: 5000, RootKey);

        CapabilityVerdict verdict = validator.Validate(token, PipelineStage.Guard, ScopeObserve, nowUnixMs: 5000 + HmacCapabilityValidator.ClockSkewToleranceMs + 1);

        Assert.False(verdict.Granted);
        Assert.Equal(FaultCode.Timeout, verdict.Fault);
    }

    [Fact]
    public void TimeJustInsideTheSkewToleranceOnEitherEdgeIsAccepted()
    {
        var validator = new HmacCapabilityValidator(RootKey);
        CapabilityToken token = CapabilityToken.Issue(1, ScopeObserve, notBeforeUnixMs: 10_000, notAfterUnixMs: 20_000, RootKey);

        Assert.True(validator.Validate(token, PipelineStage.Guard, ScopeObserve, nowUnixMs: 10_000 - HmacCapabilityValidator.ClockSkewToleranceMs).Granted);
        Assert.True(validator.Validate(token, PipelineStage.Guard, ScopeObserve, nowUnixMs: 20_000 + HmacCapabilityValidator.ClockSkewToleranceMs).Granted);
    }

    [Fact]
    public void ConstructorRejectsAnEmptyRootKey()
    {
        Assert.Throws<ArgumentException>(() => new HmacCapabilityValidator(ReadOnlySpan<byte>.Empty));
    }
}
