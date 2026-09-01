using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NosAi.Analyzers;

/// <summary>
/// INV-04 (docs/ROADMAP_ESECUTIVA.md S:1.2, S:2.4): no mock, stub or
/// synthetic data is allowed on the deterministic critical path
/// (Observe -&gt; WorldState -&gt; ... -&gt; Verify). This analyzer runs inside the
/// assemblies that implement that path and reports every type whose name
/// looks like a test double or a placeholder, whether it is declared
/// locally or merely referenced from elsewhere.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoMockOnCriticalPathAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NOSAI0001";

    // Ordered longest-first purely so a name matching two banned words (rare)
    // reports the more specific one; match order has no correctness impact.
    private static readonly string[] BannedSubstrings = { "Synthetic", "Dummy", "Stub", "Fake", "Mock" };

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Mock, stub or synthetic type on the critical path",
        messageFormat: "Type '{0}' looks like a test double or synthetic placeholder (matches '{1}') and must not be declared or referenced from a critical-path assembly (INV-04, docs/ROADMAP_ESECUTIVA.md S:1.2)",
        category: "NosAi.Safety",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "INV-04: the deterministic critical path (Observe -> WorldState -> Simulation -> Ranking -> Orchestrator -> Planner -> Guard -> Trust -> Safety -> Execute -> Verify) never sees mock, stub or synthetic data. A type named after one of those concepts has no legitimate reason to be reachable from these assemblies.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        // A declaration with no usage anywhere in this compilation (a mock
        // class nobody has wired in yet) never surfaces as an IdentifierName,
        // so the declaration itself needs its own, independent check.
        context.RegisterSymbolAction(AnalyzeDeclaredType, SymbolKind.NamedType);
        context.RegisterSyntaxNodeAction(AnalyzeReference, SyntaxKind.IdentifierName, SyntaxKind.GenericName);
    }

    private static void AnalyzeDeclaredType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        string? matched = FindBannedSubstring(type.Name);
        if (matched is null)
            return;

        foreach (Location location in type.Locations)
        {
            if (!location.IsInSource)
                continue;

            context.ReportDiagnostic(Diagnostic.Create(Rule, location, type.Name, matched));
        }
    }

    private static void AnalyzeReference(SyntaxNodeAnalysisContext context)
    {
        var name = (SimpleNameSyntax)context.Node;
        string identifierText = name.Identifier.ValueText;

        string? matched = FindBannedSubstring(identifierText);
        if (matched is null)
            return;

        // A qualified or member-access chain (Foo.Bar.MockThing) repeats every
        // segment as its own IdentifierNameSyntax; only the rightmost segment
        // is the type name itself; earlier segments are namespaces/receivers
        // and are filtered out below by not resolving to a type symbol anyway,
        // but skipping them here avoids asking the semantic model twice.
        if (name.Parent is QualifiedNameSyntax { Right: var right } && !ReferenceEquals(right, name))
            return;
        if (name.Parent is MemberAccessExpressionSyntax { Name: var member } && !ReferenceEquals(member, name))
            return;

        ISymbol? symbol = ResolveTypeLikeSymbol(context.SemanticModel, name, context.CancellationToken);
        if (symbol is null)
            return;

        string typeName = symbol is IMethodSymbol ctor ? ctor.ContainingType.Name : symbol.Name;
        string? confirmedMatch = FindBannedSubstring(typeName);
        if (confirmedMatch is null)
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, name.GetLocation(), typeName, confirmedMatch));
    }

    private static ISymbol? ResolveTypeLikeSymbol(SemanticModel semanticModel, SimpleNameSyntax name, System.Threading.CancellationToken ct)
    {
        SymbolInfo info = semanticModel.GetSymbolInfo(name, ct);
        ISymbol? symbol = info.Symbol;

        return symbol switch
        {
            ITypeSymbol => symbol,
            IMethodSymbol { MethodKind: MethodKind.Constructor } => symbol,
            _ => null,
        };
    }

    private static string? FindBannedSubstring(string identifier)
    {
        foreach (string banned in BannedSubstrings)
        {
            if (identifier.IndexOf(banned, StringComparison.OrdinalIgnoreCase) >= 0)
                return banned;
        }

        return null;
    }
}
