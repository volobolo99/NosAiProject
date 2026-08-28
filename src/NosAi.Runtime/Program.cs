using NosAi.Runtime;

Console.WriteLine("NosAi Runtime 1.0 Beta");
Console.WriteLine("Creator: Volodymyr Ryzhuk");
Console.WriteLine("Runtime foundation initialized. Live game execution is disabled by default.");

var runtime = new NosAiRuntime();
Console.WriteLine(runtime.Status);

namespace NosAi.Runtime;

public sealed class NosAiRuntime
{
    public string Status => "READY_FOR_BRINGUP";
}
