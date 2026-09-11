using System;
using System.IO;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.Observability;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Copre il canale 1 di ADR-0030 (C# -> Python, in uscita, solo append):
/// DecisionTelemetryWriter registra ogni Gate3LoopCycle su un file JSONL.
/// </summary>
public sealed class DecisionTelemetryWriterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "nosai-decision-telemetry-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static Gate3LoopCycle SampleCycle(CycleOutcome outcome = CycleOutcome.Confirmed, ActionType action = ActionType.UseSkill) => new(
        AtUtc: new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc),
        Outcome: outcome,
        Summary: "esito di prova",
        SelectedAction: action,
        Hp: ClassifiedValue<int>.Derived(80),
        MaxHp: ClassifiedValue<int>.Derived(100),
        Mp: ClassifiedValue<int>.Derived(40),
        HasTarget: ClassifiedValue<bool>.Derived(true),
        ObservationAge: TimeSpan.FromMilliseconds(50),
        WouldHaveActed: true);

    [Fact]
    public void Append_CreatesTheDirectory_AndWritesOneJsonLine()
    {
        string path = Path.Combine(_directory, "nested", "decisions.jsonl");
        var writer = new DecisionTelemetryWriter(path, new NullRuntimeLogger());

        writer.Append(SampleCycle());

        Assert.True(File.Exists(path));
        string[] lines = File.ReadAllLines(path);
        string line = Assert.Single(lines);
        Assert.Contains("\"outcome\":\"Confirmed\"", line, StringComparison.Ordinal);
        Assert.Contains("\"selected_action\":\"UseSkill\"", line, StringComparison.Ordinal);
        Assert.Contains("\"would_have_acted\":true", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Append_TwiceOnTheSamePath_AppendsRatherThanOverwriting()
    {
        string path = Path.Combine(_directory, "decisions.jsonl");
        var writer = new DecisionTelemetryWriter(path, new NullRuntimeLogger());

        writer.Append(SampleCycle(CycleOutcome.Confirmed));
        writer.Append(SampleCycle(CycleOutcome.NoCandidate));

        string[] lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        Assert.Contains("Confirmed", lines[0]);
        Assert.Contains("NoCandidate", lines[1]);
    }

    [Fact]
    public void Append_WhenThePathIsUnwritable_NeverThrows()
    {
        // La directory stessa al posto del file: File.AppendAllText deve fallire, ma Append lo assorbe.
        string path = _directory;
        Directory.CreateDirectory(_directory);
        var writer = new DecisionTelemetryWriter(path, new NullRuntimeLogger());

        var exception = Record.Exception(() => writer.Append(SampleCycle()));

        Assert.Null(exception);
    }

    [Fact]
    public void Constructor_RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new DecisionTelemetryWriter(null!, new NullRuntimeLogger()));
        Assert.Throws<ArgumentNullException>(() => new DecisionTelemetryWriter(Path.Combine(_directory, "x.jsonl"), null!));
    }
}
