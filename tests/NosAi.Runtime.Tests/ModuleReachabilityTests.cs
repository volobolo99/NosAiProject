using System.Text;
using System.Text.RegularExpressions;
using NosAi.Runtime.Observability;
using Xunit;
using Xunit.Abstractions;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Derives module reachability from the source and holds the declaration to it.
/// </summary>
/// <remarks>
/// <para>
/// The audit of 2026-08-30 counted the modules nothing referenced and wrote the
/// number into a document. A document cannot notice when a module gets wired, or
/// when a new one is written and left unwired, so the number was stale the day
/// after it was measured.
/// </para>
/// <para>
/// These tests recompute it. <see cref="ModuleReachability"/> is the declaration;
/// the source is the evidence; a disagreement fails the build. That covers the
/// three ways the claim rots: a namespace nobody declared, a module claimed
/// Integrated that nothing reaches, and a stale Unreferenced on something since
/// wired up.
/// </para>
/// </remarks>
public sealed class ModuleReachabilityTests
{
    private readonly ITestOutputHelper _output;

    public ModuleReachabilityTests(ITestOutputHelper output) => _output = output;

    /// <summary>The file that registers every certification suite.</summary>
    /// <remarks>
    /// Named rather than inferred: it is the one referrer that does not mean the
    /// runtime uses a module, and the whole SuiteOnly distinction rests on telling
    /// it apart from a real caller.
    /// </remarks>
    private const string SuiteRegistry = "CertificationSuites.cs";

    // -- deriving the truth from the source ----------------------------------

