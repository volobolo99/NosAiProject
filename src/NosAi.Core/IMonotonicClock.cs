namespace NosAi.Core;

/// <summary>
/// The one clock the critical path measures itself against. Never
/// <see cref="System.DateTime.Now"/> or <see cref="System.DateTime.UtcNow"/> on
/// that path (INV-04 determinism, docs/ROADMAP_ESECUTIVA.md S:4.3): wall-clock
/// reads are not comparable across a replay of the same journal on a different
/// day.
/// </summary>
public interface IMonotonicClock
{
    /// <summary>Ticks from a monotonic, high-resolution source (see <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/>).</summary>
    long Ticks { get; }

    /// <summary>Wall-clock milliseconds since the Unix epoch, for records and deadlines that must be human-readable and comparable across processes.</summary>
    long UnixMillis { get; }
}

/// <summary>The real clock: <see cref="System.Diagnostics.Stopwatch"/> ticks plus a Unix-epoch conversion.</summary>
public sealed class MonotonicClock : IMonotonicClock
{
    public long Ticks => System.Diagnostics.Stopwatch.GetTimestamp();

    public long UnixMillis => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

