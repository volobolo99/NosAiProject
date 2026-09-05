using System.Text.Json;
using NosAi.Core.Scheduling;
using Xunit;
using NosAi.Core.Hardware;

namespace NosAi.Core.Tests.Scheduling;

/// <summary>
/// AP-00 / A5 audit coverage: JSON round-trip and validate-on-deserialize
/// behaviour for the AI budget policy's data contracts
/// (docs/agents/phases/AP-00/A5_CLAUDE_tests_docs.md REQUIRE list explicitly
/// names "serialization" as a case to check). None of A3's Scheduling types
/// had a serialization test before this audit, even though every one of them
/// is a plain-data record/record-struct that a future orchestrator boundary
/// (telemetry export, cross-process job submission, a replay log) is likely
/// to serialize.
/// </summary>
public sealed class SchedulingContractSerializationTests
{
    [Fact]
    public void ResourceCost_RoundTripsThroughJson()
    {
        var original = new ResourceCost(CpuMillis: 12, GpuMillis: 34, RamBytes: 1024, VramBytes: 2048);

        string json = JsonSerializer.Serialize(original);
        ResourceCost restored = JsonSerializer.Deserialize<ResourceCost>(json);

        Assert.Equal(original, restored);
    }

    [Fact]
    public void ResourceCost_Zero_RoundTripsThroughJson()
    {
        string json = JsonSerializer.Serialize(ResourceCost.Zero);
        ResourceCost restored = JsonSerializer.Deserialize<ResourceCost>(json);

        Assert.Equal(ResourceCost.Zero, restored);
    }

    [Fact]
    public void FallbackStrategy_Reject_RoundTripsThroughJson()
    {
        FallbackStrategy original = FallbackStrategy.Reject("no cheaper tier available");

        string json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<FallbackStrategy>(json);

        Assert.NotNull(restored);
        Assert.Equal(FallbackKind.Reject, restored!.Kind);
        Assert.Equal("no cheaper tier available", restored.Reason);
        Assert.Null(restored.Degradation);
    }

    [Fact]
    public void FallbackStrategy_DegradeTo_RoundTripsThroughJson_IncludingTheNestedDegradationPlan()
    {
        var plan = new DegradationPlan(InferenceTier.Tier0DeterministicRules, new ResourceCost(1, 0, 1, 0), 5, 0.2);
        FallbackStrategy original = FallbackStrategy.DegradeTo(plan);

        string json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<FallbackStrategy>(json);

        Assert.NotNull(restored);
        Assert.Equal(FallbackKind.Degrade, restored!.Kind);
        Assert.NotNull(restored.Degradation);
        Assert.Equal(InferenceTier.Tier0DeterministicRules, restored.Degradation!.Tier);
        Assert.Equal(new ResourceCost(1, 0, 1, 0), restored.Degradation.EstimatedCost);
        Assert.Equal(5, restored.Degradation.EstimatedDurationMs);
        Assert.Equal(0.2, restored.Degradation.MinimumConfidence);
    }

    [Fact]
    public void InferenceJob_WithRejectFallback_RoundTripsThroughJson()
    {
        InferenceJob original = new(
            "detector-001", InferenceTier.Tier2GpuAcceleratedVision, JobPriority.High,
            DeadlineUnixMillis: 2_000_000, EstimatedDurationMs: 15,
            EstimatedCost: new ResourceCost(4, 8, 256, 512),
            MinimumConfidence: 0.75, Fallback: FallbackStrategy.Reject("no fallback configured"));

        string json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<InferenceJob>(json);

        Assert.NotNull(restored);
        Assert.Equal(original.Id, restored!.Id);
        Assert.Equal(original.Tier, restored.Tier);
        Assert.Equal(original.Priority, restored.Priority);
        Assert.Equal(original.DeadlineUnixMillis, restored.DeadlineUnixMillis);
        Assert.Equal(original.EstimatedDurationMs, restored.EstimatedDurationMs);
        Assert.Equal(original.EstimatedCost, restored.EstimatedCost);
        Assert.Equal(original.MinimumConfidence, restored.MinimumConfidence);
        Assert.Equal(original.Fallback.Kind, restored.Fallback.Kind);
    }

