namespace NosAi.Adapter.DirectEngine;

/// <summary>
/// Every power the legacy direct-engine bot had over a running NosTale client,
/// named one by one.
/// </summary>
/// <remarks>
/// <para>
/// This enumeration is a transcription, not a design: each member corresponds to
/// a function the reference implementation actually exported
/// (<c>CallFunction.h</c>) or to a memory path it actually walked
/// (<c>memscan.c</c>, the tab workers). Nothing was dropped in the move to a
/// contract, because a capability that has no name here is a capability the
/// runtime can neither authorise nor refuse — and an unnamed power is the one
/// that gets exercised by accident.
/// </para>
/// <para>
/// <b>Pet and partner are four members, not two with a flag.</b> The reference
/// passed a <c>bool</c> (<c>MovePetPartner(waypoint, moveOption)</c>,
/// <c>AttackMonsterPetPartner(monster, attackOption)</c>) and the caller in
/// <c>TargetTab.cpp</c> got it wrong: the pet branch passes <c>1</c>, the partner
/// value, so a run with "attack with pet" ticked commanded the partner twice and
/// the pet never. A boolean that selects which creature acts is a safety-relevant
/// parameter, and it is being spelled out here so no caller can invert it.
/// </para>
/// </remarks>
public enum EngineCapability
{
    /// <summary>Not a capability. Present so a defaulted field is never mistaken for a power.</summary>
    None = 0,

    /// <summary>
    /// Reading the client's own structures: position, vitals, range, the monster
    /// and ground-item lists, skill cooldowns. Read-only; it commands nothing.
    /// </summary>
    ReadState = 1,

    /// <summary>
    /// Scanning the client module for the byte signatures a profile declares.
    /// Separate from the powers it unlocks: locating a function is not calling it,
    /// and an operator may well permit the first while refusing the second.
    /// </summary>
    ResolvePattern = 2,

    /// <summary>Walking the character to a map cell (legacy <c>MoveTo</c>).</summary>
    Move = 3,

    /// <summary>Attacking an entity, optionally with a numbered skill (legacy <c>AttackMonster</c>).</summary>
    Attack = 4,

    /// <summary>The melee approach-and-strike variant (legacy <c>AttackRun</c>).</summary>
    AttackRun = 5,

    /// <summary>Picking up a ground item (legacy <c>Collect</c>).</summary>
    Collect = 6,

    /// <summary>Sitting to recover (legacy <c>Rest</c>).</summary>
    Rest = 7,

    /// <summary>Walking the pet to a map cell (legacy <c>MovePetPartner</c>, option 0).</summary>
    MovePet = 8,

    /// <summary>Walking the partner to a map cell (legacy <c>MovePetPartner</c>, option 1).</summary>
    MovePartner = 9,

    /// <summary>Ordering the pet onto an entity (legacy <c>AttackMonsterPetPartner</c>, option 0).</summary>
    AttackWithPet = 10,

    /// <summary>Ordering the partner onto an entity (legacy <c>AttackMonsterPetPartner</c>, option 1).</summary>
    AttackWithPartner = 11
}

/// <summary>The instruction set the resolved addresses and call sequences belong to.</summary>
/// <remarks>
/// Recorded rather than assumed. The reference bot is x86-only and could not be
/// otherwise: its call sequences are <c>__asm</c> blocks, which MSVC does not
/// compile for x64 at all — the x64 configurations in its project file build a
/// DLL whose engine calls are simply absent. A profile that does not state the
/// architecture it was derived for invites exactly that silent emptiness.
/// </remarks>
public enum EngineArchitecture
{
    /// <summary>Not stated. Not a synonym for x86.</summary>
    Unknown = 0,

    X86 = 1,

    X64 = 2
}

/// <summary>Queries over <see cref="EngineCapability"/>.</summary>
public static class EngineCapabilities
{
    /// <summary>Every real capability, in declaration order.</summary>
    public static IReadOnlyList<EngineCapability> All { get; } = new[]
    {
        EngineCapability.ReadState,
        EngineCapability.ResolvePattern,
        EngineCapability.Move,
        EngineCapability.Attack,
        EngineCapability.AttackRun,
        EngineCapability.Collect,
        EngineCapability.Rest,
        EngineCapability.MovePet,
        EngineCapability.MovePartner,
        EngineCapability.AttackWithPet,
        EngineCapability.AttackWithPartner
    };

    /// <summary>
    /// Whether exercising this capability means calling code inside the client.
    /// </summary>
    /// <remarks>
    /// The line that matters for risk: everything below it only reads, everything
    /// above it makes the client act. <see cref="EngineCapability.ReadState"/> and
    /// <see cref="EngineCapability.ResolvePattern"/> are on the reading side.
    /// </remarks>
    public static bool Commands(EngineCapability capability) =>
        capability is not (EngineCapability.None or EngineCapability.ReadState or EngineCapability.ResolvePattern);

    /// <summary>
    /// Whether a profile must carry a located byte signature before this capability
    /// can be offered.
    /// </summary>
    /// <remarks>
    /// Reading state does not: the reference reaches its data through pointer paths
    /// off fixed module offsets, never through a scan. Scanning obviously needs no
    /// signature of its own.
    /// </remarks>
    public static bool RequiresSignature(EngineCapability capability) => Commands(capability);
}
