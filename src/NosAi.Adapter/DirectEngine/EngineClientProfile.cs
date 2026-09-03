namespace NosAi.Adapter.DirectEngine;

/// <summary>How far a profile has got towards being usable.</summary>
public enum EngineValidationState
{
    /// <summary>
    /// Written down but never checked. The state every profile starts in, and never
    /// a synonym for "probably fine".
    /// </summary>
    Unvalidated = 0,

    /// <summary>Structurally coherent: every declared capability has a well-formed way to be found.</summary>
    Valid = 1,

    /// <summary>Checked and rejected. <see cref="EngineProfileValidation.Problems"/> says why.</summary>
    Invalid = 2
}

/// <summary>The verdict on one profile, with every problem found rather than the first.</summary>
/// <remarks>
/// All of them, because a profile is edited by hand against a specific client
/// build and reporting one fault per round-trip turns a five-minute correction
/// into an afternoon.
/// </remarks>
public sealed record EngineProfileValidation(EngineValidationState State, IReadOnlyList<string> Problems)
{
    public static EngineProfileValidation Valid { get; } =
        new(EngineValidationState.Valid, Array.Empty<string>());

    public static EngineProfileValidation Unvalidated { get; } =
        new(EngineValidationState.Unvalidated, Array.Empty<string>());

    public static EngineProfileValidation Invalid(IReadOnlyList<string> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);
        if (problems.Count == 0)
            throw new ArgumentException("An invalid profile must say what is wrong with it.", nameof(problems));

        return new EngineProfileValidation(EngineValidationState.Invalid, problems);
    }

    public bool IsValid => State == EngineValidationState.Valid;

    public override string ToString() =>
        Problems.Count == 0 ? State.ToString() : $"{State}:{string.Join(",", Problems)}";
}

/// <summary>
/// Everything that is true of one client build and nothing that is true of the
/// running process: the version it describes, the instruction set it was derived
/// for, the signatures that locate its engine functions and the pointer paths that
/// reach its data.
/// </summary>
/// <remarks>
/// <para>
/// A profile is a <i>claim</i>, not an authority. It carries no resolved address,
/// because an address only exists relative to a module that is loaded right now,
/// and a profile that remembered one would be handing out a stale pointer the
/// first time the client restarted under ASLR. Turning a profile into addresses is
/// <see cref="IEngineProfileResolver"/>'s job, on every attach.
/// </para>
/// <para>
/// The validation state travels with the profile so a caller cannot lose track of
/// whether it was ever checked. It starts <see cref="EngineValidationState.Unvalidated"/>
/// and only a resolver may replace it, through <see cref="WithValidation"/>.
/// </para>
/// </remarks>
public sealed class EngineClientProfile
{
    private readonly Dictionary<EngineCapability, EngineSignature> _signatures;
    private readonly Dictionary<string, EnginePointerPath> _pointerPaths;
    private readonly Dictionary<EngineCapability, uint> _contextOffsets;

    /// <param name="clientVersion">Which build this describes. Free-form, but never empty.</param>
    /// <param name="architecture">The instruction set the call sequences were derived for.</param>
    /// <param name="moduleName">The client module the offsets are relative to, e.g. <c>NostaleClientX.exe</c>.</param>
    /// <param name="signatures">One per capability that is reached by scanning.</param>
    /// <param name="pointerPaths">Named walks to the client's own data.</param>
    /// <param name="contextOffsets">
    /// Per-capability module offsets of the object a call needs as its context — the
    /// reference's <c>lpv*This</c> globals.
    /// </param>
    public EngineClientProfile(
        string clientVersion,
        EngineArchitecture architecture,
        string moduleName,
        IEnumerable<EngineSignature> signatures,
        IEnumerable<EnginePointerPath>? pointerPaths = null,
        IEnumerable<KeyValuePair<EngineCapability, uint>>? contextOffsets = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        ArgumentNullException.ThrowIfNull(signatures);

        ClientVersion = clientVersion;
        Architecture = architecture;
        ModuleName = moduleName;

        _signatures = new Dictionary<EngineCapability, EngineSignature>();
        DuplicateCapabilities = CollectSignatures(signatures, _signatures);

        _pointerPaths = new Dictionary<string, EnginePointerPath>(StringComparer.Ordinal);
        DuplicatePointerPaths = CollectPointerPaths(pointerPaths, _pointerPaths);

        _contextOffsets = contextOffsets is null
            ? new Dictionary<EngineCapability, uint>()
            : new Dictionary<EngineCapability, uint>(contextOffsets);

        Validation = EngineProfileValidation.Unvalidated;
    }

