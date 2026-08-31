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

    private sealed record Analysis(
        IReadOnlyDictionary<string, ModuleReach> Reach,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> Referrers);

    private static Analysis AnalyseSource()
    {
        string runtime = Path.Combine(RepositoryRoot().FullName, "src", "NosAi.Runtime");
        var files = Directory
            .EnumerateFiles(runtime, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

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
                // A using directive, or the namespace spelled out at a use site.
                if (Regex.IsMatch(code[file], $@"^\s*using\s+(static\s+)?{Regex.Escape(ns)}\s*;", RegexOptions.Multiline)
                    || code[file].Contains(ns + ".", StringComparison.Ordinal))
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
    public void Every_namespace_in_the_runtime_is_declared()
    {
        Analysis analysis = AnalyseSource();

        string[] undeclared = analysis.Reach.Keys
            .Except(ModuleReachability.Modules.Select(m => m.Namespace), StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.True(undeclared.Length == 0,
            "Namespaces present in the runtime and absent from ModuleReachability. A new "
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
    /// Reports the share of the runtime no production path reaches. Not an
    /// assertion — the number is meant to be read and to move, and pinning it
    /// would only mean editing the pin.
    /// </summary>
    [Fact]
    public void The_unreached_share_of_the_runtime_is_reported()
    {
        string runtime = Path.Combine(RepositoryRoot().FullName, "src", "NosAi.Runtime");
        var files = Directory.EnumerateFiles(runtime, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

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
