namespace NosAi.Core.Tests.Scheduling;

/// <summary>A test-only <see cref="IMonotonicClock"/> whose time is set explicitly, so scheduling tests are deterministic instead of racing the real wall clock.</summary>
internal sealed class ManualMonotonicClock : IMonotonicClock
{
    public long Ticks { get; set; }
    public long UnixMillis { get; set; }

    public ManualMonotonicClock(long unixMillis = 1_000_000)
    {
        UnixMillis = unixMillis;
        Ticks = 0;
    }
}
