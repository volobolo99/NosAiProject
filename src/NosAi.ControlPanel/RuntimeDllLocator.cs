using System.IO;

namespace NosAi.ControlPanel;

/// <summary>
/// Finds the runtime assembly the panel should launch, and refuses a copy that
/// cannot run.
/// </summary>
/// <remarks>
/// <para>
/// The build drops a copy of <c>NosAi.Runtime.dll</c> next to the panel, and
/// three call sites used to prefer it because it is the closest one. That copy
/// is incomplete: the panel is WPF, so it resolves
/// <c>Microsoft.WindowsDesktop.App</c>, which already carries
/// <c>System.Security.Cryptography.ProtectedData</c> — MSBuild therefore does
/// not copy that package asset into the panel's output, while the runtime's own
/// <c>runtimeconfig.json</c> asks only for <c>Microsoft.NETCore.App</c>, where
/// it is absent.
/// </para>
/// <para>
/// The failure that produces is not a missing-file error: Gate 1 builds a
/// bootstrap host, the host unwraps the DPAPI identity, and the JIT fails to
/// load the assembly at that point. Four Gate 1 checks reported FAIL and the
/// startup round showed a red light on a runtime that was in fact healthy — the
/// same DLL, byte for byte, passes from its own output folder.
/// </para>
/// </remarks>
internal static class RuntimeDllLocator
{
    /// <summary>The assembly whose absence makes a neighbouring copy unusable.</summary>
    public const string RequiredCompanion = "System.Security.Cryptography.ProtectedData.dll";

    private const string FileName = "NosAi.Runtime.dll";

    /// <summary>The runtime assembly to launch, or null when none is usable.</summary>
    public static string? Resolve(string repoRoot) => Resolve(repoRoot, out _);

    /// <summary>
    /// The runtime assembly to launch, with the reason when the answer is null or
    /// when a nearer copy was passed over.
    /// </summary>
    public static string? Resolve(string repoRoot, out string? reason) =>
        Resolve(repoRoot, AppContext.BaseDirectory, out reason);

    /// <summary>
    /// The same decision against an explicit panel directory, so a test can build
    /// both folders instead of asserting against whatever the test host happens to
    /// have copied next to itself.
    /// </summary>
    public static string? Resolve(string repoRoot, string panelDirectory, out string? reason)
    {
        string built = Path.Combine(
            repoRoot, "src", "NosAi.Runtime", "bin", "Release", "net8.0-windows", FileName);
        string nextToPanel = Path.Combine(panelDirectory, FileName);

        bool panelCopyExists = File.Exists(nextToPanel);
        bool panelCopyComplete = panelCopyExists
            && File.Exists(Path.Combine(panelDirectory, RequiredCompanion));

        if (File.Exists(built))
        {
            reason = panelCopyExists && !panelCopyComplete
                ? $"la copia accanto al pannello e' incompleta ({RequiredCompanion} assente): uso l'output del runtime"
                : null;
            return built;
        }

        if (panelCopyComplete)
        {
            reason = null;
            return nextToPanel;
        }

        reason = panelCopyExists
            ? $"solo una copia incompleta del runtime ({RequiredCompanion} assente accanto al pannello) e nessun output in src/NosAi.Runtime/bin/Release"
            : "runtime non compilato: manca src/NosAi.Runtime/bin/Release/net8.0-windows/NosAi.Runtime.dll";
        return null;
    }
}
