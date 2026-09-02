using NosAi.Runtime.Contracts;
using NosAi.Runtime.Safety;
using NosAi.Runtime.Security;
using Xunit;
using Xunit.Abstractions;

namespace NosAi.Runtime.Tests;

/// <summary>
/// What the runtime refuses, and why (M020–M027).
/// </summary>
/// <remarks>
/// A security boundary is judged on what it denies, so almost every test here is
/// negative. Each asserts the structured reason as well as the answer: a gate that
/// refuses everything for the wrong reason cannot be audited, and one that cannot
/// be audited is trusted on faith rather than evidence.
/// </remarks>
public sealed class RuntimeAuthorizationTests
{
    private readonly ITestOutputHelper _output;

    public RuntimeAuthorizationTests(ITestOutputHelper output) => _output = output;

    private static readonly Gate1AuthorizationPolicy Policy = new();

    private static AuthorizationDecision Ask(
        SecurityPrincipal principal,
        RuntimeCapability capability,
        TrustTier required = TrustTier.Tier1_Assisted,
        TrustTier granted = TrustTier.Tier4_FullAutonomous)
        => Policy.Evaluate(principal, capability, required, granted);

    // ------------------------------------------------------------ fail closed

    [Fact]
    public void AnUnidentifiedCallerGetsNothing()
    {
        // Checked before every other rule: an unknown principal must not be able to
        // reach a grant table at all.
        foreach (RuntimeCapability capability in Enum.GetValues<RuntimeCapability>())
        {
            var decision = Ask(SecurityPrincipal.Unknown, capability);
            Evidence.Live(_output, "esito", decision.Allowed);
            Evidence.Live(_output, "motivo", decision.Reason, "fail-closed su principal ignoto");

            Assert.False(decision.Allowed);
            Assert.Equal("unknown_principal", decision.Reason);
        }
    }

    [Fact]
    public void AnUnknownCapabilityIsRefused()
    {
        var decision = Ask(SecurityPrincipal.Operator, RuntimeCapability.Unknown);

        Evidence.Live(_output, "principal", SecurityPrincipal.Operator, "il piu' privilegiato");
        Evidence.Live(_output, "esito", decision.Allowed);
        Evidence.Live(_output, "motivo", decision.Reason,
            "nemmeno l'operatore ottiene una capability che il runtime non conosce");

        Assert.False(decision.Allowed);
        Assert.Equal("unknown_capability", decision.Reason);
    }

    [Fact]
    public void ACapabilityOutsideTheEnumIsRefused()
    {
        // A value cast in from outside — a wire field, a config string — must not
        // slip past the allow-list because it is not a defined member.
        var decision = Ask(SecurityPrincipal.Operator, (RuntimeCapability)999);

        Assert.False(decision.Allowed);
        Assert.Equal("unknown_capability", decision.Reason);
    }

    [Fact]
    public void EveryPrincipalAndCapabilityPairIsEitherGrantedDeliberatelyOrDenied()
    {
        // The allow-list must be exhaustive by construction: a capability added to
        // the enum without a matching rule has to land on deny, not on a gap.
        foreach (SecurityPrincipal principal in Enum.GetValues<SecurityPrincipal>())
        {
            foreach (RuntimeCapability capability in Enum.GetValues<RuntimeCapability>())
            {
                var decision = Ask(principal, capability);
                Assert.NotNull(decision.Reason);
                Assert.NotEmpty(decision.Reason);
                if (!decision.Allowed)
                    continue;

                // Anything allowed must be an observation or a request — never a
                // power that acts on the game.
                Assert.True(
                    capability is RuntimeCapability.ObserveSnapshot
                        or RuntimeCapability.RequestCommand
                        or RuntimeCapability.ReadGameTraffic
                        or RuntimeCapability.ReadProcessMemory,
                    $"{principal} was allowed {capability}, which acts on the game");
            }
        }
    }

    // -------------------------------------------------------- execution is off

