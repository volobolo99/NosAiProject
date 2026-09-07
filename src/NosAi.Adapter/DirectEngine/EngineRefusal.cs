using NosAi.Core;

namespace NosAi.Adapter.DirectEngine;

/// <summary>Why the direct engine would not do something.</summary>
/// <remarks>
/// An enumeration rather than a message, because a refusal that only exists as
/// prose cannot be tested, counted, or acted on differently by the caller. The
/// detail string carries the specifics; the code carries the meaning.
/// </remarks>
public enum EngineRefusalCode
{
    /// <summary>Not a refusal.</summary>
    None = 0,

    /// <summary>No client profile has been loaded, so no address is known.</summary>
    ProfileMissing = 1,

    /// <summary>A profile was supplied but did not survive validation.</summary>
    ProfileInvalid = 2,

    /// <summary>The profile is for a different instruction set than the attached client.</summary>
    ArchitectureMismatch = 3,

    /// <summary>The profile declares no entry point for this capability.</summary>
    CapabilityNotDeclared = 4,

    /// <summary>The capability is declared but its signature was not found in the client module.</summary>
    SignatureUnresolved = 5,

    /// <summary>The safety authority refused this capability for this caller.</summary>
    NotAuthorized = 6,

    /// <summary>The request itself is malformed: a missing handle, a cell off the map.</summary>
    InvalidRequest = 7,

    /// <summary>Nothing is attached to read from or act on.</summary>
    NotAttached = 8,

    /// <summary>
    /// Everything checked out and the act still did not happen, because the code
    /// that would perform it has not been built yet.
    /// </summary>
    /// <remarks>
    /// Its own code, deliberately. "We will not" and "we cannot yet" are different
    /// answers, and collapsing the second into the first would make this foundation
    /// look like a decision to drop the capability rather than a seam waiting to be
    /// filled.
    /// </remarks>
    NotImplemented = 9
}

/// <summary>A refusal with its reason attached.</summary>
/// <param name="Code">What kind of refusal this is.</param>
/// <param name="Detail">
/// The specifics, in the repository's <c>reason:subject</c> shape, e.g.
/// <c>signature_unresolved:attack_run</c>.
/// </param>
public sealed record EngineRefusal(EngineRefusalCode Code, string Detail)
{
    /// <summary>
    /// The pipeline-wide fault this refusal reports as.
    /// </summary>
    /// <remarks>
    /// <see cref="FaultCode"/> is the shared vocabulary the rest of the pipeline
    /// already speaks, and it is coarser than <see cref="EngineRefusalCode"/> on
    /// purpose: the detailed code is for the operator, this is for the stage result.
    /// Anything that is neither a scope denial nor an attach problem has no truthful
    /// member here, and reports <see cref="FaultCode.None"/> rather than borrowing
    /// one that means something else.
    /// </remarks>
    public FaultCode Fault => Code switch
    {
        EngineRefusalCode.NotAuthorized => FaultCode.ScopeDenied,
        EngineRefusalCode.NotAttached => FaultCode.AttachFailed,
        EngineRefusalCode.ProfileMissing => FaultCode.AttachFailed,
        EngineRefusalCode.ProfileInvalid => FaultCode.AttachFailed,
        EngineRefusalCode.ArchitectureMismatch => FaultCode.AttachFailed,
        EngineRefusalCode.SignatureUnresolved => FaultCode.AttachFailed,
        _ => FaultCode.None
    };

    public override string ToString() => $"{Code}:{Detail}";
}
