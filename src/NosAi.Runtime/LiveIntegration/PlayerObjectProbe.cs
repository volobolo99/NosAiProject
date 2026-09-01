using System.Diagnostics;
using System.Globalization;
using NosAi.Runtime.Security;

namespace NosAi.LiveIntegration;

/// <summary>
/// Resolves the character object in a running client and reports what it found,
/// so the pointer chain can be confirmed before anything plans on it.
/// </summary>
/// <remarks>
/// <para>
/// T-11's whole procedure, in one command. The signature and offsets come from a
/// public binding for this client and are a hypothesis until something
/// independent agrees with them — so the useful output is not the coordinate but
/// the character id, which the <i>server</i> also sent on the wire. A wrong chain
/// can produce a plausible coordinate; it will not also produce this session's
/// character id.
/// </para>
/// <para>
/// Read-only. It opens the process for reading and querying and does nothing
/// else: no injection, no write, no hook.
/// </para>
/// </remarks>
public static class PlayerObjectProbe
{
    /// <param name="processId">The client process, or 0 to find it by name.</param>
    /// <param name="expectedCharacterId">
    /// The id observed on the wire, when there is one. Supplying it turns this
    /// from a reading into a confirmation.
    /// </param>
    public static int Run(int processId, long? expectedCharacterId)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Reading process memory needs Windows.");
            return 2;
        }

        if (processId <= 0 && !TryFindClient(out processId, out string? findFailure))
        {
            Console.WriteLine($"[REFUSED] {findFailure}");
            return 1;
        }

        using Process? process = TryGetProcess(processId);
        if (process is null)
        {
            Console.WriteLine($"[REFUSED] process_not_found:{processId}");
            return 1;
        }

        using ProcessMemoryReader? reader = ProcessMemoryReader.TryOpen(
            processId, SecurityPrincipal.Operator, out string? openFailure);
        if (reader is null)
        {
            Console.WriteLine($"[REFUSED] {openFailure}");
            Console.WriteLine("  The client runs at a higher integrity level than this process.");
            Console.WriteLine("  NostaleLauncher.exe asks for 'asInvoker', so it does not need to:");
            Console.WriteLine("  restart the game from a normal (non-elevated) shell and this works");
            Console.WriteLine("  with no elevation at all. Elevating this console also works.");
            return 1;
        }

        if (!TryMainModule(process, out IntPtr moduleBase, out long moduleSize, out string? moduleFailure))
        {
            Console.WriteLine($"[REFUSED] {moduleFailure}");
            return 1;
        }

        Console.WriteLine($"Client: pid={processId} module={moduleBase.ToInt64():X} size={moduleSize} bytes");

        var clock = Stopwatch.StartNew();
        if (!NosTaleClientLayout.TryResolve(
                reader, moduleBase, moduleSize, out NosTaleClientLayout? layout, out string? resolveFailure))
        {
            Console.WriteLine($"[REFUSED] {resolveFailure}");
            Console.WriteLine("  The signature is the one NosSmooth.Local publishes for this client.");
            Console.WriteLine("  A client build it does not describe is a real answer, not a bug here.");
            return 1;
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Signature found in {clock.ElapsedMilliseconds} ms; "
            + $"manager pointer at {layout!.PlayerManagerPointerAddress.ToInt64():X}"));

        if (!layout.TryReadPlayer(reader, out PlayerObjectReading player, out string? readFailure))
        {
            Console.WriteLine($"[REFUSED] {readFailure}");
            if (readFailure is not null && readFailure.EndsWith("_null", StringComparison.Ordinal))
                Console.WriteLine("  A null here is the ordinary state at the login screen: enter the world and retry.");
            return 1;
        }

        Console.WriteLine($"  character id : {player.CharacterId}");
        Console.WriteLine($"  entity id    : {player.EntityId}");
        Console.WriteLine($"  position     : {player.X}, {player.Y}");

        if (expectedCharacterId is not { } expected)
        {
            Console.WriteLine();
            Console.WriteLine("No id to check against, so this is a reading and not a confirmation.");
            Console.WriteLine("  Capture the wire and pass the id cond reported:");
            Console.WriteLine("    --player-probe --expect-id <id>");
            return 0;
        }

        if (player.CharacterId != expected)
        {
            Console.WriteLine();
            Console.WriteLine($"[MISMATCH] the client says {player.CharacterId}, the wire said {expected}.");
            Console.WriteLine("  The chain reads memory that is not this character. The coordinate above");
            Console.WriteLine("  is a plausible number describing nothing, which is exactly what this");
            Console.WriteLine("  check exists to catch. Nothing should plan on it.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine($"[CONFIRMED] the client and the server agree on character {expected}.");
        Console.WriteLine("  Two independent sources on one number: the pointer chain is the character's.");
        Console.WriteLine("  Record this in docs/GATE1_CHECKLIST.md as the evidence T-11 asked for.");
        return 0;
    }

    private static bool TryFindClient(out int processId, out string? failureReason)
    {
        processId = 0;
        failureReason = null;

        foreach (string name in RealClientConnector.DefaultProcessNames)
        {
            foreach (Process candidate in Process.GetProcessesByName(name))
            {
                using (candidate)
                {
                    // The one drawing a window is the one holding the world; the
                    // others are helpers with no character in them.
                    if (candidate.MainWindowHandle != IntPtr.Zero)
                    {
                        processId = candidate.Id;
                        return true;
                    }

                    if (processId == 0)
                        processId = candidate.Id;
                }
            }
        }

        if (processId != 0)
            return true;

        failureReason = $"client_not_running:{string.Join('/', RealClientConnector.DefaultProcessNames)}";
        return false;
    }

    private static Process? TryGetProcess(int processId)
    {
        try
        {
            return Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool TryMainModule(
        Process process, out IntPtr moduleBase, out long moduleSize, out string? failureReason)
    {
        moduleBase = IntPtr.Zero;
        moduleSize = 0;
        failureReason = null;

        try
        {
            if (process.MainModule is not { } module)
            {
                failureReason = "client_module_not_located";
                return false;
            }

            moduleBase = module.BaseAddress;
            moduleSize = module.ModuleMemorySize;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Reading another process's module list fails the same way the memory
            // read does when it sits higher, and the reason should say so rather
            // than surfacing a Win32 code.
            failureReason = $"client_module_not_readable:{ex.GetType().Name}";
            return false;
        }
    }
}