    private EngineClientProfile(EngineClientProfile source, EngineProfileValidation validation)
    {
        ClientVersion = source.ClientVersion;
        Architecture = source.Architecture;
        ModuleName = source.ModuleName;
        _signatures = source._signatures;
        _pointerPaths = source._pointerPaths;
        _contextOffsets = source._contextOffsets;
        DuplicateCapabilities = source.DuplicateCapabilities;
        DuplicatePointerPaths = source.DuplicatePointerPaths;
        Validation = validation;
    }

    public string ClientVersion { get; }

    public EngineArchitecture Architecture { get; }

    public string ModuleName { get; }

    /// <summary>The verdict so far. <see cref="EngineValidationState.Unvalidated"/> until a resolver rules.</summary>
    public EngineProfileValidation Validation { get; }

    /// <summary>Capabilities this profile knows how to locate.</summary>
    public IReadOnlyCollection<EngineCapability> DeclaredCapabilities => _signatures.Keys;

    public IReadOnlyDictionary<EngineCapability, EngineSignature> Signatures => _signatures;

    public IReadOnlyDictionary<string, EnginePointerPath> PointerPaths => _pointerPaths;

    /// <summary>Module offsets of the context objects the engine calls need.</summary>
    public IReadOnlyDictionary<EngineCapability, uint> ContextOffsets => _contextOffsets;

    /// <summary>
    /// Capabilities that were declared more than once on construction.
    /// </summary>
    /// <remarks>
    /// Kept rather than thrown on, so validation can report it as a problem with the
    /// profile instead of an exception nobody sees. A dictionary silently keeping the
    /// last of two signatures for the same capability is how a profile ends up
    /// resolving to the wrong function.
    /// </remarks>
    public IReadOnlyList<EngineCapability> DuplicateCapabilities { get; }

    public IReadOnlyList<string> DuplicatePointerPaths { get; }

    /// <summary>Whether this profile claims to be able to reach a capability at all.</summary>
    public bool Declares(EngineCapability capability) =>
        capability switch
        {
            EngineCapability.ResolvePattern => _signatures.Count > 0,
            EngineCapability.ReadState => _pointerPaths.Count > 0,
            _ => _signatures.ContainsKey(capability)
        };

    /// <summary>The same profile carrying a verdict. Used by resolvers, not by callers.</summary>
    public EngineClientProfile WithValidation(EngineProfileValidation validation)
    {
        ArgumentNullException.ThrowIfNull(validation);
        return new EngineClientProfile(this, validation);
    }

    private static IReadOnlyList<EngineCapability> CollectSignatures(
        IEnumerable<EngineSignature> source, Dictionary<EngineCapability, EngineSignature> into)
    {
        List<EngineCapability>? duplicates = null;
        foreach (EngineSignature signature in source)
        {
            ArgumentNullException.ThrowIfNull(signature);
            if (!into.TryAdd(signature.Capability, signature))
                (duplicates ??= new List<EngineCapability>()).Add(signature.Capability);
        }

        return duplicates ?? (IReadOnlyList<EngineCapability>)Array.Empty<EngineCapability>();
    }

    private static IReadOnlyList<string> CollectPointerPaths(
        IEnumerable<EnginePointerPath>? source, Dictionary<string, EnginePointerPath> into)
    {
        if (source is null)
            return Array.Empty<string>();

        List<string>? duplicates = null;
        foreach (EnginePointerPath path in source)
        {
            ArgumentNullException.ThrowIfNull(path);
            if (!into.TryAdd(path.Name, path))
                (duplicates ??= new List<string>()).Add(path.Name);
        }

        return duplicates ?? (IReadOnlyList<string>)Array.Empty<string>();
    }
}
