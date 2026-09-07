using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Certification;
using NosAi.Core.WorldModel.Exploration;
using NosAi.Core.WorldModel.Quests;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Navigation;
using NosAi.Runtime.Observability;
using NosAi.Runtime.Perception.Network;

namespace NosAi.Runtime.Testing;

/// <summary>
/// The stages of AP-10's end-to-end scenario that no certification suite
/// covered: map discovery, exploration, target recognition, multi-step quest.
/// </summary>
/// <remarks>
/// <para>
/// <c>--certification-report</c> reported those four as <c>Present</c> with
/// <c>no_suite_evidences_this_stage</c>, which was true and was the first time
/// the gap had been visible: the certification machinery answered one
/// <see langword="bool"/> for the whole product, so a stage nothing exercised
/// looked exactly like a stage that passed.
/// </para>
/// <para>
/// The checks below exercise the real production code and assert the property
/// that would actually matter if it broke -- what each of these components does
/// when it does <b>not</b> know something. That is the direction this project's
/// invariants run in: a frontier planner that invents a destination, a target
/// establishment that reads an unknown vnum as a monster, or a quest counter
/// that reads an unread inventory as zero are the failures worth catching, and
/// they are all silent.
/// </para>
/// <para>
/// Two recorded checks run beside the synthetic ones when the recordings are
/// present, proving the material those four components consume is what the wire
/// really carries rather than only what a fixture described. MapDiscovery and
/// MultiStepQuest have no recorded check because the wire carries neither a map
/// id (a <c>c_map</c> change packet exists and no capture in the archive holds
/// one) nor quest state; TargetRecognition rests on the vnum a real capture
/// carries and Exploration on the distinct positions a real capture observes.
/// </para>
/// <para>
/// Nothing here touches a live client, so nothing here is evidence for
/// <c>Verified</c>; <see cref="CertificationReportBuilder"/> enforces that
/// ceiling regardless of what these return.
/// </para>
/// </remarks>
public static class ScenarioStageTestRunner
{
    private static readonly DateTime Now = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly MapId Map = new("map-1");

    /// <summary>Runs every check and reports each one by name.</summary>
    public static Task<bool> RunAllTestsAsync() => RunAllTestsAsync(captureDirectory: null);

    /// <summary>
    /// Runs every check, resolving recordings from <paramref name="captureDirectory"/>
    /// when it is set instead of the environment or the repository's <c>data/</c>.
    /// </summary>
    /// <remarks>
    /// The recorded checks are added beside the synthetic ones, never instead of
    /// them: the fixtures prove the shape on every machine, the recordings prove
    /// that shape is what the wire really carries. On a clone without the
    /// recordings the recorded checks skip with the reason printed, and the
    /// synthetic ones carry the load -- a skip is loud and is never a pass.
    /// </remarks>
    internal static Task<bool> RunAllTestsAsync(string? captureDirectory)
    {
        Console.WriteLine("=== Scenario stages - map discovery, exploration, target recognition, quests ===");

        bool allPassed = true;

        allPassed &= Run("Map extraction without a client names why, and invents no path", MapExtractionNamesItsFailure);
        allPassed &= Run("A missing map catalogue is refused, not read as an empty one", MissingCatalogueIsRefused);

        allPassed &= Run("An unvisited map has an UNKNOWN footprint, not an explored one", EmptyFootprintIsUnknown);
        allPassed &= Run("No frontier is invented for a map nothing has been observed on", NoFrontierWithoutObservation);
        allPassed &= Run("No frontier selected means an unreachable plan, not an empty route", NoFrontierYieldsUnreachable);
        allPassed &= Run("A footprint for another map is refused loudly, never merged", FootprintOfAnotherMapIsRefused);

        allPassed &= Run("An entity that hit us is established by that evidence alone", HitEstablishesTarget);
        allPassed &= Run("An entity with no vnum is not established, and says so", NoVnumIsNotEstablished);
        allPassed &= Run("Without a catalogue no vnum becomes a monster", NoCatalogueEstablishesNothing);

        allPassed &= Run("A quest with unmet prerequisites is not startable", PrerequisitesGateStartability);
        allPassed &= Run("The next objective is the first incomplete one", NextObjectiveIsTheFirstIncomplete);
        allPassed &= Run("An unobserved inventory makes collect progress UNKNOWN, not zero", UnobservedInventoryIsUnknown);
        allPassed &= Run("An observed inventory without the item is a known zero", ObservedInventoryWithoutItemIsZero);

        var recorded = new HashSet<CertificationStage>();
        allPassed &= RunRecorded(
            CertificationStage.TargetRecognition,
            "A recorded capture carries entities whose vnum the wire stated",
            TargetRecognitionRecording,
            CaptureCarriesVnum,
            recorded,
            captureDirectory);
        allPassed &= RunRecorded(
            CertificationStage.Exploration,
            "A recorded capture carries distinct entities at observed positions",
            ExplorationRecording,
            CaptureCarriesDistinctPositions,
            recorded,
            captureDirectory);

        LastRecordedEvidence = recorded;

        Console.WriteLine(allPassed
            ? "=== Scenario stage checks passed. Local only: this is not real-environment verification. ==="
            : "=== Scenario stage checks FAILED. See the lines marked FAIL above. ===");

        return Task.FromResult(allPassed);
    }

