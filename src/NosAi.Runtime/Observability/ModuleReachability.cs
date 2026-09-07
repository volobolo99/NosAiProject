// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Observability — Which modules the runtime actually reaches, declared and checked
// ============================================================================
//
// The audit of 2026-08-30 found roughly 1,800 lines that nothing outside their
// own namespace referenced, and made the point this file exists to keep:
// "renderle eseguibili non le rende integrate" — a green suite over a module
// nobody calls proves the module works on its own, not that it is used.
//
// That finding was written into a document, and a document decays. Modules get
// wired, modules get written and left unwired, and nobody re-runs the analysis.
// So the classification lives here as data, and ModuleReachabilityTests derives
// the same classification from the source and fails when the two disagree.
// Adding a namespace without declaring it fails. Declaring one Integrated when
// nothing reaches it fails. Leaving a stale Unreferenced on a module that has
// since been wired fails too.

using System.Collections.Immutable;

namespace NosAi.Runtime.Observability;

/// <summary>How far a module is from the runtime's own execution paths.</summary>
public enum ModuleReach : byte
{
    /// <summary>Production code outside the module reaches it.</summary>
    Integrated = 0,

    /// <summary>
    /// Only the certification suite registry reaches it.
    /// </summary>
    /// <remarks>
    /// The module can be exercised and its suite can pass, and none of that says
    /// the runtime uses it. This is the distinction the audit asked to be kept
    /// visible, and the reason it is a status of its own rather than a footnote
    /// on <see cref="Integrated"/>.
    /// </remarks>
    SuiteOnly = 1,

    /// <summary>Nothing reaches it — not the runtime, not even a suite.</summary>
    Unreferenced = 2,
}

/// <summary>One module and how the runtime reaches it.</summary>
/// <param name="Namespace">The module's namespace, as declared in its files.</param>
/// <param name="Reach">What the source actually shows, not what is intended.</param>
/// <param name="Note">
/// Why it is where it is, and what would move it. Required for anything that is
/// not <see cref="ModuleReach.Integrated"/>: an unreached module without a
/// stated reason is indistinguishable from one nobody has looked at.
/// </param>
public sealed record ModuleRecord(string Namespace, ModuleReach Reach, string Note = "");

