using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using NosAi.Analyzers;
using Xunit;

namespace NosAi.Core.Tests;

/// <summary>
/// Drives <see cref="NoMockOnCriticalPathAnalyzer"/> (INV-04) against real,
/// freshly compiled source through the actual Roslyn analyzer pipeline
/// (<see cref="CompilationWithAnalyzers"/>) rather than asserting on the
/// analyzer's private helpers: this is what actually runs during
/// `dotnet build` for every critical-path project, so a passing test here is
/// evidence the rule fires in practice, not just in isolation.
/// </summary>
[Trait("Category", "Gate1")]
public sealed class NoMockOnCriticalPathAnalyzerTests
{
    [Theory]
    [InlineData("Mock")]
    [InlineData("Fake")]
    [InlineData("Stub")]
    [InlineData("Dummy")]
    [InlineData("Synthetic")]
    public async Task ADeclaredTypeNamedAfterATestDoubleIsFlagged(string bannedWord)
    {
        string source = $$"""
            namespace CriticalPathSample;

            public sealed class Heartbeat{{bannedWord}}Sensor
            {
                public int Read() => 0;
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAnalyzerAsync(source);

        Diagnostic hit = Assert.Single(diagnostics, d => d.Id == NoMockOnCriticalPathAnalyzer.DiagnosticId);
        Assert.Equal(DiagnosticSeverity.Error, hit.Severity);
        Assert.Contains(bannedWord, hit.GetMessage(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReferencingATypeDefinedInAnotherAssemblyIsFlaggedJustLikeALocalDeclaration()
    {
        // INV-04 says "referenced", not "declared": a critical-path assembly
        // that merely consumes someone else's NetworkStub is exactly as much
        // of a violation as declaring the type itself would be.
        MetadataReference externalLibrary = CompileToReference("""
            namespace ExternalTestDoubles;

            public sealed class NetworkStub
            {
                public int Ping() => 1;
            }
            """);

        string source = """
            using ExternalTestDoubles;

            namespace CriticalPathSample;

            public sealed class Consumer
            {
                public int UsePing() => new NetworkStub().Ping();
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAnalyzerAsync(source, externalLibrary);

        Diagnostic hit = Assert.Single(diagnostics, d => d.Id == NoMockOnCriticalPathAnalyzer.DiagnosticId);
        Assert.Contains("NetworkStub", hit.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStringLiteralThatHappensToContainABannedWordIsNotFlagged()
    {
        string source = """
            namespace CriticalPathSample;

            public sealed class HeartbeatSensor
            {
                public string Describe() => "stub reading pending";
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAnalyzerAsync(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == NoMockOnCriticalPathAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task OrdinaryProductionCodeWithNoBannedNamesProducesNoDiagnostic()
    {
        string source = """
            namespace CriticalPathSample;

            public sealed class HeartbeatSensor
            {
                private readonly System.Collections.Generic.List<int> _samples = new();

                public void Record(int value) => _samples.Add(value);

                public int Total()
                {
                    int total = 0;
                    foreach (int sample in _samples)
                        total += sample;
                    return total;
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAnalyzerAsync(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == NoMockOnCriticalPathAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task ALocalVariableMerelyNamedAfterABannedWordIsNotFlagged()
    {
        // INV-04 targets types, not identifiers in general: a variable called
        // "mockData" that holds a perfectly real int is not a test double.
        string source = """
            namespace CriticalPathSample;

            public sealed class HeartbeatSensor
            {
                public int Read()
                {
                    int mockData = 42;
                    return mockData;
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAnalyzerAsync(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == NoMockOnCriticalPathAnalyzer.DiagnosticId);
    }

    private static async Task<ImmutableArray<Diagnostic>> RunAnalyzerAsync(string source, params MetadataReference[] extraReferences)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source);
        ImmutableArray<MetadataReference> references = PlatformReferences.Value.AddRange(extraReferences);

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "NosAi.Analyzers.TestSubject",
            syntaxTrees: new[] { tree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        AssertNoCompileErrors(compilation);

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new NoMockOnCriticalPathAnalyzer());
        CompilationWithAnalyzers withAnalyzers = compilation.WithAnalyzers(analyzers);
        return await withAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static MetadataReference CompileToReference(string source)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source);
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: $"NosAi.Analyzers.TestDependency.{Guid.NewGuid():N}",
            syntaxTrees: new[] { tree },
            references: PlatformReferences.Value,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        AssertNoCompileErrors(compilation);

        using var stream = new MemoryStream();
        EmitResult result = compilation.Emit(stream);
        Assert.True(result.Success, "Failed to emit the in-memory test dependency assembly: " + string.Join("; ", result.Diagnostics));
        stream.Position = 0;
        return MetadataReference.CreateFromStream(stream);
    }

    private static void AssertNoCompileErrors(CSharpCompilation compilation)
    {
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        Assert.True(errors.IsEmpty, "Test source failed to compile: " + string.Join("; ", errors));
    }

    private static readonly Lazy<ImmutableArray<MetadataReference>> PlatformReferences = new(() =>
    {
        string? trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrEmpty(trustedAssemblies))
            throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES was not provided by the runtime; cannot build a reference set for the in-memory compilation.");

        return trustedAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
    });
}