    [Theory]
    [InlineData(RuntimeCapability.ExecuteGameAction)]
    [InlineData(RuntimeCapability.SendLiveInput)]
    [InlineData(RuntimeCapability.InjectPacket)]
    public void NoPrincipalMayActOnTheGameAtGateOne(RuntimeCapability capability)
    {
        // Whoever asks, whatever tier they hold. The reason names the gate, so an
        // operator is not sent looking for a permissions problem.
        foreach (SecurityPrincipal principal in Enum.GetValues<SecurityPrincipal>())
        {
            if (principal == SecurityPrincipal.Unknown)
                continue;

            var decision = Ask(principal, capability, TrustTier.Tier1_Assisted, TrustTier.Tier4_FullAutonomous);

            Assert.False(decision.Allowed);
            Assert.Equal(Gate1AuthorizationPolicy.ExecutionDisabledReason, decision.Reason);
        }
    }

    [Fact]
    public void TheHighestTrustTierDoesNotUnlockExecution()
    {
        // Trust and gate level are different axes. Holding Tier 4 must not read as
        // permission to act while the gate says otherwise.
        var decision = Ask(SecurityPrincipal.Operator, RuntimeCapability.ExecuteGameAction,
            TrustTier.Tier1_Assisted, TrustTier.Tier4_FullAutonomous);

        Assert.False(decision.Allowed);
        Assert.Equal(Gate1AuthorizationPolicy.ExecutionDisabledReason, decision.Reason);
    }

    // ------------------------------------------------------------ the grants

    [Fact]
    public void TheOperatorMayObserveAndAsk()
    {
        Assert.True(Ask(SecurityPrincipal.Operator, RuntimeCapability.ObserveSnapshot).Allowed);
        Assert.True(Ask(SecurityPrincipal.Operator, RuntimeCapability.RequestCommand).Allowed);
    }

    [Fact]
    public void ThePhoneMayNotMakeThePcCaptureTrafficOrReadMemory()
    {
        // The phone is an operator's screen. A stolen or spoofed device must not be
        // able to turn the PC into a capture tool — that power belongs to the person
        // at the machine (ADR-0014 widened the data paths, not who may use them).
        foreach (var capability in new[] { RuntimeCapability.ReadGameTraffic, RuntimeCapability.ReadProcessMemory })
        {
            var decision = Ask(SecurityPrincipal.GuardDevice, capability);
            Assert.False(decision.Allowed);
            Assert.Equal("capability_not_granted", decision.Reason);
        }
    }

    [Fact]
    public void TheAutonomousAgentMayOnlyObserve()
    {
        Assert.True(Ask(SecurityPrincipal.AutonomousAgent, RuntimeCapability.ObserveSnapshot).Allowed);

        // It has no person behind it, so it may not even ask for a command.
        var decision = Ask(SecurityPrincipal.AutonomousAgent, RuntimeCapability.RequestCommand);
        Assert.False(decision.Allowed);
        Assert.Equal("capability_not_granted", decision.Reason);
    }

    // ------------------------------------------------------------- trust tier

    [Fact]
    public void ATierBelowWhatTheActionDemandsIsRefusedWithBothTiersNamed()
    {
        var decision = Ask(SecurityPrincipal.Operator, RuntimeCapability.RequestCommand,
            required: TrustTier.Tier4_FullAutonomous, granted: TrustTier.Tier2_SemiAutonomous);

        Assert.False(decision.Allowed);
        Assert.Contains("trust_tier_insufficient", decision.Reason);
        Assert.Contains("Tier4", decision.Reason);
        Assert.Contains("Tier2", decision.Reason);
    }

    [Fact]
    public void AnExactTierMatchIsEnough()
    {
        Assert.True(Ask(SecurityPrincipal.Operator, RuntimeCapability.RequestCommand,
            required: TrustTier.Tier3_AutonomousRestricted, granted: TrustTier.Tier3_AutonomousRestricted).Allowed);
    }

    // -------------------------------------------------------- the safety gate