    private static DirectoryInfo RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        Assert.True(directory is not null, "Repository root not found: no NosAi.sln above the test assembly.");
        return directory!;
    }

    /// <summary>
    /// Strips comments before matching.
    /// </summary>
    /// <remarks>
    /// A <c>&lt;see cref="..."/&gt;</c> naming a type is documentation, not use.
    /// Counting it would let a module be talked about into looking integrated —
    /// and this file's own remarks name several modules it must not count.
    /// </remarks>
    private static string WithoutComments(string source)
    {
        string withoutBlocks = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return string.Join('\n', withoutBlocks
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
    }


    /// <summary>
    /// Whether <paramref name="source"/> spells a type in <paramref name="ns"/>,
    /// as opposed to merely spelling a namespace that begins with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A plain <c>Contains(ns + ".")</c> counts <c>NosAi.Core.Planning.Goap</c> as
    /// a use of <c>NosAi.Core.Planning</c>, so every parent inherits its
    /// children's referrers. That was invisible while the register held only
    /// namespaces whose parents were reached anyway; extending it to
    /// <c>NosAi.Core</c> made it produce a wrong verdict immediately, and a
    /// self-inflicted one -- <c>ModuleReachability.cs</c> declaring the child
    /// made the parent look referenced from the register that was supposed to
    /// report it unreached.
    /// </para>
    /// <para>
    /// The distinction is exact and needs no heuristic: in <c>ns.Foo</c>, if
    /// <c>ns.Foo</c> is itself a declared namespace then the text names the
    /// child, and if it is not then <c>Foo</c> is a type in <c>ns</c>.
    /// </para>
    /// </remarks>
    private static bool NamesATypeIn(string source, string ns, IReadOnlyCollection<string> namespaces)
    {
        foreach (Match m in Regex.Matches(source, $@"{Regex.Escape(ns)}\.(\w+)"))
        {
            if (!namespaces.Contains($"{ns}.{m.Groups[1].Value}"))
                return true;
        }

        return false;
    }

    private sealed record Analysis(
        IReadOnlyDictionary<string, ModuleReach> Reach,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> Referrers);

    /// <summary>
    /// The project whose source is deliberately not scanned.
    /// </summary>
    /// <remarks>
    /// <c>NosAi.GuardAi.App</c> has no <c>.csproj</c> and is not in the solution:
    /// ADR-0025 §3 keeps its source as historical reference for the phone-side
    /// Gate 1 client and states it is not scheduled for further work. Scanning it
    /// would report every one of its namespaces as unreached, which is true and
    /// says nothing — it is unreached by decision, not by neglect, and mixing
    /// that into a debt register would drown the entries that are debt.
    /// </remarks>
    private const string FrozenProject = "NosAi.GuardAi.App";

    /// <summary>
    /// Every production source file the analysis reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All of <c>src/</c>, not one project. Scanning only
    /// <c>src/NosAi.Runtime</c> — as this analysis did until 2026-09-07 — makes
    /// every referrer outside it invisible, and an invisible referrer is
    /// reported as no referrer at all. That is the pessimistic error this file's
    /// own remarks warn about: it invites someone to delete working code.
    /// <c>NosAi.Adapter</c> is the plain example — used by
    /// <c>src/NosAi.Host/NosAiHost.cs</c> and by nothing inside the runtime — and
    /// <c>NosAi.Host</c> is the case it actually got wrong, declared SuiteOnly
    /// while its own executable's <c>Program.cs</c> reaches it.
    /// </para>
    /// <para>
    /// Tests are still not scanned, and that is the whole question: a module
    /// exercised only by its tests is not wired into anything.
    /// </para>
    /// </remarks>
    private static string[] ProductionSources()
    {
        string src = Path.Combine(RepositoryRoot().FullName, "src");
        return Directory
            .EnumerateDirectories(src)
            .Where(d => Path.GetFileName(d) != FrozenProject)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();
    }

    private static Analysis AnalyseSource()
    {
        string[] files = ProductionSources();

        var code = files.ToDictionary(f => f, f => WithoutComments(File.ReadAllText(f)));
        var namespaceOf = new Dictionary<string, string>();
        foreach (string file in files)
        {
            Match m = Regex.Match(code[file], @"^\s*namespace\s+([\w.]+)", RegexOptions.Multiline);
            if (m.Success) namespaceOf[file] = m.Groups[1].Value;
        }

        string[] namespaces = namespaceOf.Values.Distinct().OrderBy(n => n, StringComparer.Ordinal).ToArray();

        var referrers = new Dictionary<string, IReadOnlyCollection<string>>();
        foreach (string ns in namespaces)
        {
            var found = new List<string>();
            foreach (string file in files)
            {
                if (namespaceOf.GetValueOrDefault(file) == ns) continue;
                // A using directive -- plain, static, or aliased -- or the
                // namespace spelled out at a use site.
                //
                // The alias form was the blind spot, and it produced exactly the
                // error this file's own remarks call the dangerous one. Two
                // production files open with `using CoreHardware =
                // NosAi.Core.Hardware;` and one of them *implements*
                // CoreHardware.IHardwareCapabilityProvider, yet the register
                // declared NosAi.Core.Hardware unreached: the plain-using pattern
                // does not match an alias, and NamesATypeIn looks for the
                // namespace followed by a dot, while an aliased namespace is
                // followed by a semicolon. A module that looks dead and is not
                // invites someone to delete working code.
                if (Regex.IsMatch(code[file], $@"^\s*using\s+(static\s+)?{Regex.Escape(ns)}\s*;", RegexOptions.Multiline)
                    || Regex.IsMatch(code[file], $@"^\s*using\s+\w+\s*=\s*{Regex.Escape(ns)}\s*;", RegexOptions.Multiline)
                    || NamesATypeIn(code[file], ns, namespaces))
                {
                    found.Add(file);
                }
            }
            referrers[ns] = found;
        }

        var reach = new Dictionary<string, ModuleReach>();
        foreach (string ns in namespaces)
        {
            string[] names = referrers[ns].Select(Path.GetFileName).ToArray()!;
            reach[ns] = names.Length == 0
                ? ModuleReach.Unreferenced
                : names.All(n => n == SuiteRegistry) ? ModuleReach.SuiteOnly : ModuleReach.Integrated;
        }

        // Reachability is transitive: a module reached only from a module nothing
        // reaches is not reached. Without this pass, NosAiCapabilityKernel — which
        // nothing calls — would carry Economy and Navigation into Integrated.
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (string ns in namespaces)
            {
                if (reach[ns] != ModuleReach.Integrated) continue;
                bool anyLive = referrers[ns].Any(file =>
                    Path.GetFileName(file) != SuiteRegistry
                    && reach.GetValueOrDefault(namespaceOf.GetValueOrDefault(file) ?? ns, ModuleReach.Integrated)
                       != ModuleReach.Unreferenced);
                if (anyLive) continue;

                reach[ns] = referrers[ns].Any(f => Path.GetFileName(f) == SuiteRegistry)
                    ? ModuleReach.SuiteOnly
                    : ModuleReach.Unreferenced;
                changed = true;
            }
        }

        return new Analysis(reach, referrers);
    }

    // -- the checks ----------------------------------------------------------

    [Fact]
    public void Every_namespace_in_production_is_declared()
    {
        Analysis analysis = AnalyseSource();

        string[] undeclared = analysis.Reach.Keys
            .Except(ModuleReachability.Modules.Select(m => m.Namespace), StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.True(undeclared.Length == 0,
            "Namespaces present in production and absent from ModuleReachability. A new "
            + "module has to state whether anything reaches it:\n  " + string.Join("\n  ", undeclared));
    }

    [Fact]
    public void No_module_is_declared_that_no_longer_exists()
    {
        Analysis analysis = AnalyseSource();

        string[] ghosts = ModuleReachability.Modules
            .Select(m => m.Namespace)
            .Except(analysis.Reach.Keys, StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.True(ghosts.Length == 0,
            "Declared modules with no namespace in the source:\n  " + string.Join("\n  ", ghosts));
    }

    /// <summary>
    /// The check that matters: the declaration must equal what the source shows.
    /// </summary>
    /// <remarks>
    /// It fails in both directions on purpose. A module claimed Integrated that
    /// nothing reaches is the optimistic error the audit found. A stale
    /// Unreferenced on something since wired up is the pessimistic one, and it is
    /// just as bad — it invites someone to delete working code.
    /// </remarks>
    [Fact]
    public void The_declared_reach_matches_what_the_source_shows()
    {
        Analysis analysis = AnalyseSource();
        var wrong = new List<string>();

        foreach (ModuleRecord module in ModuleReachability.Modules)
        {
            if (!analysis.Reach.TryGetValue(module.Namespace, out ModuleReach actual)) continue;
            if (actual == module.Reach) continue;

            string[] names = analysis.Referrers[module.Namespace]
                .Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray()!;
            wrong.Add($"{module.Namespace}: declared {module.Reach}, source shows {actual} "
                      + $"(referrers: {(names.Length == 0 ? "none" : string.Join(", ", names))})");
        }

        Assert.True(wrong.Count == 0, "Declared reach disagrees with the source:\n  " + string.Join("\n  ", wrong));
    }

    /// <summary>
    /// An unreached module with no stated reason is indistinguishable from one
    /// nobody has looked at, which is how it stays unreached.
    /// </summary>
    [Fact]
    public void Every_unreached_module_says_why()
    {
        string[] silent = ModuleReachability.Modules
            .Where(m => m.Reach != ModuleReach.Integrated && string.IsNullOrWhiteSpace(m.Note))
            .Select(m => m.Namespace)
            .ToArray();

        Assert.True(silent.Length == 0,
            "Unreached modules with no note:\n  " + string.Join("\n  ", silent));
    }

    [Fact]
    public void No_module_is_declared_twice()
    {
        string[] duplicates = ModuleReachability.Modules
            .GroupBy(m => m.Namespace, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        Assert.Empty(duplicates);
    }

    /// <summary>
    /// Reports the share of production code no production path reaches. Not an
    /// assertion — the number is meant to be read and to move, and pinning it
    /// would only mean editing the pin.
    /// </summary>
    [Fact]
    public void The_unreached_share_of_production_is_reported()
    {
        string[] files = ProductionSources();

        var lines = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            Match m = Regex.Match(WithoutComments(text), @"^\s*namespace\s+([\w.]+)", RegexOptions.Multiline);
            if (!m.Success) continue;
            lines[m.Groups[1].Value] = lines.GetValueOrDefault(m.Groups[1].Value) + text.Split('\n').Length;
        }

        int Total(ModuleReach reach) => ModuleReachability.With(reach).Sum(m => lines.GetValueOrDefault(m.Namespace));
        int integrated = Total(ModuleReach.Integrated);
        int suiteOnly = Total(ModuleReach.SuiteOnly);
        int unreferenced = Total(ModuleReach.Unreferenced);
        int all = integrated + suiteOnly + unreferenced;

        var report = new StringBuilder();
        report.AppendLine($"Integrated:   {integrated,6} lines");
        report.AppendLine($"SuiteOnly:    {suiteOnly,6} lines");
        report.AppendLine($"Unreferenced: {unreferenced,6} lines");
        report.AppendLine($"Unreached:    {suiteOnly + unreferenced,6} lines "
                          + $"({100.0 * (suiteOnly + unreferenced) / all:F1}% of {all})");
        _output.WriteLine(report.ToString());

        Assert.True(all > 0);
    }
}
