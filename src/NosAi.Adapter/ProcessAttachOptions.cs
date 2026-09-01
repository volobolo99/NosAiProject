namespace NosAi.Adapter;

/// <summary>
/// What to attach to, and how to know it is the real thing: the module the
/// operator expects the target to load, and that module's known-good SHA-256
/// hash on disk. A process merely sharing a name is not enough to trust
/// (docs/ROADMAP_ESECUTIVA.md S:2.2).
/// </summary>
/// <param name="ProcessName">The OS process name to search for (without <c>.exe</c>).</param>
/// <param name="ExpectedModule">The module file name (e.g. a DLL) the process must have loaded.</param>
/// <param name="ModuleSha256">The expected SHA-256 hash of that module's on-disk file, as an uppercase hex string.</param>
/// <param name="TimeoutMs">How long <see cref="Win32ProcessAdapter"/> may spend locating and verifying the process before giving up.</param>
public readonly record struct ProcessAttachOptions(
    string ProcessName,
    string ExpectedModule,
    string ModuleSha256,
    int TimeoutMs);
