namespace NosAi.Runtime.Configuration;

public sealed class RuntimeOptions
{
    public const string SectionName = "NosAi";

    public bool LiveInputEnabled { get; init; }
    public bool PacketInjectionEnabled { get; init; }
    public int OperationTimeoutMs { get; init; } = 5000;

    public void Validate()
    {
        if (OperationTimeoutMs is < 100 or > 120_000)
            throw new InvalidOperationException("OperationTimeoutMs must be between 100 and 120000 milliseconds.");

        if (PacketInjectionEnabled && !LiveInputEnabled)
            throw new InvalidOperationException("Packet injection cannot be enabled while live input is disabled.");
    }
}
