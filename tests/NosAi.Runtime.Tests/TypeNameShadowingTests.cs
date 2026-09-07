using System.Text.RegularExpressions;
using NosAi.Runtime.Observability;
using Xunit;
using Xunit.Abstractions;

namespace NosAi.Runtime.Tests;

/// <summary>
/// A type name that lives in both a reached module and an unreached one, which
/// is how the wrong one gets used.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ModuleReachability"/> records which modules the runtime reaches.
/// It cannot record the hazard that follows from an unreached one sharing a name
/// with a live type: <c>using</c> the dead namespace and writing the shared name
/// compiles, runs, and silently uses the copy nobody calls. Nothing about the
/// call site looks wrong.
/// </para>
/// <para>
/// That is not hypothetical here. <c>NosAi.Core.Planning.GoalStack</c> shadows
/// the <c>NosAi.Runtime.Autonomy.GoalStack</c> that <c>Gate3Runtime</c> actually
/// composes, and <c>NosAi.Core.Safety.RecoveryController</c> shadows the
/// <c>NosAi.Runtime.Safety.RecoveryController</c> the same class holds -- two
/// live safety-relevant types with a dead twin one <c>using</c> away.
/// </para>
/// <para>
/// The check does not forbid the shadowing. It forbids the <b>undeclared</b>
/// shadowing: an accepted pair goes in <see cref="Accepted"/> with the reason it
/// is accepted, and a new one fails until somebody writes that reason down.
/// </para>
/// </remarks>
public sealed class TypeNameShadowingTests
{
    private readonly ITestOutputHelper _output;

    public TypeNameShadowingTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Shadowed names that are known and argued, and why.
    /// </summary>
    /// <remarks>
    /// A key here is a claim that the duplication is deliberate. It is not a
    /// suppression list: each entry names the record that decided it, or the
    /// property that makes the pair harmless.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> Accepted =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DataSourceKind"] =
                "ADR-0026 (Accepted): declared once per bounded context because NosAi.Core has zero "
                + "dependencies and hosts more than one domain that must not import each other. The "
                + "three declarations are identical by value and bridged where the domains meet.",

            ["ClassifiedValue"] =
                "Same decision as DataSourceKind: it is that enum's wrapper, and travels with it. "
                + "ADR-0026 argues the pair together.",

            ["ActionExecutionVerifier"] =
                "Gate 6's own copy. Gate 6 is SuiteOnly by design -- it certifies integration and "
                + "nothing in production should call it -- so its types shadow Gate 3's on a path "
                + "production does not take.",

            ["AuthorizedActionExecutor"] = "Gate 6's own copy: see ActionExecutionVerifier.",
            ["ExecutionResult"] = "Gate 6's own copy: see ActionExecutionVerifier.",
            ["Program"] = "Every executable declares one. Entry points, not a shadowed library type.",

            ["CognitiveObservabilityRegistry"] =
                "The Control Panel is an application, unreached because nothing is above it. Its copy "
                + "is the WPF side of a bridge whose runtime side carries the same name on purpose.",

            ["CognitiveRuntimeTraceBridge"] = "The Control Panel's half of the same bridge.",

