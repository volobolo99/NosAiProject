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
/// The declared reachability of every module in the runtime assembly.
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
        new("NosAi.Runtime.Observability", ModuleReach.Integrated),
        new("NosAi.Runtime.Orchestration", ModuleReach.Integrated),
        new("NosAi.Runtime.Perception", ModuleReach.Integrated),
        new("NosAi.Runtime.Perception.Network", ModuleReach.Integrated),
        new("NosAi.Runtime.Safety", ModuleReach.Integrated),
        new("NosAi.Runtime.Security", ModuleReach.Integrated),
        new("NosAi.Runtime.Tactical", ModuleReach.Integrated),
        new("NosAi.Runtime.Testing", ModuleReach.Integrated),
        new("NosAi.Runtime.WorldModel", ModuleReach.Integrated),

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

        new("NosAi.Host", ModuleReach.SuiteOnly,
            "An alternative runtime host on port 8767. Program.cs boots "
            + "Gate1BootstrapHost instead, so this one runs only under --host-test. "
            + "Two hosts is one more than the project needs."),

        new("NosAi.Miniland.Production", ModuleReach.SuiteOnly,
            "Miniland automation over an adapter that has no live game behind it."),

        new("NosAi.Navigation.Pathfinding", ModuleReach.SuiteOnly,
            "Same transitive story as Economy: reached only from "
            + "NosAiCapabilityKernel. It also refuses to plan across ground nobody "
            + "mapped, and nothing maps ground yet."),

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
        new("NosAi.Events.InstantBattle", ModuleReach.Unreferenced,
            "Instant Combat and timed events. No caller and no suite."),

        new("NosAi.LiveIntegration.Capture", ModuleReach.Unreferenced,
            "The traffic capture engine: WinDivert source, IPv4/TCP parser, "
            + "reassembly, .noscap record and replay, analyser. Complete and "
            + "tested, and no production path constructs it — attaching a capture "
            + "backend is the operator's decision under ADR-0014, and no operator "
            + "surface makes it yet. WinDivert is also not installed, so no real "
            + "traffic has ever been captured."),

        new("NosAi.Raids.Orchestration", ModuleReach.Unreferenced,
            "A second raid module beside NosAi.Raids.Dodekatheon, and the one "
            + "without even a suite."),

        new("NosAi.Runtime.Capabilities", ModuleReach.Unreferenced,
            "NosAiCapabilityKernel, which composes Economy and Navigation. It is "
            + "the reason both of those look integrated from a direct reference "
            + "count and are not: nothing composes the composer."),

        new("NosAi.Runtime.GameData", ModuleReach.Unreferenced,
            "The reference database read out of the client's .NOS archives: "
            + "15,279 records over 1,428,698 values, fully decoded. Nothing reads "
            + "it back yet. It is what a decoded observation would be interpreted "
            + "against, which is a step past having observations at all."),

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