    [Fact]
    public void TheGateStillRefusesEveryActionAsItAlwaysHas()
    {
        // The outcome must not have changed: this used to be a bare `return false`,
        // and adding reasons must not have turned a gate that always refused into
        // one that sometimes permits.
        var gate = new CapabilityAuthorizationGate();
        var allowed = new GuardDecision(true, TrustTier.Tier4_FullAutonomous, "guard_ok");

        foreach (ActionKind kind in Enum.GetValues<ActionKind>())
        {
            var action = new CandidateAction($"a-{kind}", kind, TrustTier.Tier1_Assisted, 1.0);
            Assert.False(gate.Authorize(action, allowed));
        }
    }

    [Fact]
    public void ANoOpIsStillAnActionAndIsStillRefused()
    {
        // The specific loosening this guards against: routing NoOp to observation
        // would have made the gate authorise it.
        var gate = new CapabilityAuthorizationGate();
        var action = new CandidateAction("noop", ActionKind.NoOp, TrustTier.Tier1_Assisted, 0.0);

        var decision = gate.Evaluate(action, new GuardDecision(true, TrustTier.Tier4_FullAutonomous, "guard_ok"));

        Assert.False(decision.Allowed);
        Assert.Equal(Gate1AuthorizationPolicy.ExecutionDisabledReason, decision.Reason);
    }

    [Fact]
    public void AGuardRejectionIsReportedAsSuchAndNotOverturned()
    {
        // The guard's verdict comes first: the policy answers a different question
        // and must never be able to reverse a rejection.
        var gate = new CapabilityAuthorizationGate();
        var action = new CandidateAction("a", ActionKind.Move, TrustTier.Tier1_Assisted, 1.0);

        var decision = gate.Evaluate(action, new GuardDecision(false, TrustTier.Tier1_Assisted, "too_risky"));

        Assert.False(decision.Allowed);
        Assert.StartsWith("guard_refused:", decision.Reason);
        Assert.Contains("too_risky", decision.Reason);
    }

    [Fact]
    public void TheGateRemembersItsLastDecisionForReporting()
    {
        var gate = new CapabilityAuthorizationGate();
        var action = new CandidateAction("a", ActionKind.Combat, TrustTier.Tier1_Assisted, 1.0);

        gate.Authorize(action, new GuardDecision(true, TrustTier.Tier4_FullAutonomous, "ok"));

        Assert.NotNull(gate.LastDecision);
        Assert.Equal(Gate1AuthorizationPolicy.ExecutionDisabledReason, gate.LastDecision!.Reason);
        Assert.Contains("DENY", gate.LastDecision.ToString());
    }

    [Fact]
    public void TheGateRefusesNullInputRatherThanTreatingItAsPermission()
    {
        var gate = new CapabilityAuthorizationGate();
        var action = new CandidateAction("a", ActionKind.Move, TrustTier.Tier1_Assisted, 1.0);

        Assert.Throws<ArgumentNullException>(() => gate.Authorize(null!, new GuardDecision(true, TrustTier.Tier1_Assisted, "ok")));
        Assert.Throws<ArgumentNullException>(() => gate.Authorize(action, null!));
    }

    [Fact]
    public void APolicyIsRequiredRatherThanDefaultingToPermissive()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CapabilityAuthorizationGate(null!, SecurityPrincipal.Operator, TrustTier.Tier4_FullAutonomous));
    }

    [Fact]
    public void TheComposedGateActsAsTheAutonomousAgentNotTheOperator()
    {
        // An action arriving through the orchestrator has no person behind it.
        // Assuming an operator would grant it more than it should hold.
        var gate = new CapabilityAuthorizationGate();
        gate.Authorize(new CandidateAction("a", ActionKind.Move, TrustTier.Tier1_Assisted, 1.0),
            new GuardDecision(true, TrustTier.Tier1_Assisted, "ok"));

        Assert.Equal(SecurityPrincipal.AutonomousAgent, gate.LastDecision!.Principal);
    }
}
