using System.Diagnostics;
using NosAi.Runtime.Security;

namespace NosAi.LiveIntegration;

/// <summary>
/// An open, resolved read handle on the running client: the process, the reader
/// and the located layout, held together for as long as they are used.
/// </summary>
/// <remarks>
/// <para>
/// The three pieces are useless apart — a reader without a resolved layout has
/// nothing to read, and a layout outlives neither the process it was found in nor
/// the handle it was found through — and every tool that touches the client
/// memory had grown its own copy of the same forty lines. This is that sequence
/// once, so a change to how attaching works (a different process name, a
/// different privilege, a new refusal) happens in one place.
/// </para>
/// <para>
/// <b>The layout is resolved, not remembered.</b> The signature scan runs on
/// every attach, so ASLR and client restarts stop being a source of stale
/// addresses. The pointer chain underneath is followed on every read for the same
/// reason; see <see cref="NosTaleClientLayout.TryReadPlayer"/>.
/// </para>
/// </remarks>
public sealed class ClientMemorySession : IDisposable
{
    private readonly Process _process;
    private readonly ProcessMemoryReader _reader;
    private readonly NosTaleClientLayout _layout;
    private bool _disposed;

    private ClientMemorySession(
        Process process,
        ProcessMemoryReader reader,
        NosTaleClientLayout layout,
        IntPtr moduleBase,
        long moduleSize)
    {
        _process = process;
        _reader = reader;
        _layout = layout;
        ModuleBase = moduleBase;
        ModuleSize = moduleSize;
    }

    /// <summary>The client process this session is reading.</summary>
    public int ProcessId => _process.Id;

    /// <summary>The open read handle, for callers that need it directly.</summary>
    public ProcessMemoryReader Reader => _reader;

    /// <summary>The resolved layout of the client's own structures.</summary>
    public NosTaleClientLayout Layout => _layout;

    /// <summary>Base of the client's main module, as this session found it.</summary>
    public IntPtr ModuleBase { get; }

    /// <summary>Its size in bytes.</summary>
    public long ModuleSize { get; }

    /// <summary>
    /// Finds the client, opens it for reading and resolves its layout, or says
    /// which of those three failed.
    /// </summary>
    /// <param name="processId">
    /// A specific process, or zero to take the first client with a window. Naming
    /// one matters when two clients are running and the operator means a
    /// particular character.
    /// </param>
    /// <remarks>
    /// Nothing is left open on failure: a session either exists complete or does
    /// not exist, so no caller has to reason about a half-attached one.
    /// </remarks>
    public static bool TryAttach(
        out ClientMemorySession? session,
        out string? failureReason,
        int processId = 0)
    {
        session = null;
        failureReason = null;

        Process? process = FindClient(processId);
        if (process is null)
        {
            failureReason = processId > 0 ? $"client_not_running:{processId}" : "client_not_running";
            return false;
        }

        ProcessMemoryReader? reader = null;
        try
        {
            reader = ProcessMemoryReader.TryOpen(process.Id, SecurityPrincipal.Operator, out failureReason);
            if (reader is null)
                return false;

            IntPtr moduleBase;
            long moduleSize;
            try
            {
                if (process.MainModule is not { } module)
                {
                    failureReason = "client_module_not_located";
                    return false;
                }

                moduleBase = module.BaseAddress;
                moduleSize = module.ModuleMemorySize;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // The usual cause is integrity, not a missing module: the client
                // runs elevated and a medium-integrity process cannot see inside
                // it. Naming the exception keeps that diagnosable.
                failureReason = $"client_module_not_readable:{ex.GetType().Name}";
                return false;
            }

            if (!NosTaleClientLayout.TryResolve(
                    reader, moduleBase, moduleSize, out NosTaleClientLayout? layout, out failureReason))
            {
                return false;
            }

            session = new ClientMemorySession(process, reader, layout!, moduleBase, moduleSize);
            reader = null;
            process = null;
            return true;
        }
        finally
        {
            reader?.Dispose();
            process?.Dispose();
        }
    }

    /// <summary>Follows the chain and reads the character, or says where it broke.</summary>
    public bool TryReadPlayer(out PlayerObjectReading reading, out string? failureReason)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _layout.TryReadPlayer(_reader, out reading, out failureReason);
    }

    /// <summary>
    /// The player manager and the character's map object, as this attach resolves
    /// them. They are the frame of reference an address found by scanning has to
    /// be restated in before it can outlive the process it was found in.
    /// </summary>
    public bool TryResolveBases(out IntPtr playerManager, out IntPtr playerObject, out string? failureReason)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _layout.TryResolveBases(_reader, out playerManager, out playerObject, out failureReason);
    }

    /// <summary>Reads whether a target is selected, and the candidate id behind it.</summary>
    public bool TryReadTarget(out TargetPointerReading reading, out string? failureReason)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _layout.TryReadTarget(_reader, out reading, out failureReason);
    }

    /// <summary>Reads the id of the map the character is on, or says where it broke.</summary>
    public bool TryReadMapId(out int mapId, out string? failureReason)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _layout.TryReadMapId(_reader, out mapId, out failureReason);
    }

    /// <summary>
    /// Every stats-block shape inside the player manager and player object
    /// windows. Empty is a real answer, not a failed attach.
    /// </summary>
    public bool TryScanPlayerVitals(out IReadOnlyList<PlayerVitalsHit> hits, out string? failureReason)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _layout.TryScanPlayerVitals(_reader, out hits, out failureReason);
    }

    /// <summary>
    /// The unique structural candidate, still UNKNOWN. Ambiguous or empty is a
    /// named refusal, not a guessed block.
    /// </summary>
    public bool TryReadPlayerVitals(out PlayerVitalsCandidate reading, out string? failureReason)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _layout.TryReadPlayerVitals(_reader, out reading, out failureReason);
    }

    /// <summary>Reads one of the client's entity lists for the current map.</summary>
    public bool TryReadEntities(
        MapEntityKind kind, out IReadOnlyList<MapEntityReading> entities, out string? failureReason)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return NosTaleClientLayout.TryReadEntities(
            _reader, ModuleBase, ModuleSize, kind, out entities, out failureReason);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _reader.Dispose();
        _process.Dispose();
    }

    /// <summary>
    /// The client process, by id when one was named and otherwise the first one
    /// with a window.
    /// </summary>
    /// <remarks>
    /// A window is required because the windowless match is the Delphi
    /// <c>TApplication</c> stub, which is a real process handle that renders
    /// nothing — the same trap <see cref="NosAi.Runtime.Perception.ClientWindowLocator"/> was
    /// written to avoid one level up.
    /// </remarks>
    private static Process? FindClient(int processId)
    {
        if (processId > 0)
        {
            try
            {
                Process named = Process.GetProcessById(processId);
                if (!named.HasExited)
                    return named;

                named.Dispose();
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        foreach (string name in RealClientConnector.DefaultProcessNames)
        {
            foreach (Process candidate in Process.GetProcessesByName(name))
            {
                if (candidate.MainWindowHandle != IntPtr.Zero)
                    return candidate;

                candidate.Dispose();
            }
        }

        return null;
    }
}
