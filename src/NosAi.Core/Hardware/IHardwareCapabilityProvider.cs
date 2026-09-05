namespace NosAi.Core.Hardware;

/// <summary>
/// Minimal seam between the hardware-capability contracts owned by this
/// folder and whoever actually knows how to obtain a
/// <see cref="HardwareCapabilitySnapshot"/> at runtime. Real implementations
/// (Windows WMI probes, live telemetry polling, thermal sensors) belong in
/// <c>NosAi.Runtime.Hardware</c> and are out of scope for this contract
/// layer; <c>NosAi.Core</c> performs no I/O and no P/Invoke.
///
/// A3's resource-budget policy and any other consumer that needs "the
/// current hardware picture" should depend on this interface rather than on
/// a concrete probe, so tests can supply a fixed/deterministic
/// <see cref="HardwareCapabilitySnapshot"/> without touching real hardware
/// or WMI.
/// </summary>
public interface IHardwareCapabilityProvider
{
    /// <summary>
    /// Returns the most recent <see cref="HardwareCapabilitySnapshot"/> known
    /// to the runtime. Implementations must never fabricate a value for a
    /// capability they cannot confirm — they return
    /// <see cref="ClassifiedValue{T}.Unknown"/> for that field (or
    /// <see cref="HardwareCapabilitySnapshot.Unknown"/> for the whole
    /// snapshot) instead. This call is expected to be cheap (returning a
    /// cached/last-known snapshot); it is not required to perform a fresh
    /// hardware probe on every invocation.
    /// </summary>
    HardwareCapabilitySnapshot GetSnapshot();
}