    /// <summary>
    /// The stages whose recorded checks ran and passed on the most recent
    /// scenario run, exposed so <see cref="CertificationReportBuilder.Build"/>
    /// can say "both" for them rather than "synthetic".
    /// </summary>
    public static IReadOnlySet<CertificationStage> LastRecordedEvidence { get; private set; }
        = new HashSet<CertificationStage>();

    /// <summary>The environment variable that relocates the recordings, as the test suite's own.</summary>
    public const string RecordedCaptureDirectoryVariable = "NOSAI_CAPTURE_DIR";

    /// <summary>The recording that carries entities with a stated vnum.</summary>
    private const string TargetRecognitionRecording = "nostale_combat.noscap";

    /// <summary>The recording that carries entities moving across observed positions.</summary>
    private const string ExplorationRecording = "nostale_01.noscap";

    private static bool RunRecorded(
        CertificationStage stage,
        string name,
        string recording,
        Func<string, bool> check,
        HashSet<CertificationStage> recorded,
        string? captureDirectory)
    {
        string? path = ResolveRecording(recording, captureDirectory);
        if (path is null)
        {
            // A skip is loud and is not a pass: the stage is not added to
            // `recorded`, so the report says "synthetic", not "both".
            Console.WriteLine($"[SKIP] {name} [{recording} assente sotto data/; evidenza registrata non raccolta]");
            return true;
        }

        try
        {
            bool passed = check(path);
            Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] {name}");
            if (passed)
                recorded.Add(stage);
            return passed;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] {name} [{ex.GetType().Name}: {ex.Message}]");
            return false;
        }
    }

    /// <summary>
    /// Resolves a recording the way the test suite's
    /// <see cref="RecordedCaptureFactAttribute"/> does: an explicit directory
    /// (or <c>NOSAI_CAPTURE_DIR</c>) first, then the repository's <c>data/</c>.
    /// </summary>
    internal static string? ResolveRecording(string fileName, string? captureDirectory = null)
    {
        string? directory = captureDirectory
            ?? Environment.GetEnvironmentVariable(RecordedCaptureDirectoryVariable);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            string path = Path.Combine(directory, fileName);
            return File.Exists(path) ? path : null;
        }

        string repo = TestSuiteRunner.FindRepositoryRoot(Environment.CurrentDirectory)
                      ?? TestSuiteRunner.FindRepositoryRoot()
                      ?? Directory.GetCurrentDirectory();
        string repoPath = Path.Combine(repo, "data", fileName);
        return File.Exists(repoPath) ? repoPath : null;
    }

    /// <summary>
    /// The wire-level fact target recognition consumes: a real capture carries
    /// at least one entity whose vnum the wire stated, not one defaulted from a
    /// neighbour.
    /// </summary>
    internal static bool CaptureCarriesVnum(string path)
    {
        WorldReplayReport report = WorldReplayCommand.InspectFile(path);
        return report.Ok && report.Entities.Any(e => IsReadVnum(e.VnumText));
    }

    /// <summary>
    /// The wire-level fact exploration consumes: a real capture carries more
    /// than one distinct observed position, the raw material a footprint is
    /// built from.
    /// </summary>
    internal static bool CaptureCarriesDistinctPositions(string path)
    {
        WorldReplayReport report = WorldReplayCommand.InspectFile(path);
        return report.Ok && report.Entities.Select(e => (e.X, e.Y)).Distinct().Count() > 1;
    }

    private static bool IsReadVnum(string vnumText) =>
        vnumText != WorldReplayCommand.VnumNotRead && vnumText != WorldReplayCommand.VnumAbsent;

    private static bool Run(string name, Func<bool> check)
    {
        try
        {
            bool passed = check();
            Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] {name}");
            return passed;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] {name} [{ex.GetType().Name}: {ex.Message}]");
            return false;
        }
    }

    // ------------------------------------------------------- map discovery

    /// <summary>
    /// With no client installed the extractor must name the reason. A path
    /// returned anyway would be read as "extract from here" by the only caller
    /// that matters.
    /// </summary>
    private static bool MapExtractionNamesItsFailure()
    {
        bool resolved = MapGridExtractor.TryResolveDedicatedMapsDirectory(out string path, out string? reason);

        // Either it found a real directory on this machine, or it refused and
        // said why. What it must never do is refuse silently.
        return resolved
            ? !string.IsNullOrWhiteSpace(path) && Directory.Exists(path)
            : !string.IsNullOrWhiteSpace(reason);
    }

    /// <summary>
    /// A catalogue that is not there is not a catalogue with no maps in it. The
    /// second reads as "this client has no maps", which is a fact about the
    /// world rather than about the reader.
    /// </summary>
    private static bool MissingCatalogueIsRefused()
    {
        string absent = Path.Combine(Path.GetTempPath(), $"nosai-no-maps-{Guid.NewGuid():N}");

        bool loaded = MapGridExtractor.TryLoadCatalog(absent, out _, out string? failure);

        return !loaded && !string.IsNullOrWhiteSpace(failure);
    }

    // --------------------------------------------------------- exploration

    private static bool EmptyFootprintIsUnknown()
    {
        ExplorationFootprint footprint = ExplorationFootprint.Empty(Map, "never_observed_on_this_map", Now);

        // Not "explored: false" -- nobody has looked.
        return footprint.VisitedTiles.Count == 0
               && !footprint.FullyExplored.HasValue
               && footprint.FullyExplored.Reason == "never_observed_on_this_map";
    }

    private static bool NoFrontierWithoutObservation()
    {
        MapModel unknownMap = MapModel.Unknown(Map, "map_never_observed", Now);
        ExplorationFootprint footprint = ExplorationFootprint.Empty(Map, "never_observed_on_this_map", Now);

        IReadOnlyList<FrontierCandidate> candidates = ExplorationPlanner.BuildFrontierCandidates(
            unknownMap, footprint, new WorldPosition(0, 0), EquatableArray<Mob>.Empty);

        return candidates.Count == 0;
    }

    private static bool NoFrontierYieldsUnreachable()
    {
        FrontierCandidate? none = ExplorationPlanner.SelectNextFrontier(Array.Empty<FrontierCandidate>());
        NavigationPlan plan = ExplorationPlanner.BuildNavigationPlan(Map, none, Now);

        // An empty waypoint list alone is ambiguous, which the contract's own
        // remarks say: the fact must be Unknown or false, never absent.
        return none is null
               && plan.Waypoints.Count == 0
               && (!plan.IsReachable.HasValue || !plan.IsReachable.Value);
    }

    /// <summary>
    /// Scoring one map's frontier against another map's footprint is refused
    /// loudly, not answered with an empty list.
    /// </summary>
    /// <remarks>
    /// This check was first written expecting an empty result, and the planner
    /// throws instead -- which is stronger. An empty list reads as "nothing left
    /// to explore here" and would quietly end exploration; the exception says
    /// the caller mixed two maps' state, which is a bug in the caller and not a
    /// fact about the world.
    /// </remarks>
    private static bool FootprintOfAnotherMapIsRefused()
    {
        MapModel map = MapModel.Unknown(Map, "map_never_observed", Now);
        ExplorationFootprint elsewhere = ExplorationFootprint.Empty(new MapId("map-2"), "other_map", Now);

        try
        {
            ExplorationPlanner.BuildFrontierCandidates(
                map, elsewhere, new WorldPosition(0, 0), EquatableArray<Mob>.Empty);
            return false;
        }
        catch (ArgumentException ex)
        {
            return ex.Message.Contains("map-2", StringComparison.Ordinal)
                   && ex.Message.Contains("map-1", StringComparison.Ordinal);
        }
    }

    // -------------------------------------------------- target recognition

    private static SelectableEntity Entity(int? vnum = null) =>
        new(313816, new MapPoint(10, 10), 1.0, Now, vnum);

    private static bool HitEstablishesTarget()
    {
        var aggressor = ClassifiedValue<Aggressor>.Live(new Aggressor(313816, 3), Now);

        TargetVerdict verdict = TargetEstablishment.Assess(Entity(), aggressor, selected: null, catalogue: null);

        return verdict.IsEstablished && verdict.Evidence == TargetEvidence.AttackedUs;
    }

    private static bool NoVnumIsNotEstablished()
    {
        TargetVerdict verdict = TargetEstablishment.Assess(Entity(), hitBy: null, selected: null, catalogue: null);

        // Most entities are located long before anything says what they are, and
        // that is not a licence to swing at them.
        return !verdict.IsEstablished && !string.IsNullOrWhiteSpace(verdict.Reason);
    }

    private static bool NoCatalogueEstablishesNothing()
    {
        TargetVerdict verdict = TargetEstablishment.Assess(
            Entity(vnum: 36), hitBy: null, selected: null, catalogue: null);

        CatalogueLookup lookup = TargetEstablishment.ClassifyByCatalogue(36, catalogue: null);

        return !verdict.IsEstablished && lookup.Class == CatalogueClass.CatalogueNotLoaded;
    }

    // ------------------------------------------------------ multi-step quest

    private static QuestObjective Objective(QuestObjectiveStatus status) => new(
        WorldFact<string>.Live("obiettivo", 1d, Now),
        WorldFact<int>.Live(status == QuestObjectiveStatus.Completed ? 1 : 0, 1d, Now),
        WorldFact<int>.Live(1, 1d, Now),
        WorldFact<QuestObjectiveStatus>.Live(status, 1d, Now));

    private static bool PrerequisitesGateStartability()
    {
        var node = new QuestNode(
            new QuestId("second"),
            EquatableArray<QuestId>.From(new[] { new QuestId("first") }),
            EquatableArray<QuestReward>.Empty);

        bool withoutPrerequisite = QuestGraphPlanner.IsStartable(node, EquatableArray<Quest>.Empty);

        var completedFirst = new Quest(
            new QuestId("first"),
            WorldFact<string>.Live("prima", 1d, Now),
            WorldFact<QuestObjectiveStatus>.Live(QuestObjectiveStatus.Completed, 1d, Now),
            EquatableArray<QuestObjective>.From(new[] { Objective(QuestObjectiveStatus.Completed) }));

        bool withPrerequisite = QuestGraphPlanner.IsStartable(
            node, EquatableArray<Quest>.From(new[] { completedFirst }));

        return !withoutPrerequisite && withPrerequisite;
    }

    private static bool NextObjectiveIsTheFirstIncomplete()
    {
        var quest = new Quest(
            new QuestId("q"),
            WorldFact<string>.Live("quest", 1d, Now),
            WorldFact<QuestObjectiveStatus>.Live(QuestObjectiveStatus.InProgress, 1d, Now),
            EquatableArray<QuestObjective>.From(new[]
            {
                Objective(QuestObjectiveStatus.Completed),
                Objective(QuestObjectiveStatus.InProgress),
            }));

        QuestObjective? next = QuestGraphPlanner.NextIncompleteObjective(quest);

        return next is not null
               && next.Status.HasValue
               && next.Status.Value == QuestObjectiveStatus.InProgress;
    }

    private static bool UnobservedInventoryIsUnknown()
    {
        var target = new QuestObjectiveTarget(QuestObjectiveKind.Collect, item: new ItemId("1234"));

        WorldFact<int> progress = QuestGraphPlanner.AssessCollectProgress(
            target,
            WorldFact<EquatableArray<InventoryItem>>.Unknown("inventory_never_read", Now),
            Now);

        return !progress.HasValue && progress.Reason == "inventory_never_read";
    }

    private static bool ObservedInventoryWithoutItemIsZero()
    {
        var target = new QuestObjectiveTarget(QuestObjectiveKind.Collect, item: new ItemId("1234"));

        WorldFact<int> progress = QuestGraphPlanner.AssessCollectProgress(
            target,
            WorldFact<EquatableArray<InventoryItem>>.Live(EquatableArray<InventoryItem>.Empty, 1d, Now),
            Now);

        // The channel spoke and this item is not there: a known zero, not an
        // Unknown. The two used to be the same answer.
        return progress.HasValue && progress.Value == 0;
    }
}
