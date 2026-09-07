using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Certification;

namespace NosAi.Runtime.Testing;

/// <summary>
/// Turns "which certification suites passed" into the per-stage scorecard
/// AP-10 asks for: a level, the evidence behind it, and what is missing.
/// </summary>
/// <remarks>
/// <para>
/// The certification machinery answered a <see langword="bool"/>. That is the
/// one shape a certification must not have: it cannot say <i>which</i> stage is
/// weak, what backs the claim, or what would raise it.
/// <c>NosAi.Core.WorldModel.Certification</c> held exactly that vocabulary --
/// <see cref="VerificationLevel"/>, <see cref="CertificationStageResult"/> with
/// required evidence and named blockers -- and had no caller.
/// </para>
/// <para>
/// <b>Nothing here can report <see cref="VerificationLevel.Verified"/>.</b> That
/// level means validated against the real client per <c>CLAUDE.md</c>'s
/// real-environment rule, and a suite that passes on this machine is not that
/// evidence however green it is. The ceiling is
/// <see cref="VerificationLevel.Integrated"/>, and every stage carries the
/// blocker naming what real-target step would lift it. A builder that could
/// certify the product from its own tests would be the most convincing wrong
/// answer this repository could produce.
/// </para>
/// <para>
/// <b>An unmapped stage says so.</b> Five of the fourteen had no suite when this
/// was written, and reporting that -- rather than borrowing a neighbouring
/// suite's green -- is what got them covered: four now rest on
/// <c>ScenarioStageTestRunner</c>, written for them, and Attach on the gate1
/// checks that were already about attaching and had never been declared. A
/// guessed mapping would have turned a real gap into a passing line and nobody
/// would have written a check.
/// </para>
/// </remarks>
public static class CertificationReportBuilder
{
    /// <summary>Blocker every stage carries: no suite is real-target evidence.</summary>
    public const string RealTargetBlocker =
        "verified_needs_real_target: una suite verde su questa macchina non e' validazione "
        + "sul client reale (CLAUDE.md, regola dell'ambiente reale). Vedi docs/TEST_RIMANDATI.md.";

    /// <summary>Blocker for a stage no suite evidences.</summary>
    public const string NoSuiteBlocker =
        "no_suite_evidences_this_stage: nessuna suite di certificazione copre questo stadio, "
        + "quindi il suo livello non e' sostenuto da nulla che questo processo possa eseguire.";

    /// <summary>
    /// Which suites evidence which stage.
    /// </summary>
    /// <remarks>
    /// Declared, never inferred from a name. A stage is listed only where a
    /// suite's own subject matches it: the mapping is read off
    /// <c>CertificationSuites.All</c>'s descriptions and the checks those runners
    /// print. Where no suite matches, the stage is absent from this table on
    /// purpose -- see <see cref="NoSuiteBlocker"/>.
    /// </remarks>
    public static IReadOnlyDictionary<CertificationStage, IReadOnlyList<string>> SuitesByStage { get; } =
        new Dictionary<CertificationStage, IReadOnlyList<string>>
        {
            [CertificationStage.Startup] = new[] { "gate1" },

            // gate1 evidences two stages, and its own checks split cleanly
            // between them: the wire header, sequence guard, RSA challenge and
            // heartbeat are startup; "Missing client does not invent gameplay",
            // "OS session baseline is LIVE when observed", "UNKNOWN client
            // fields are not published as values" and "Bootstrap without a
            // client reports DEGRADED" are about attaching to one.
            [CertificationStage.Attach] = new[] { "gate1" },

            [CertificationStage.Perception] = new[] { "perception", "netobserve" },

            // Written for these four, which no suite covered: the report said so
            // itself, and that was the first time the gap was visible.
            [CertificationStage.MapDiscovery] = new[] { "scenario" },
            [CertificationStage.Exploration] = new[] { "scenario" },
            [CertificationStage.TargetRecognition] = new[] { "scenario" },
            [CertificationStage.MultiStepQuest] = new[] { "scenario" },
            [CertificationStage.Navigation] = new[] { "navigation" },
            [CertificationStage.Combat] = new[] { "gate3" },
            [CertificationStage.Recovery] = new[] { "gate3" },
            [CertificationStage.LootInventory] = new[] { "economy" },
            [CertificationStage.EquipmentProgression] = new[] { "gate4" },
            [CertificationStage.Persistence] = new[] { "gate2", "storage" },
            [CertificationStage.Evidence] = new[] { "gate6" },
        };

    /// <summary>
    /// Builds the report from each suite's pass/fail.
    /// </summary>
    /// <param name="suitePassed">
    /// Whether a suite passed, by its key. A key absent from this map is a suite
    /// that was not run, which is not the same as one that failed and is reported
    /// as its own blocker.
    /// </param>
    /// <param name="observedAtUtc">When the run happened.</param>
    public static CertificationReport Build(
        IReadOnlyDictionary<string, bool> suitePassed,
        DateTime observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(suitePassed);

        var stages = new List<CertificationStageResult>(14);

        foreach (CertificationStage stage in Enum.GetValues<CertificationStage>())
        {
            if (!SuitesByStage.TryGetValue(stage, out IReadOnlyList<string>? keys))
            {
                stages.Add(new CertificationStageResult(
                    stage,
                    VerificationLevel.Present,
                    evidence: "nessuna suite dichiarata per questo stadio",
                    blockers: EquatableArray<string>.From(new[] { NoSuiteBlocker, RealTargetBlocker })));
                continue;
            }

            var blockers = new List<string>();
            var ran = new List<string>();
            bool allPassed = true;

            foreach (string key in keys)
            {
                if (!suitePassed.TryGetValue(key, out bool passed))
                {
                    blockers.Add($"suite_not_run:{key}");
                    allPassed = false;
                    continue;
                }

                ran.Add(passed ? $"{key}=PASS" : $"{key}=FAIL");
                if (!passed)
                {
                    blockers.Add($"suite_failed:{key}");
                    allPassed = false;
                }
            }

            // Integrated is the ceiling: the suites exercise the wiring, and
            // nothing here has touched a real client.
            blockers.Add(RealTargetBlocker);

            stages.Add(new CertificationStageResult(
                stage,
                allPassed ? VerificationLevel.Integrated : VerificationLevel.Present,
                evidence: ran.Count == 0 ? $"suite dichiarate: {string.Join(", ", keys)}" : string.Join(", ", ran),
                blockers: EquatableArray<string>.From(blockers)));
        }

        return new CertificationReport(EquatableArray<CertificationStageResult>.From(stages), observedAtUtc);
    }
}