/// <summary>
/// The declared reachability of every module in this repository's production
/// source.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reachability is transitive here.</b> A module reached only from a module
/// nothing reaches is not reached. Counting the direct reference would have
/// reported <c>NosAi.Economy.Inventory</c> and <c>NosAi.Navigation.Pathfinding</c>
/// as integrated on the strength of <c>NosAiCapabilityKernel</c> — which is
/// itself in a namespace nothing calls.
/// </para>
/// <para>
/// <b>References inside comments do not count.</b> A <c>&lt;see cref="..."/&gt;</c>
/// naming a type is documentation, not use, and counting it would let a module
/// be talked about into looking integrated.
/// </para>
/// <para>
/// <b>Tests do not count either.</b> A module exercised only by its tests is not
/// wired into the runtime, which is the whole question being asked.
/// </para>
/// <para>
/// <b>All of <c>src/</c> is scanned, not one project.</b> Until 2026-09-07 the
/// analysis read only <c>src/NosAi.Runtime</c>, which made every referrer
/// outside it invisible -- and an invisible referrer is indistinguishable from
/// no referrer. That is the pessimistic error, and it had already produced one:
/// <c>NosAi.Host</c> was declared SuiteOnly while its own executable's
/// <c>Program.cs</c> reaches it. <c>NosAi.GuardAi.App</c> is the one project
/// left out, because ADR-0025 §3 freezes it as historical reference: it is
/// unreached by decision, and mixing that in would drown the entries that are
/// debt.
/// </para>
/// <para>
/// <b>One verdict here is an artifact, and saying so is cheaper than chasing
/// it.</b> <c>NosAi.Runtime</c> is declared Integrated and contains exactly one
/// file, <c>Program.cs</c> -- the entry point, which by definition nothing can
/// reference. Its apparent referrers were checked one by one and every one is a
/// string: assembly and project file names (<c>NosAi.Runtime.dll</c>,
/// <c>.csproj</c>, <c>.exe</c>), the test assembly's name, and
/// <c>NosAi.Runtime.AI</c> -- a segment of <c>NosAi.Runtime.AI.Decision</c> that
/// is not itself a namespace. Distinguishing those from a real type reference
/// needs a rule about C# identifiers rather than about namespaces, and it would
/// buy one correct verdict on a namespace whose status nobody can act on.
/// </para>
/// <para>
/// The <i>other</i> half of that imprecision was real and is fixed: a plain
/// substring match let a parent namespace inherit its children's referrers,
/// which reported <c>NosAi.Core.Planning</c> as reached the moment this register
/// declared <c>NosAi.Core.Planning.Goap</c> -- the register making its own
/// subject look alive. <c>ModuleReachabilityTests.NamesATypeIn</c> now requires
/// the text after the dot to not complete a declared namespace.
/// </para>
/// </remarks>
public static class ModuleReachability
{
    public static ImmutableArray<ModuleRecord> Modules { get; } =
    [
        // -- reached by the runtime's own paths ------------------------------
        new("NosAi.LiveIntegration", ModuleReach.Integrated),
        new("NosAi.Runtime", ModuleReach.Integrated),
        new("NosAi.Runtime.AI.Decision", ModuleReach.Integrated),
        new("NosAi.Runtime.Adapters", ModuleReach.Integrated),
        new("NosAi.Runtime.Autonomy", ModuleReach.Integrated),
        new("NosAi.Runtime.Configuration", ModuleReach.Integrated),
        new("NosAi.Runtime.Contracts", ModuleReach.Integrated),
        new("NosAi.Runtime.Gate1", ModuleReach.Integrated),
        new("NosAi.Runtime.Gate2", ModuleReach.Integrated),
        new("NosAi.Runtime.Gate3", ModuleReach.Integrated),
        new("NosAi.Runtime.Gate4", ModuleReach.Integrated),
        new("NosAi.Runtime.Gate5", ModuleReach.Integrated),
        new("NosAi.Runtime.Guard", ModuleReach.Integrated),
        new("NosAi.Runtime.Hardware", ModuleReach.Integrated),
        new("NosAi.Runtime.Humanizer", ModuleReach.Integrated),
        new("NosAi.Runtime.LowLevel", ModuleReach.Integrated),
        new("NosAi.Runtime.Operator", ModuleReach.Integrated),
        new("NosAi.Runtime.Observability", ModuleReach.Integrated),
        new("NosAi.Runtime.Orchestration", ModuleReach.Integrated),
        new("NosAi.Runtime.Perception", ModuleReach.Integrated),
        new("NosAi.Runtime.Perception.Network", ModuleReach.Integrated),
        new("NosAi.Runtime.Safety", ModuleReach.Integrated),
        new("NosAi.Runtime.Security", ModuleReach.Integrated),
        new("NosAi.Runtime.Tactical", ModuleReach.Integrated),
        new("NosAi.Runtime.Testing", ModuleReach.Integrated),
        new("NosAi.Runtime.WorldModel", ModuleReach.Integrated),
        new("NosAi.Runtime.WorldModel.Fusion", ModuleReach.Integrated),
        new("NosAi.Runtime.GameData", ModuleReach.Integrated),
        new("NosAi.Runtime.Navigation", ModuleReach.Integrated),
        new("NosAi.Navigation.Pathfinding", ModuleReach.Integrated),

        // -- NosAi.Core, NosAi.Adapter and the other production assemblies ---
        // Visible here only since the scan covers all of src/. The Core World
        // Model namespaces are reached from the runtime's own commands, which is
        // what makes the AP-01..AP-05 contracts something the runtime uses
        // rather than something it merely compiles against.
        new("NosAi.Adapter", ModuleReach.Integrated),
        new("NosAi.Core", ModuleReach.Integrated),
        new("NosAi.Core.Cognitive", ModuleReach.Integrated),
        new("NosAi.Core.CharacterControl", ModuleReach.Integrated),
        new("NosAi.Core.Game", ModuleReach.Integrated,
            "Reached, but by inheritance rather than by use, and the distinction "
            + "is worth keeping: this register works at namespace granularity, so "
            + "GameFunctionCatalog counts as reached because CharacterActionPlanner "
            + "sits in a namespace EngageCommand now calls into -- for the guard, "
            + "not for the planner. The catalogue's only caller is still that "
            + "planner, and still nothing calls it."),
        new("NosAi.Core.Memory", ModuleReach.Integrated),
        new("NosAi.Core.Navigation", ModuleReach.Integrated),
        new("NosAi.Core.Testing", ModuleReach.Integrated),
        new("NosAi.Core.WorldModel", ModuleReach.Integrated),
        new("NosAi.Core.WorldModel.Combat", ModuleReach.Integrated),
        new("NosAi.Core.WorldModel.Exploration", ModuleReach.Integrated),
        new("NosAi.Core.WorldModel.Loadout", ModuleReach.Integrated),
        new("NosAi.Core.WorldModel.Quests", ModuleReach.Integrated),
        new("NosAi.Core.WorldModel.Reconstruction", ModuleReach.Integrated),
        new("NosAi.Core.WorldModel.Strategy", ModuleReach.Integrated),
        new("NosAi.Core.WorldModel.Temporal", ModuleReach.Integrated),
        new("NosAi.Host", ModuleReach.Integrated),
        new("NosAi.Security", ModuleReach.Integrated),
        new("NosAi.Storage", ModuleReach.Integrated),

        // -- reachable only through the certification suite registry ---------
        new("NosAi.AI.LocalInference", ModuleReach.SuiteOnly,
            "Declared SIMULATED inference. Nothing routes to it: Gate 5's provider "
            + "router is what production asks for a provider, and it does not know "
            + "this one. Wiring it means registering it in the router, and doing so "
            + "without keeping the SIMULATED label would be the worse outcome."),

        new("NosAi.Economy.Inventory", ModuleReach.SuiteOnly,
            "Reached only from NosAiCapabilityKernel, which is itself in an "
            + "unreferenced namespace — so the direct reference is not a path. It "
            + "also has no real input: inventory and prices come from the game, and "
            + "the gameplay provider reports UNKNOWN until a protocol map exists."),

        new("NosAi.Hardware.Autoscale", ModuleReach.SuiteOnly,
            "Duplicates in intent what NosAi.Runtime.Hardware does in fact: the "
            + "Gate 1 snapshot takes its hardware baseline from the latter. Which "
            + "of the two survives is a decision, not a wiring job."),

        new("NosAi.Miniland.Production", ModuleReach.SuiteOnly,
            "Miniland automation over an adapter that has no live game behind it."),

        new("NosAi.Network.Gateway", ModuleReach.SuiteOnly,
            "A second gateway alongside the Gate 1 operator API that the dashboard "
            + "and the Control Panel actually talk to."),

        new("NosAi.Raids.Dodekatheon", ModuleReach.SuiteOnly,
            "Endgame raid orchestration. Far downstream of a runtime that is not "
            + "yet allowed to execute anything."),

        new("NosAi.Runtime.Gate6", ModuleReach.SuiteOnly,
            "By design, and the one entry here that is not debt: Gate 6 is a "
            + "certification of integration. Nothing in production should call it, "
            + "and it is listed so that stays a deliberate fact rather than an "
            + "oversight nobody re-checked."),

        new("NosAi.Storage.Infrastructure", ModuleReach.SuiteOnly,
            "The second SQLite implementation. Gate 2's is the one applied to a "
            + "real connection; this one renders a PRAGMA script. Its values are "
            + "now read from Gate 2 so the two cannot diverge, but which survives "
            + "is still open and belongs to two authors."),

        // -- nothing reaches them at all -------------------------------------

        // Two of these are unreached and not debt: an executable's own top
        // namespace has nothing above it to be reached from, and a library whose
        // consumers are all outside this scan is not the same as a library
        // nobody uses. They are declared here rather than exempted so the fact
        // stays checked.
        new("NosAi.ControlPanel", ModuleReach.Unreferenced,
            "Not debt: it is an application. The WPF Control Panel is a process "
            + "of its own -- it reaches into the runtime, and nothing reaches "
            + "into it, which is what being a top-level entry point means."),

        new("NosAi.GuardClient", ModuleReach.Unreferenced,
            "Not debt either, but worth watching: the PC-side library of the "
            + "phone channel. ADR-0025 S:2 restored it deliberately because six "
            + "test files exercise the real Gate 1 handshake against it, and its "
            + "other consumer -- the Android app -- is frozen out of the solution "
            + "by S:3 of the same ADR. If the mobile channel never returns, this "
            + "is a library kept alive by its own tests."),

        new("NosAi.Adapter.DirectEngine", ModuleReach.Unreferenced,
            "1 973 lines, 15 files, 27 types, and the two methods at the centre "
            + "of it refuse: DirectEngineAdapter.ReadState and .Execute both "
            + "return EngineRefusalCode.NotImplemented. Its own doc comment calls "
            + "it a declared seam rather than a hidden placeholder, which is "
            + "honest -- but a seam nothing has ever attached to."),

        new("NosAi.Core.Hardware", ModuleReach.Unreferenced,
            "The tier/capability contracts of AP-00. Reached only from "
            + "NosAi.Core.Scheduling, which nothing reaches; the runtime takes "
            + "its hardware baseline from NosAi.Runtime.Hardware instead. Which "
            + "of the two survives is the same open decision "
            + "NosAi.Hardware.Autoscale carries."),

        new("NosAi.Core.Knowledge", ModuleReach.Unreferenced,
            "Adaptive knowledge contracts, reached only from a mission-strategy "
            + "adapter that is itself unreached."),

        new("NosAi.Core.Perception", ModuleReach.Unreferenced,
            "18 lines, one file, and the only namespace in this register that "
            + "not even a test references. Perception in production is "
            + "NosAi.Runtime.Perception."),

        new("NosAi.Core.Planning", ModuleReach.Unreferenced,
            "The HTN/GOAP layer named in CLAUDE.md's own canonical flow "
            + "(Ranking -> Strategic Orchestrator -> HTN/GOAP -> Guard). No "
            + "production file reaches it; planning that runs today is "
            + "StrategyPlanner plus Gate3's own loop. LexicographicOrchestrator "
            + "and DeadlinePlanner are not reached even by a test. There is also "
            + "a second GoalStack here beside the one Gate3Runtime actually uses "
            + "(NosAi.Runtime.Autonomy) -- two types, one name, one of them dead."),

        new("NosAi.Core.Planning.Goap", ModuleReach.Unreferenced,
            "The GOAP half of the layer above, unreached for the same reason."),

        new("NosAi.Core.Progression", ModuleReach.Unreferenced,
            "Character progression contracts. Gate 4 does progression in the "
            + "runtime and does not consult these."),

        new("NosAi.Core.Safety", ModuleReach.Unreferenced,
            "41 lines. Safety that is authoritative lives in NosAi.Runtime.Safety, "
            + "which the architecture requires ('Runtime is authoritative for "
            + "authorization and safety'); this one is reached by tests only."),

        new("NosAi.Core.Scheduling", ModuleReach.Unreferenced,
            "887 lines of tier queue and async execution, the largest unreached "
            + "module in NosAi.Core. It is what would carry NosAi.Core.Hardware "
            + "into use, and nothing carries it."),

        new("NosAi.Core.Statistics", ModuleReach.Unreferenced,
            "Statistical helpers. Gate 4's BetaBinomialEvidence is the one the "
            + "runtime uses."),

        new("NosAi.Core.WorldModel.Certification", ModuleReach.Unreferenced,
            "Certification contracts for the World Model, beside the runtime's "
            + "own certification suites. Unreached by production, like Gate6 -- "
            + "but unlike Gate6 it is not even reached by the suite registry."),

        new("NosAi.Events.InstantBattle", ModuleReach.Unreferenced,
            "Instant Combat and timed events. No caller and no suite."),

        new("NosAi.LiveIntegration.Capture", ModuleReach.Integrated,
            "The traffic capture engine: WinDivert source, IPv4/TCP parser, "
            + "reassembly, .noscap record and replay, analyser. Reached from "
            + "Gate1ObservationChannel, which the bootstrap host builds when the "
            + "operator names an endpoint with --observe-game. Attaching it stays "
            + "the operator's decision under ADR-0014: with no endpoint the "
            + "channel is not built and gameplay keeps reporting UNKNOWN."),

        new("NosAi.Raids.Orchestration", ModuleReach.Unreferenced,
            "A second raid module beside NosAi.Raids.Dodekatheon, and the one "
            + "without even a suite."),

        new("NosAi.Runtime.Capabilities", ModuleReach.Unreferenced,
            "NosAiCapabilityKernel, which composes Economy and Navigation. It is "
            + "the reason both of those look integrated from a direct reference "
            + "count and are not: nothing composes the composer."),

        new("NosAi.Runtime.Learning", ModuleReach.Unreferenced,
            "PredictionLedger. It only learns from LIVE outcomes by design, and "
            + "there are no LIVE gameplay outcomes to learn from yet, so wiring it "
            + "now would give it nothing to do."),

        new("NosAi.Runtime.PlayAi", ModuleReach.Unreferenced,
            "Fifteen lines with no caller."),

        new("NosAi.Runtime.Telemetry", ModuleReach.Unreferenced,
            "Superseded in practice by the durable event log in Gate 2, which is "
            + "what the runtime records through."),
    ];

    /// <summary>Modules at a given reach, in declaration order.</summary>
    public static IEnumerable<ModuleRecord> With(ModuleReach reach) =>
        Modules.Where(m => m.Reach == reach);

    /// <summary>
    /// A plain-text report for the operator.
    /// </summary>
    /// <remarks>
    /// Printed by <c>--module-report</c>. The counts come first because the useful
    /// question is how much of the assembly the runtime actually runs, and that
    /// number is easy to lose behind a list.
    /// </remarks>
    public static string Report()
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine("Module reachability (source of truth: ModuleReachabilityTests)");
        text.AppendLine();
        foreach (ModuleReach reach in new[] { ModuleReach.Integrated, ModuleReach.SuiteOnly, ModuleReach.Unreferenced })
        {
            ModuleRecord[] group = With(reach).ToArray();
            text.AppendLine($"== {reach} ({group.Length}) ==");
            foreach (ModuleRecord module in group)
            {
                text.AppendLine($"  {module.Namespace}");
                if (module.Note.Length > 0)
                    text.AppendLine($"      {module.Note}");
            }
            text.AppendLine();
        }
        return text.ToString();
    }
}
