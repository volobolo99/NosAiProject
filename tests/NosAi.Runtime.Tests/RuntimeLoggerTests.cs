using NosAi.Runtime.Configuration;
using NosAi.Runtime.Gate1;
using NosAi.Runtime.Observability;
using Xunit;

namespace NosAi.Runtime.Tests;

// M012 (structured logging and correlation identifiers): CorrelationScope and the
// id ConsoleRuntimeLogger now stamps on every line. No mocking framework anywhere
// in this file -- CorrelationScopeTests exercises the real AsyncLocal-backed
// implementation, ConsoleRuntimeLoggerCorrelationTests exercises the real logger
// writing to a really (temporarily) redirected Console.Out, and
// Gate1BootstrapHostCorrelationTests drives a real Gate1BootstrapHost with a
// small, honest recorder that implements IRuntimeLogger for real -- the same way
// UiLogger does in production -- rather than verifying call expectations against a fake.

/// <summary>The ambient correlation id scope itself.</summary>
public sealed class CorrelationScopeTests
{
    [Fact]
    public void OutsideAnyScopeCurrentIsNull()
    {
        Assert.Null(CorrelationScope.Current);
    }

    [Fact]
    public void BeginMakesTheIdCurrentUntilDisposed()
    {
        Assert.Null(CorrelationScope.Current);
        using (CorrelationScope.Begin("abc123"))
        {
            Assert.Equal("abc123", CorrelationScope.Current);
        }
        Assert.Null(CorrelationScope.Current);
    }

    [Fact]
    public void NestedScopesRestoreTheOuterIdRatherThanNull()
    {
        using (CorrelationScope.Begin("outer"))
        {
            using (CorrelationScope.Begin("inner"))
            {
                Assert.Equal("inner", CorrelationScope.Current);
            }
            Assert.Equal("outer", CorrelationScope.Current);
        }
        Assert.Null(CorrelationScope.Current);
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        var scope = CorrelationScope.Begin("x");
        scope.Dispose();
        // A second Dispose must not restore an even earlier value a second time --
        // there isn't one, and this must not throw either.
        scope.Dispose();
        Assert.Null(CorrelationScope.Current);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BeginRejectsAnEmptyId(string? id)
    {
        Assert.Throws<ArgumentException>(() => CorrelationScope.Begin(id!));
    }

    [Fact]
    public async Task TheIdFlowsIntoATaskForkedWhileTheScopeIsOpen()
    {
        // This is the exact property Gate1BootstrapHost depends on: its accept
        // loop, watchdog and discovery responder are all forked with a bare
        // `_ = SomeLoopAsync(token)` while the scope opened in its constructor is
        // still current, and every one of them must see the same id.
        using (CorrelationScope.Begin("forked-scope"))
        {
            string? seenInForkedTask = await Task.Run(() => CorrelationScope.Current);
            Assert.Equal("forked-scope", seenInForkedTask);
        }
    }

    [Fact]
    public async Task TwoConcurrentScopesDoNotLeakIntoEachOther()
    {
        // AsyncLocal is the reason this holds: two independent logical flows each
        // get their own copy of the ambient value, unlike a plain static field.
        static async Task<string?> RunWithScope(string id, int delayMs)
        {
            using (CorrelationScope.Begin(id))
            {
                await Task.Delay(delayMs);
                return CorrelationScope.Current;
            }
        }

        var results = await Task.WhenAll(RunWithScope("run-a", 30), RunWithScope("run-b", 5));
        Assert.Equal("run-a", results[0]);
        Assert.Equal("run-b", results[1]);
    }
}

/// <summary>What <see cref="ConsoleRuntimeLogger"/> actually prints, with and without a scope.</summary>
public sealed class ConsoleRuntimeLoggerCorrelationTests
{
    [Fact]
    public void OutsideAnyScopeTheLoggerPrintsNone()
    {
        string output = CaptureConsole(() => new ConsoleRuntimeLogger().Info("hello"));
        Assert.Contains("correlationId=none", output);
    }

    [Fact]
    public void InsideAScopeTheLoggerPrintsTheRealIdInsteadOfNone()
    {
        string output;
        using (CorrelationScope.Begin("test-run-42"))
        {
            output = CaptureConsole(() => new ConsoleRuntimeLogger().Info("hello"));
        }
        Assert.Contains("correlationId=test-run-42", output);
        Assert.DoesNotContain("correlationId=none", output);
    }

    [Fact]
    public void ErrorLinesCarryTheScopeIdToo()
    {
        string output;
        using (CorrelationScope.Begin("err-scope"))
        {
            output = CaptureConsole(() => new ConsoleRuntimeLogger().Error("boom", new InvalidOperationException("x")));
        }
        Assert.Contains("correlationId=err-scope", output);
        Assert.Contains("InvalidOperationException", output);
    }

