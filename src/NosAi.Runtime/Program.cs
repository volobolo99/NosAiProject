using NosAi.Runtime.Adapters;
using NosAi.Runtime.Humanizer;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Safety;

namespace NosAi.Runtime;

public static class Program
{
    public static void Main(string[] args)
    {
        // Composition root: runtime starts in safe mode until an explicit
        // production configuration authorizes live execution.
        var safetyPolicy = RuntimeSafetyPolicy.SafeDefault;
        var inputBackend = new Win32InputBackend();
        var humanizer = new DeterministicHumanizer(inputBackend);

        Console.WriteLine("NosAi Runtime 1.0 Beta");
        Console.WriteLine($"Live input: {safetyPolicy.LiveInputEnabled}");
        Console.WriteLine($"Packet injection: {safetyPolicy.PacketInjectionEnabled}");
        Console.WriteLine("Runtime composition initialized in safe mode.");
    }
}
