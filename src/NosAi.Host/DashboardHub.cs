using NosAi.Core;

namespace NosAi.Host;

/// <summary>One telemetry sample surfaced to the operator dashboard.</summary>
public readonly record struct TelemetryFrame(long UnixMillis, PipelineStage Stage, string Status, FaultCode Fault);

/// <summary>
/// The live telemetry sink (docs/ROADMAP_ESECUTIVA.md S:2.2). Deliberately
/// simple for Gate 1: an in-memory ring of recent frames plus a subscription
/// event, enough for a dashboard client or a test to observe what the host
/// just did. Gate 8's <c>ITelemetrySink</c> (percentile histograms, EventSource)
/// supersedes this once that Gate is reached; this is not it.
/// </summary>
public sealed class DashboardHub
{
    private readonly object _lock = new();
    private readonly List<TelemetryFrame> _frames = [];

    public event Action<TelemetryFrame>? FramePublished;

    public int AcceptedFrameCount { get; private set; }

    public int CompletedSessionCount { get; private set; }

    public bool PeerConnected { get; private set; }

    /// <param name="countAsAcceptedFrame">
    /// Application frames that passed codec, sequence and opcode checks.
    /// Handshake and attach telemetry do not count: T-07's "conteggio frame
    /// crescente" is the application stream, not every dashboard line.
    /// </param>
    public void Publish(in TelemetryFrame frame, bool countAsAcceptedFrame = false)
    {
        lock (_lock)
        {
            _frames.Add(frame);
            if (countAsAcceptedFrame)
                AcceptedFrameCount++;
            if (frame.Status == "transport")
                PeerConnected = true;
            else if (frame.Status == "disconnected")
            {
                PeerConnected = false;
                CompletedSessionCount++;
            }
        }

        FramePublished?.Invoke(frame);
    }

    public IReadOnlyList<TelemetryFrame> Snapshot()
    {
        lock (_lock)
        {
            return _frames.ToArray();
        }
    }
}
