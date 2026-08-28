namespace NosAi.Runtime.Hardware;

public sealed class AutoSetManager
{
    private readonly IHardwareProbe _probe;
    private readonly HardwareProfileStore _store;

    public AutoSetManager(IHardwareProbe probe, HardwareProfileStore store)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>Detects hardware and loads the existing profile or performs first-run Auto-Setting.</summary>
    public RuntimeSettings Initialize()
    {
        var hardware = _probe.Detect();
        return _store.LoadOrCreate(hardware);
    }
}
