using NosAi.Runtime.Gate1;
using NosAi.Runtime.Hardware;
using NosAi.Runtime.Orchestration;

namespace NosAi.Runtime;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Any(a => string.Equals(a, "--gate1-test", StringComparison.OrdinalIgnoreCase)))
            return await Gate1TestRunner.RunAllAsync().ConfigureAwait(false) ? 0 : 1;

        var runtime = RuntimeComposition.CreateSafe();
        var profileStore = new HardwareProfileStore(
            HardwareProfilePaths.PlayAiDefaultProfile(),
            new HardwareAutoSettings());
        var autoSet = new AutoSetManager(new WindowsHardwareProbe(), profileStore);
        var settings = autoSet.Initialize();

        Console.WriteLine("NosAi Runtime 1.0 Beta");
        Console.WriteLine($"Live input: {runtime.SafetyPolicy.LiveInputEnabled}");
        Console.WriteLine($"Packet injection: {runtime.SafetyPolicy.PacketInjectionEnabled}");
        Console.WriteLine($"Compute tier: {settings.ComputeTier}");
        Console.WriteLine($"Memory tier: {settings.MemoryTier}");
        Console.WriteLine($"Graphics tier: {settings.GraphicsTier}");
        Console.WriteLine($"Workers: {settings.WorkerCount}");
        Console.WriteLine("Runtime composition and hardware profile initialized in safe mode.");
        return 0;
    }
}