    private static string CaptureConsole(Action action)
    {
        var writer = new StringWriter();
        var previous = Console.Out;
        Console.SetOut(writer);
        try { action(); }
        finally { Console.SetOut(previous); }
        return writer.ToString();
    }
}

/// <summary>End-to-end: a real host's entire log output, correlated.</summary>
public sealed class Gate1BootstrapHostCorrelationTests
{
    /// <summary>
    /// Records every call for real, including the correlation id ambient at the
    /// moment of the call -- not by re-implementing ConsoleRuntimeLogger's string
    /// format, but by reading the same <see cref="CorrelationScope.Current"/> it
    /// reads. Deliberately not a Console-redirecting test: this host also starts
    /// background loops (accept loop, watchdog, discovery), and stealing the
    /// process-wide <see cref="Console.Out"/> while they run would race against
    /// any other test doing the same thing concurrently.
    /// </summary>
    private sealed class RecordingLogger : IRuntimeLogger
    {
        private readonly object _sync = new();
        public List<(string Level, string Message, string? CorrelationId)> Entries { get; } = new();

        public void Info(string message, IReadOnlyDictionary<string, object?>? properties = null) => Record("INFO", message);
        public void Warning(string message, IReadOnlyDictionary<string, object?>? properties = null) => Record("WARN", message);
        public void Error(string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? properties = null) => Record("ERROR", message);

        private void Record(string level, string message)
        {
            lock (_sync)
                Entries.Add((level, message, CorrelationScope.Current));
        }
    }

    private static Gate1HostOptions AbsentClientOptions() => new()
    {
        GuardPort = 0,
        DashboardPort = 0,
        StartDashboard = false,
        EnableDiscovery = false,
        ClientProcessName = $"nosai-absent-client-{Guid.NewGuid():N}"
    };

    [Fact]
    public async Task EveryLineABootstrapHostEmitsCarriesItsOwnCorrelationId()
    {
        var logger = new RecordingLogger();
        string correlationId;

        await using (var host = new Gate1BootstrapHost(AbsentClientOptions(), logger))
        {
            await host.StartAsync();
            correlationId = host.Capture().CorrelationId;
        }

        Assert.NotEmpty(logger.Entries);
        Assert.All(logger.Entries, entry => Assert.Equal(correlationId, entry.CorrelationId));
        // Guards against the regression this test exists for: every entry actually
        // had a real id, not every entry sharing a coincidental null.
        Assert.All(logger.Entries, entry => Assert.False(string.IsNullOrWhiteSpace(entry.CorrelationId)));
    }

    [Fact]
    public async Task TwoSequentialHostsGetDifferentCorrelationIdsAndNeitherSeesTheOthers()
    {
        // Deliberately not asserting that CorrelationScope.Current pops back to
        // null once DisposeAsync returns here: DisposeAsync suspends on real
        // awaits (closing sockets, cancelling loops), and a callee's AsyncLocal
        // mutation made after its own suspension point is local to its own
        // continuation -- it does not flow back to this method's continuation,
        // the same way a plain local variable write inside a called method
        // never flows back to its caller. That is standard ExecutionContext
        // behaviour, not something Gate1BootstrapHost needs to fight: the two
        // things that matter in production -- each host's own logs, for its own
        // lifetime, carrying its own id, and two hosts never sharing one -- are
        // exactly what this test (and EveryLineABootstrapHostEmitsCarriesItsOwnCorrelationId
        // above) verify.
        var firstLogger = new RecordingLogger();
        string firstId;
        await using (var first = new Gate1BootstrapHost(AbsentClientOptions(), firstLogger))
        {
            await first.StartAsync();
            firstId = first.Capture().CorrelationId;
        }

        var secondLogger = new RecordingLogger();
        string secondId;
        await using (var second = new Gate1BootstrapHost(AbsentClientOptions(), secondLogger))
        {
            await second.StartAsync();
            secondId = second.Capture().CorrelationId;
        }

        Assert.NotEqual(firstId, secondId);
        Assert.All(firstLogger.Entries, entry => Assert.Equal(firstId, entry.CorrelationId));
        Assert.All(secondLogger.Entries, entry => Assert.Equal(secondId, entry.CorrelationId));
        // Neither host's logger ever recorded the other host's id.
        Assert.DoesNotContain(firstLogger.Entries, entry => entry.CorrelationId == secondId);
        Assert.DoesNotContain(secondLogger.Entries, entry => entry.CorrelationId == firstId);
    }
}