            ["InventorySlot"] =
                "NosAi.Economy.Inventory's own slot type, beside the ActionTarget.InventorySlot the "
                + "runtime addresses an act with. Different concepts that happen to share a noun; the "
                + "economy module is SuiteOnly and neither is reachable from the other's namespace.",
        };

    /// <summary>
    /// Shadowings that are <b>not</b> argued: real hazards, recorded because
    /// removing them is a decision nobody has taken.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are the ones this check was written for. Each is a live type with a
    /// dead twin one <c>using</c> away, and in every case the dead twin is
    /// earlier scaffolding the runtime grew past: <c>NosAi.Core.Planning</c>
    /// shadows three at once -- <c>GoalId</c>, <c>GoalStack</c> and
    /// <c>RankedAction</c> -- and <c>NosAi.Core.Safety</c> shadows the
    /// <c>RecoveryController</c> and <c>RecoveryState</c> that
    /// <c>Gate3ExecutionOrchestrator</c> actually composes.
    /// </para>
    /// <para>
    /// They are listed rather than removed because removal is not this check's
    /// call. <c>NosAi.Core.Planning</c> is the only HTN/GOAP code in the
    /// repository and <c>CLAUDE.md</c>'s canonical flow names that stage, so
    /// deleting it is a product decision; and its tests would go with it.
    /// Tracked as <c>docs/agents/EXECUTION_QUEUE.md</c> Q-111.
    /// </para>
    /// <para>
    /// Being here still costs nothing to correctness and everything to
    /// pretending: the list is printed by
    /// <see cref="TheShadowedNamesAreReported"/>, and a name leaves it only by
    /// the dead copy going away.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> KnownHazard =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GoalId"] = "live NosAi.Core.WorldModel, dead twin in NosAi.Core.Planning.",
            ["GoalStack"] =
                "live NosAi.Runtime.Autonomy -- the one Gate3Runtime composes -- dead twin in "
                + "NosAi.Core.Planning. ModuleReachability's own note already calls this pair out.",
            ["RankedAction"] = "live NosAi.Runtime.Tactical, dead twin in NosAi.Core.Planning.",
            ["RecoveryController"] =
                "live NosAi.Runtime.Safety, dead twin in NosAi.Core.Safety. Gate3ExecutionOrchestrator "
                + "holds the live one; the dead one is 40 lines nothing calls.",
            ["RecoveryState"] = "live NosAi.Runtime.Safety, dead twin in NosAi.Core.Safety.",
            ["ScreenPoint"] =
                "live NosAi.Runtime.Humanizer, dead twin in NosAi.Raids.Orchestration -- a module with "
                + "no caller, no test and no certification suite.",
        };

    /// <summary>Every name this check tolerates, argued or merely recorded.</summary>
    private static IEnumerable<KeyValuePair<string, string>> Tolerated => Accepted.Concat(KnownHazard);

    /// <summary>Matches a public type declaration and captures its name.</summary>
    /// <remarks>
    /// <c>record struct</c> and <c>readonly record struct</c> are matched before
    /// bare <c>record</c> so the name is the identifier and not the word
    /// <c>struct</c> -- the first draft of this scan reported a type called
    /// "struct" declared in ninety files.
    /// </remarks>
    private static readonly Regex PublicType = new(
        @"^\s*public\s+(?:(?:sealed|abstract|static|partial|readonly|unsafe)\s+)*"
        + @"(?:record\s+struct|record\s+class|class|record|struct|interface|enum)\s+(\w+)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// No live type name is silently reachable from a module nothing calls.
    /// </summary>
    [Fact]
    public void NoLiveTypeIsShadowedByAnUnreachedModule_WithoutASayingWhy()
    {
        var declaredIn = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (string file in ProductionSources())
        {
            string source = File.ReadAllText(file);
            Match ns = Regex.Match(source, @"^\s*namespace\s+([\w.]+)", RegexOptions.Multiline);
            if (!ns.Success) continue;

            foreach (Match type in PublicType.Matches(source))
            {
                if (!declaredIn.TryGetValue(type.Groups[1].Value, out HashSet<string>? namespaces))
                    declaredIn[type.Groups[1].Value] = namespaces = new HashSet<string>(StringComparer.Ordinal);
                namespaces.Add(ns.Groups[1].Value);
            }
        }

        var reach = ModuleReachability.Modules.ToDictionary(m => m.Namespace, m => m.Reach, StringComparer.Ordinal);
        var undeclared = new List<string>();

        foreach ((string type, HashSet<string> namespaces) in declaredIn.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (namespaces.Count < 2 || Accepted.ContainsKey(type) || KnownHazard.ContainsKey(type)) continue;

            string[] live = namespaces.Where(n => reach.GetValueOrDefault(n, ModuleReach.Integrated) == ModuleReach.Integrated)
                .OrderBy(n => n, StringComparer.Ordinal).ToArray();
            string[] dead = namespaces.Where(n => reach.GetValueOrDefault(n, ModuleReach.Integrated) != ModuleReach.Integrated)
                .OrderBy(n => n, StringComparer.Ordinal).ToArray();

            if (live.Length == 0 || dead.Length == 0) continue;

            undeclared.Add($"{type}: live in {string.Join(", ", live)}; also declared in {string.Join(", ", dead)}");
        }

        Assert.True(undeclared.Count == 0,
            "A live type name is also declared in a module nothing reaches. Using that namespace and "
            + "writing the name compiles and silently takes the dead copy. Either remove the dead one, "
            + "or add it to TypeNameShadowingTests.Accepted with the reason it stands:\n  "
            + string.Join("\n  ", undeclared));
    }

    /// <summary>
    /// Every tolerated entry still describes a real pair, so neither list can
    /// outlive what it excuses.
    /// </summary>
    /// <remarks>
    /// An allow-list nobody prunes is how a check stops checking. This fails when
    /// an entry no longer names a type declared in two namespaces at all -- for
    /// instance because the dead copy was finally removed, which is the outcome
    /// the list exists to make visible rather than permanent.
    /// </remarks>
    [Fact]
    public void EveryAcceptedShadowingStillExists()
    {
        var declaredIn = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (string file in ProductionSources())
        {
            string source = File.ReadAllText(file);
            Match ns = Regex.Match(source, @"^\s*namespace\s+([\w.]+)", RegexOptions.Multiline);
            if (!ns.Success) continue;

            foreach (Match type in PublicType.Matches(source))
            {
                if (!declaredIn.TryGetValue(type.Groups[1].Value, out HashSet<string>? namespaces))
                    declaredIn[type.Groups[1].Value] = namespaces = new HashSet<string>(StringComparer.Ordinal);
                namespaces.Add(ns.Groups[1].Value);
            }
        }

        string[] stale = Tolerated.Select(p => p.Key)
            .Where(t => !declaredIn.TryGetValue(t, out HashSet<string>? n) || n.Count < 2)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToArray();

        Assert.True(stale.Length == 0,
            "Accepted entries that no longer name a duplicated type. Remove them:\n  " + string.Join("\n  ", stale));
    }

    /// <summary>Reports the shadowed names, accepted ones included, for reading.</summary>
    [Fact]
    public void TheShadowedNamesAreReported()
    {
        _output.WriteLine($"{Accepted.Count} shadowed names are argued and deliberate:");
        foreach ((string type, string why) in Accepted.OrderBy(p => p.Key, StringComparer.Ordinal))
            _output.WriteLine($"  {type}: {why}");

        _output.WriteLine(string.Empty);
        _output.WriteLine($"{KnownHazard.Count} are hazards awaiting a decision (Q-111):");
        foreach ((string type, string why) in KnownHazard.OrderBy(p => p.Key, StringComparer.Ordinal))
            _output.WriteLine($"  {type}: {why}");

        Assert.NotEmpty(Accepted);
    }

    /// <summary>
    /// The same source set <see cref="ModuleReachabilityTests"/> reads: all of
    /// <c>src/</c> bar the project ADR-0025 froze.
    /// </summary>
    private static string[] ProductionSources()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        Assert.True(directory is not null, "Repository root not found: no NosAi.sln above the test assembly.");

        string src = Path.Combine(directory!.FullName, "src");
        return Directory
            .EnumerateDirectories(src)
            .Where(d => Path.GetFileName(d) != "NosAi.GuardAi.App")
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();
    }
}
