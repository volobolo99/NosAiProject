namespace NosAi.Core.WorldModel.Certification;

/// <summary>
/// The project's own verification-level convention -- used in prose
/// throughout CLAUDE.md and every phase status doc under
/// docs/agents/phases/ since AP-00, never a real type until AP-10, the
/// phase this hierarchy exists to certify against. Ordered so a plain
/// numeric comparison is a meaningful "at least this mature" check.
/// </summary>
public enum VerificationLevel
{
    /// <summary>Contracts/algorithms exist, compile, and are unit-tested in isolation.</summary>
    Present = 0,

    /// <summary>Wired into a real runtime cycle; combined test suites pass with it in place.</summary>
    Integrated = 1,

    /// <summary>Known defects for the declared scope are fixed and closed out.</summary>
    Done = 2,

    /// <summary>Validated against the real target (client, hardware) per CLAUDE.md's real-environment rule -- never claimed without that evidence.</summary>
    Verified = 3
}

/// <summary>
/// One stage of the end-to-end scenario docs/ROADMAP_ESECUTIVA.md S:AP-10
/// names: "startup → attach → perception → map discovery → exploration →
/// navigation → target recognition → combat → loot/inventory →
/// multi-step quest → equipment/progression → recovery → persistence →
/// evidence", in that order.
/// </summary>
public enum CertificationStage
{
    Startup = 0,
    Attach = 1,
    Perception = 2,
    MapDiscovery = 3,
    Exploration = 4,
    Navigation = 5,
    TargetRecognition = 6,
    Combat = 7,
    LootInventory = 8,
    MultiStepQuest = 9,
    EquipmentProgression = 10,
    Recovery = 11,
    Persistence = 12,
    Evidence = 13
}

/// <summary>
/// One stage's honestly assessed maturity, with what backs that
/// assessment and, when it is not yet <see cref="VerificationLevel.Verified"/>,
/// exactly what is missing. A certification report is only as credible as
/// its citations -- <see cref="Evidence"/> is required precisely so this
/// type cannot carry an asserted level with nothing behind it.
/// </summary>
/// <param name="Stage">Which stage this result is about.</param>
/// <param name="Level">The honestly assessed level -- never higher than what <see cref="Evidence"/> actually supports.</param>
/// <param name="Evidence">A named pointer to what backs this level (a status doc section, a test run, a hardware log) -- never empty.</param>
/// <param name="Blockers">Named reasons this stage is not at a higher level yet. Empty only when <see cref="Level"/> is already <see cref="VerificationLevel.Verified"/>.</param>
public sealed record CertificationStageResult
{
    public CertificationStage Stage { get; init; }
    public VerificationLevel Level { get; init; }
    public string Evidence { get; init; }
    public EquatableArray<string> Blockers { get; init; }

    public CertificationStageResult(
        CertificationStage stage,
        VerificationLevel level,
        string evidence,
        EquatableArray<string> blockers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidence);
        if (level == VerificationLevel.Verified && blockers.Count != 0)
        {
            throw new ArgumentException(
                "A Verified stage cannot carry blockers -- if something still blocks it, it is not Verified.",
                nameof(blockers));
        }

        Stage = stage;
        Level = level;
        Evidence = evidence;
        Blockers = blockers;
    }
}

/// <summary>
/// The full scorecard docs/ROADMAP_ESECUTIVA.md S:AP-10 asks for: every
/// <see cref="CertificationStage"/>'s current, evidence-backed maturity.
/// This type never runs anything itself -- it is the shape a certification
/// assessment is recorded in, the same boundary every other phase's
/// planner draws between deciding and executing.
/// </summary>
/// <param name="Stages">One result per stage. A stage missing from this array is not the same as an Unknown/low result -- callers should treat an incomplete report as itself a finding.</param>
/// <param name="ObservedAtUtc">When this report was compiled.</param>
public sealed record CertificationReport(
    EquatableArray<CertificationStageResult> Stages,
    DateTime ObservedAtUtc)
{
    /// <summary>
    /// The weakest stage's level -- a certification is only as strong as
    /// its weakest stage, the same principle
    /// docs/ROADMAP_ESECUTIVA.md S:AP-10's own "scenario completo" wording
    /// implies (every stage in one continuous run, not any one in
    /// isolation). <see langword="null"/> when <see cref="Stages"/> is
    /// empty -- nothing to report a level for, never defaulted to
    /// <see cref="VerificationLevel.Present"/> by omission.
    /// </summary>
    public VerificationLevel? OverallLevel
    {
        get
        {
            if (Stages.Count == 0)
                return null;

            VerificationLevel lowest = Stages[0].Level;
            for (int i = 1; i < Stages.Count; i++)
            {
                if (Stages[i].Level < lowest)
                    lowest = Stages[i].Level;
            }

            return lowest;
        }
    }

    /// <summary>True only when every declared stage is <see cref="VerificationLevel.Verified"/> and all 14 stages are present.</summary>
    public bool IsFullyCertified => Stages.Count == 14 && OverallLevel == VerificationLevel.Verified;
}