    [Fact]
    public void InferenceJob_WithDegradeFallback_RoundTripsThroughJson()
    {
        var plan = new DegradationPlan(InferenceTier.Tier1LightweightLocalMl, new ResourceCost(1, 0, 1, 0), 5, 0.1);
        InferenceJob original = new(
            "reasoning-007", InferenceTier.Tier3ExpensiveLocalReasoning, JobPriority.Critical,
            DeadlineUnixMillis: 5_000_000, EstimatedDurationMs: 200,
            EstimatedCost: new ResourceCost(10, 20, 1024, 2048),
            MinimumConfidence: 0.9, Fallback: FallbackStrategy.DegradeTo(plan));

        string json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<InferenceJob>(json);

        Assert.NotNull(restored);
        Assert.Equal(FallbackKind.Degrade, restored!.Fallback.Kind);
        Assert.Equal(InferenceTier.Tier1LightweightLocalMl, restored.Fallback.Degradation!.Tier);
        // The restored job must still be usable by TryDegrade -- a
        // deserialized job is not second-class relative to one built in
        // memory.
        bool degradedOk = restored.Fallback.TryDegrade(restored, out InferenceJob degraded);
        Assert.True(degradedOk);
        Assert.Equal(InferenceTier.Tier1LightweightLocalMl, degraded.Tier);
    }

    /// <summary>
    /// <see cref="InferenceJob"/>'s validation lives in its redeclared
    /// property initializers, which System.Text.Json's parameterized-record
    /// deserialization runs through (it calls the primary constructor with
    /// the deserialized values, the same path <c>new InferenceJob(...)</c>
    /// uses) rather than bypassing them via reflection-based field writes.
    /// This test locks that guarantee down explicitly: a JSON payload
    /// carrying an already-invalid job (negative estimated cost) must fail
    /// loudly on deserialize, never silently produce an
    /// <see cref="InferenceJob"/> whose invariants do not actually hold.
    /// </summary>
    [Fact]
    public void InferenceJob_DeserializingAStructurallyInvalidPayload_ThrowsRatherThanProducingABrokenInstance()
    {
        // Same shape System.Text.Json would produce for a valid job, but with
        // EstimatedCost.CpuMillis negative -- InferenceJob's own constructor
        // validation must reject this exactly as it would for `new InferenceJob(...)`.
        const string json = """
            {
              "Id": "bad-job",
              "Tier": 1,
              "Priority": 1,
              "DeadlineUnixMillis": 1000,
              "EstimatedDurationMs": 10,
              "EstimatedCost": { "CpuMillis": -1, "GpuMillis": 0, "RamBytes": 0, "VramBytes": 0 },
              "MinimumConfidence": 0.5,
              "Fallback": { "Kind": 0, "Degradation": null, "Reason": null }
            }
            """;

        Assert.Throws<ArgumentOutOfRangeException>(() => JsonSerializer.Deserialize<InferenceJob>(json));
    }

    [Fact]
    public void InferenceJob_DeserializingAnEmptyIdPayload_ThrowsRatherThanProducingABrokenInstance()
    {
        const string json = """
            {
              "Id": "",
              "Tier": 0,
              "Priority": 0,
              "DeadlineUnixMillis": 1000,
              "EstimatedDurationMs": 10,
              "EstimatedCost": { "CpuMillis": 0, "GpuMillis": 0, "RamBytes": 0, "VramBytes": 0 },
              "MinimumConfidence": 0.5,
              "Fallback": { "Kind": 0, "Degradation": null, "Reason": null }
            }
            """;

        Assert.Throws<ArgumentException>(() => JsonSerializer.Deserialize<InferenceJob>(json));
    }

    [Fact]
    public void AdmissionResult_RoundTripsThroughJson()
    {
        InferenceJob job = new("a", InferenceTier.Tier0DeterministicRules, JobPriority.Normal, 1_000, 1,
            new ResourceCost(0, 0, 0, 0), 0.1, FallbackStrategy.Reject());
        var original = new AdmissionResult(AdmissionOutcome.Accepted, job, RejectionReason.None, "admitted");

        string json = JsonSerializer.Serialize(original);
        AdmissionResult restored = JsonSerializer.Deserialize<AdmissionResult>(json);

        Assert.Equal(original.Outcome, restored.Outcome);
        Assert.Equal(original.Job.Id, restored.Job.Id);
        Assert.Equal(original.Reason, restored.Reason);
        Assert.Equal(original.Explanation, restored.Explanation);
    }
}
