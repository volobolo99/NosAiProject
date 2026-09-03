using NosAi.LiveIntegration;
using NosAi.Runtime.Navigation;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The last link of the target chain, and the proof it still needs (`C1-6`).
/// </summary>
/// <remarks>
/// The behavioural oracle established <c>manager+0x44</c> and that is a pointer, so
/// <c>HasTarget</c> is settled. Whether <c>[pointer]+0x08</c> is the entity id is an
/// analogy with the player object, and this is where an analogy is either turned into a
/// measurement by a second source or left as UNKNOWN.
/// </remarks>
public sealed class TargetChainProbeTests
{
    private const long PlayerId = 3_443_217;
    private const int TargetId = 313_906;

    // ------------------------------------------------------------------ the comparison

    [Fact]
    public void TwoSourcesAgreeingOnAPlausibleIdEstablishesIt()
    {
        TargetChainVerdict verdict = TargetChainProbe.Compare(
            memoryId: TargetId, memoryFailure: null, wireId: TargetId, wireFailure: null, PlayerId);

        Assert.True(verdict.Established);
        Assert.Null(verdict.Reason);
        Assert.Equal(TargetId, verdict.MemoryId);
        Assert.Equal(TargetId, verdict.WireId);
    }

    /// <summary>
    /// The case the double source exists for. Nothing is written down when the two
    /// disagree, and the reason carries both numbers so the operator can see which is
    /// which.
    /// </summary>
    [Fact]
    public void DisagreementRefusesAndShowsBothNumbers()
    {
        TargetChainVerdict verdict = TargetChainProbe.Compare(
            memoryId: TargetId, memoryFailure: null, wireId: 999_111, wireFailure: null, PlayerId);

        Assert.False(verdict.Established);
        Assert.StartsWith(TargetChainProbe.DisagreeReason + ":", verdict.Reason);
        Assert.Contains("313906", verdict.Reason);
        Assert.Contains("999111", verdict.Reason);
        Assert.Contains("NON scrivere l'offset", TargetChainProbe.Advice(verdict));
    }

    /// <summary>
    /// The check that would have caught the pointer being mistaken for an id: the value
    /// read at <c>manager+0x44</c> sits three orders of magnitude away from any real id
    /// on this build.
    /// </summary>
    [Fact]
    public void APointerSizedNumberIsNotAnEntityId()
    {
        TargetChainVerdict verdict = TargetChainProbe.Compare(
            memoryId: unchecked((int)0x22C8A4F0), memoryFailure: null,
            wireId: TargetId, wireFailure: null, PlayerId);

        Assert.False(verdict.Established);
        Assert.StartsWith(TargetChainProbe.ImplausibleReason + ":", verdict.Reason);
        Assert.Contains("L'analogia col giocatore non regge", TargetChainProbe.Advice(verdict));
    }

    /// <summary>
    /// Plausible and unconfirmed is exactly what this project refuses to publish: it
    /// looks right, and looking right is not evidence.
    /// </summary>
    [Fact]
    public void WithoutTheWireAPlausibleNumberIsStillNotEstablished()
    {
        TargetChainVerdict verdict = TargetChainProbe.Compare(
            memoryId: TargetId, memoryFailure: null, wireId: null,
            wireFailure: "gameplay_provider_not_available", PlayerId);

        Assert.False(verdict.Established);
        Assert.Equal("gameplay_provider_not_available", verdict.Reason);
        Assert.Equal(TargetId, verdict.MemoryId);
        Assert.Contains("--observe-game", TargetChainProbe.Advice(verdict));
    }

    [Fact]
    public void WithNothingReadFromMemoryTheReasonComesFromTheChain()
    {
        TargetChainVerdict verdict = TargetChainProbe.Compare(
            memoryId: null, memoryFailure: "target_entity_id_unreadable",
            wireId: TargetId, wireFailure: null, PlayerId);

        Assert.False(verdict.Established);
        Assert.Equal("target_entity_id_unreadable", verdict.Reason);
        Assert.Contains("HasTarget resta comunque", TargetChainProbe.Advice(verdict));
    }

    // ------------------------------------------------------------------- the wire side

    [Fact]
    public void TheWiresAnswerIsReadFromTheOperatorApi()
    {
        const string Json = """
            {"client":{"gameplayBaseline":{"value":{"selectedTarget":{"value":{"entityId":313906,
            "entityType":3},"source":"LIVE","hasObservedValue":true}}}}}
            """;

        Assert.True(TargetChainProbe.TryReadWireTarget(Json, out long id, out string? reason));
        Assert.Equal(TargetId, id);
        Assert.Null(reason);
    }

    [Fact]
    public void AnUnknownSelectionOnTheWireCarriesItsOwnReason()
    {
        const string Json = """
            {"client":{"gameplayBaseline":{"value":{"selectedTarget":{"value":null,"source":"UNKNOWN",
            "hasObservedValue":false,"failureReason":"no_target_selection_observed"}}}}}
            """;

        Assert.False(TargetChainProbe.TryReadWireTarget(Json, out _, out string? reason));
        Assert.Equal("no_target_selection_observed", reason);
    }

    [Fact]
    public void ASnapshotWithNoGameplayIsNamedRatherThanGuessed()
    {
        Assert.False(TargetChainProbe.TryReadWireTarget("""{"health":"Healthy"}""", out _, out string? reason));
        Assert.Equal("gameplay_provider_not_available", reason);
    }

    [Fact]
    public void RubbishIsRefusedWithoutThrowing()
    {
        Assert.False(TargetChainProbe.TryReadWireTarget("not json", out _, out string? reason));
        Assert.StartsWith("operator_api_unparsable:", reason);
    }

    // --------------------------------------------------- what the pointer alone settles

    /// <summary>
    /// <c>HasTarget</c> does not wait for the identity: knowing that there is a target and
    /// knowing which one are two facts, and the pointer settles the first by itself.
    /// </summary>
    [Fact]
    public void ThePointerSettlesWhetherThereIsATargetOnItsOwn()
    {
        var selected = new TargetPointerReading(
            new IntPtr(0x21BF5C60), new IntPtr(0x22C8A4F0), TargetId, null);
        var none = new TargetPointerReading(new IntPtr(0x21BF5C60), IntPtr.Zero, null, null);

        Assert.True(selected.HasTarget);
        Assert.False(none.HasTarget);

        // And the identity being unreadable does not disturb it.
        var unreadable = new TargetPointerReading(
            new IntPtr(0x21BF5C60), new IntPtr(0x22C8A4F0), null, "target_entity_id_unreadable");
        Assert.True(unreadable.HasTarget);
    }
}
