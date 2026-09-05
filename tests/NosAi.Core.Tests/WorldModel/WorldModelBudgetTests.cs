using System.Diagnostics;
using System.Globalization;
using NosAi.Core.WorldModel;
using Xunit;
using Xunit.Abstractions;
using static NosAi.Core.Tests.WorldModel.WorldModelFixtures;

namespace NosAi.Core.Tests.WorldModel;

/// <summary>
/// AP-01 resource-budget checks (A5). These are reproducible benchmarks with
/// generous ceilings, not micro-benchmarks: the point is that a snapshot
/// revision, validation and replay digest stay far below the Gate3 cycle
/// budget on any development machine. Measured numbers are printed so the
/// handoff can quote them; the assertions only fail on an order-of-magnitude
/// regression. Run with:
/// <c>dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release --filter FullyQualifiedName~WorldModelBudgetTests --logger "console;verbosity=detailed"</c>
/// </summary>
public sealed class WorldModelBudgetTests
{
    private const int Iterations = 2_000;

    /// <summary>Per-operation ceiling. The Gate3 cycle is hundreds of milliseconds; 2 ms leaves two orders of magnitude.</summary>
    private const double MaxMicrosPerOperation = 2_000;

    private readonly ITestOutputHelper _output;

    public WorldModelBudgetTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void RevisionWithOneChangedFactStaysWithinBudget()
    {
        var snapshot = Populated();
        Warmup(() => snapshot.Advance(T2 + 1) with { Player = snapshot.Player with { Hp = Net(new Vital(1, 1000)) } });

        var micros = Measure(() =>
        {
            var next = snapshot.Advance(T2 + 1);
            next = next with { Player = next.Player with { Hp = Net(new Vital(500, 1000), at: T2 + 1) } };
            return next.Revision;
        });

        Report("revision+with", micros);
        Assert.True(micros < MaxMicrosPerOperation, Format(micros));
    }

    [Fact]
    public void ValidateStaysWithinBudget()
    {
        var snapshot = Populated();
        Warmup(() => snapshot.Validate().Length);

        var micros = Measure(() => snapshot.Validate().Length);

        Report("validate", micros);
        Assert.True(micros < MaxMicrosPerOperation, Format(micros));
    }

    [Fact]
    public void DigestStaysWithinBudget()
    {
        var snapshot = Populated();
        Warmup(() => snapshot.ComputeDigest());

        var micros = Measure(() => snapshot.ComputeDigest());

        Report("digest", micros);
        Assert.True(micros < MaxMicrosPerOperation, Format(micros));
    }

    [Fact]
    public void FactEnumerationStaysWithinBudget()
    {
        var snapshot = Populated();
        Warmup(() => snapshot.KnownFactCount);

        var micros = Measure(() => snapshot.KnownFactCount + (snapshot.IsActionable ? 1 : 0));

        Report("facts+actionable", micros);
        Assert.True(micros < MaxMicrosPerOperation, Format(micros));
    }

    [Fact]
    public void RevisionDoesNotAllocateUnboundedly()
    {
        var snapshot = Populated();
        Warmup(() => snapshot.Advance(T2 + 1).Revision);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Iterations; i++)
        {
            snapshot = snapshot.Advance(snapshot.ObservedAtUnixMillis + 1);
        }

        var perRevision = (GC.GetAllocatedBytesForCurrentThread() - before) / (double)Iterations;
        _output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"allocation per revision: {perRevision:0} B"));

        // A revision copies one record header; the immutable collections are shared.
        Assert.True(perRevision < 1_024, string.Create(CultureInfo.InvariantCulture, $"{perRevision:0} B per revision"));
    }

    private static void Warmup<T>(Func<T> op)
    {
        for (var i = 0; i < 50; i++)
        {
            _ = op();
        }
    }

    private static double Measure<T>(Func<T> op)
    {
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < Iterations; i++)
        {
            _ = op();
        }

        sw.Stop();
        return sw.Elapsed.TotalMilliseconds * 1_000 / Iterations;
    }

    private void Report(string name, double micros)
        => _output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{name}: {micros:0.0} us/op over {Iterations} iterations"));

    private static string Format(double micros)
        => string.Create(CultureInfo.InvariantCulture, $"{micros:0.0} us/op exceeds {MaxMicrosPerOperation} us/op");
}
