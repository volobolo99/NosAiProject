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

    /// <summary>Evidence-nature label for a stage backed only by fixtures.</summary>
    public const string SyntheticLabel = "sintetica";

    /// <summary>Evidence-nature label for a stage backed only by recorded bytes.</summary>
    public const string RecordedLabel = "registrata";

    /// <summary>Evidence-nature label for a stage backed by both fixtures and recorded bytes.</summary>
    public const string BothLabel = "entrambe";

    /// <summary>
    /// The nature of the evidence behind a stage, stated rather than left for a
    /// reader to infer from the suite names.
    /// </summary>
    /// <param name="stage">The stage.</param>
    /// <param name="recordedStages">
    /// The stages whose recorded checks ran and passed this run. When null or
    /// empty, no stage has recorded evidence and every stage is synthetic.
    /// </param>
    public static EvidenceNature NatureFor(
        CertificationStage stage,
        IReadOnlySet<CertificationStage>? recordedStages)
    {
        bool synthetic = SuitesByStage.ContainsKey(stage);
        bool recorded = recordedStages?.Contains(stage) == true;
        return (synthetic, recorded) switch
        {
            (true, true) => EvidenceNature.Both,
            (false, true) => EvidenceNature.Recorded,
            _ => EvidenceNature.Synthetic,
        };
    }

    /// <summary>The printed label for a nature, stable enough to assert against.</summary>
    public static string NatureLabel(EvidenceNature nature) => nature switch
    {
        EvidenceNature.Recorded => RecordedLabel,
        EvidenceNature.Both => BothLabel,
        _ => SyntheticLabel,
    };

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
    /// <param name="recordedEvidence">
    /// The stages whose recorded checks ran and passed. When null, the value
    /// <see cref="ScenarioStageTestRunner.LastRecordedEvidence"/> published by the
    /// scenario suite this run is used, so a report compiled after
    /// <c>--scenario-test</c> ran over real recordings says "both" for the stages
    /// those recordings covered and "synthetic" for the rest.
    /// </param>
    public static CertificationReport Build(
        IReadOnlyDictionary<string, bool> suitePassed,
        DateTime observedAtUtc,
        IReadOnlySet<CertificationStage>? recordedEvidence = null)
    {
        ArgumentNullException.ThrowIfNull(suitePassed);
        recordedEvidence ??= ScenarioStageTestRunner.LastRecordedEvidence;

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

            // The nature of the evidence is stated before the suite list, so the
            // printed report distinguishes a stage backed only by fixtures from
            // one a real recording also exercised -- without anyone reading tests.
            string suiteEvidence = ran.Count == 0
                ? $"suite dichiarate: {string.Join(", ", keys)}"
                : string.Join(", ", ran);
            string nature = NatureLabel(NatureFor(stage, recordedEvidence));

            stages.Add(new CertificationStageResult(
                stage,
                allPassed ? VerificationLevel.Integrated : VerificationLevel.Present,
                evidence: $"{nature}: {suiteEvidence}",
                blockers: EquatableArray<string>.From(blockers)));
        }

        return new CertificationReport(EquatableArray<CertificationStageResult>.From(stages), observedAtUtc);
    }
}

/// <summary>
/// What kind of evidence backs a certification stage.
/// </summary>
/// <remarks>
/// A stage whose checks are all in-process fixtures is <see cref="Synthetic"/>;
/// one whose checks ran over a real recording on top of those fixtures is
/// <see cref="Both"/>. <see cref="Recorded"/> alone would be a stage evidenced
/// only by recorded bytes with no fixture coverage, which this builder does not
/// currently produce -- every stage carries synthetic checks, and recordings are
/// added beside them, never instead of them. The value stays in the vocabulary
/// so a report can state the full shape without inventing a new type later.
/// </remarks>
public enum EvidenceNature
{
    /// <summary>Backed only by fixtures built in process.</summary>
    Synthetic = 0,

    /// <summary>Backed only by recorded bytes.</summary>
    Recorded = 1,

    /// <summary>Backed by both fixtures and recorded bytes.</summary>
    Both = 2
}
