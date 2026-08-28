namespace NosAi.Runtime.Safety;

public sealed record RuntimeSafetyPolicy(
    bool LiveInputEnabled,
    bool PacketInjectionEnabled,
    bool RequireClientHealthy,
    bool RequireGuardApproval)
{
    public static RuntimeSafetyPolicy SafeDefault { get; } = new(
        LiveInputEnabled: false,
        PacketInjectionEnabled: false,
        RequireClientHealthy: true,
        RequireGuardApproval: true);
}
