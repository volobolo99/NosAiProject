namespace NosAi.Adapter.DirectEngine;

/// <summary>
/// The whole surface of the direct engine: every capability the reference
/// implementation had over a running NosTale client, expressed once, in one place,
/// where the runtime can authorise each one separately.
/// </summary>
/// <remarks>
/// <para>
/// Named methods as well as <see cref="Execute"/>, on purpose. The generic entry
/// point is what a planner uses; the named ones are what make the inventory
/// legible — a reader comparing this against <c>CallFunction.h</c> can see that
/// nothing was quietly dropped in the move from a DLL of inline assembly to a
/// contract. They are conveniences over <see cref="Execute"/> and share every check
/// it makes.
/// </para>
/// <para>
/// <b>Nothing here injects.</b> This interface says what may be asked for and what
/// comes back; it says nothing about how a request reaches the client, which is a
/// later decision with its own review. An implementation that cannot yet carry out
/// a capability answers <see cref="EngineRefusalCode.NotImplemented"/> rather than
/// pretending, and rather than dropping the capability from the surface.
/// </para>
/// <para>
/// Synchronous by design: each of these is one call into a client that is either
/// there or is not, and wrapping an immediate refusal in a task would only make the
/// fail-closed path harder to read.
/// </para>
/// </remarks>
public interface IDirectEngineAdapter
{
    /// <summary>The addresses in force, or null when no profile has been resolved.</summary>
    EngineResolvedProfile? Profile { get; }

    /// <summary>Every capability this contract covers, whether or not it is available now.</summary>
    IReadOnlyList<EngineCapability> DeclaredCapabilities { get; }

    /// <summary>
    /// Validates and resolves a candidate profile against a loaded client module.
    /// </summary>
    /// <param name="candidate">The build description to try.</param>
    /// <param name="moduleImage">The client module's bytes, starting at its base.</param>
    /// <param name="moduleBase">Where that image is loaded in the target process.</param>
    /// <param name="processId">The process those addresses will be valid in.</param>
    /// <param name="architecture">What the attached client actually is.</param>
    /// <param name="refusal">Non-null exactly when this returns false.</param>
    /// <remarks>
    /// Authorised as <see cref="EngineCapability.ResolvePattern"/>: scanning a client
    /// is itself something an operator may permit or refuse, separately from calling
    /// what the scan finds.
    /// </remarks>
    bool TryLoadProfile(
        EngineClientProfile candidate,
        ReadOnlySpan<byte> moduleImage,
        nuint moduleBase,
        int processId,
        EngineArchitecture architecture,
        out EngineRefusal? refusal);

    /// <summary>
    /// Whether a capability can be exercised right now, and why not when it cannot.
    /// </summary>
    /// <remarks>
    /// A pure question: asking must never be a way to make something happen. It exists
    /// so a planner can decline to select an action it cannot carry out, instead of
    /// selecting one and discovering the refusal after the fact.
    /// </remarks>
    bool IsAvailable(EngineCapability capability, out EngineRefusal? refusal);

    /// <summary>Reads the character's own state from the client's structures.</summary>
    EngineStateResult ReadState();

    /// <summary>Runs one request through every check and, when they all pass, carries it out.</summary>
    EngineActionResult Execute(in EngineActionRequest request);

    /// <summary>Walks the character to a map cell (legacy <c>MoveTo</c>).</summary>
    EngineActionResult Move(int mapX, int mapY, string correlationId);

    /// <summary>Attacks an entity, with a numbered skill or 0 for the basic attack (legacy <c>AttackMonster</c>).</summary>
    EngineActionResult Attack(nuint targetHandle, short skillId, string correlationId);

    /// <summary>Closes and strikes (legacy <c>AttackRun</c>).</summary>
    EngineActionResult AttackRun(nuint targetHandle, string correlationId);

    /// <summary>Picks up a ground item (legacy <c>Collect</c>).</summary>
    EngineActionResult Collect(nuint itemHandle, string correlationId);

    /// <summary>Sits to recover (legacy <c>Rest</c>).</summary>
    EngineActionResult Rest(string correlationId);

    /// <summary>Walks the pet to a map cell (legacy <c>MovePetPartner</c>, option 0).</summary>
    EngineActionResult MovePet(int mapX, int mapY, string correlationId);

    /// <summary>Walks the partner to a map cell (legacy <c>MovePetPartner</c>, option 1).</summary>
    EngineActionResult MovePartner(int mapX, int mapY, string correlationId);

    /// <summary>Orders the pet onto an entity (legacy <c>AttackMonsterPetPartner</c>, option 0).</summary>
    EngineActionResult AttackWithPet(nuint targetHandle, string correlationId);

    /// <summary>Orders the partner onto an entity (legacy <c>AttackMonsterPetPartner</c>, option 1).</summary>
    EngineActionResult AttackWithPartner(nuint targetHandle, string correlationId);
}
