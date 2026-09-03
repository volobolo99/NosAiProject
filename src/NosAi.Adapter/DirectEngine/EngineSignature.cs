namespace NosAi.Adapter.DirectEngine;

/// <summary>
/// One byte signature and its mask: how a capability's entry point is found in a
/// client build whose addresses nobody may hardcode.
/// </summary>
/// <remarks>
/// <para>
/// Mask characters follow the reference scanner (<c>memscan.c</c>): <c>'?'</c>
/// matches any byte, <c>'x'</c> requires the pattern byte. Where this is
/// deliberately stricter is length. The reference derived the pattern length from
/// <c>strlen(szMask)</c> and never compared it to the pattern array, so a mask one
/// character longer than its pattern read past the end of the array on every
/// candidate address — and one of its own signatures is exactly that shape
/// (<c>ATTACK_THIS_PATTERN</c> is seven bytes, <c>ATTACK_THIS_MASK</c> is eight
/// characters). Here the two must agree, and a signature that cannot say how long
/// it is is rejected before it is ever matched.
/// </para>
/// <para>
/// The pattern is held as a private array and handed out only as a span, so a
/// profile cannot be edited from under a resolver that has already validated it.
/// </para>
/// </remarks>
public sealed class EngineSignature
{
    private readonly byte[] _pattern;

    /// <param name="capability">The power this entry point provides.</param>
    /// <param name="name">A short label for diagnostics, e.g. <c>move</c>.</param>
    /// <param name="pattern">The bytes to match, one per mask character.</param>
    /// <param name="mask">One character per pattern byte: <c>x</c> to require it, <c>?</c> to ignore it.</param>
    public EngineSignature(EngineCapability capability, string name, byte[] pattern, string mask)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(mask);

        Capability = capability;
        Name = name;
        _pattern = (byte[])pattern.Clone();
        Mask = mask;
    }

    public EngineCapability Capability { get; }

    public string Name { get; }

    /// <summary>One character per byte of <see cref="Pattern"/>.</summary>
    public string Mask { get; }

    /// <summary>The bytes to match. Length is authoritative only once <see cref="IsWellFormed"/> holds.</summary>
    public ReadOnlySpan<byte> Pattern => _pattern;

    public int Length => _pattern.Length;

    /// <summary>
    /// Whether this signature can be matched at all, and why not when it cannot.
    /// </summary>
    /// <param name="problem">
    /// Non-null exactly when this returns false. Named rather than boolean so a
    /// profile's validation report says which signature is malformed and how.
    /// </param>
    public bool IsWellFormed(out string? problem)
    {
        if (_pattern.Length == 0)
        {
            problem = $"signature_empty:{Name}";
            return false;
        }

        if (Mask.Length != _pattern.Length)
        {
            problem = $"signature_mask_length_mismatch:{Name}:mask={Mask.Length}:pattern={_pattern.Length}";
            return false;
        }

        for (int i = 0; i < Mask.Length; i++)
        {
            if (Mask[i] is not ('x' or 'X' or '?'))
            {
                problem = $"signature_mask_character_unknown:{Name}:index={i}:char={Mask[i]}";
                return false;
            }
        }

        // A signature of nothing but wildcards matches the first address scanned,
        // which is worse than not resolving: it produces a confident wrong answer.
        bool anyFixed = false;
        foreach (char c in Mask)
        {
            if (c != '?')
            {
                anyFixed = true;
                break;
            }
        }

        if (!anyFixed)
        {
            problem = $"signature_all_wildcards:{Name}";
            return false;
        }

        problem = null;
        return true;
    }
}

/// <summary>
/// A pointer walk from a fixed module offset to a value the client maintains.
/// </summary>
/// <remarks>
/// <para>
/// The semantics are the reference's <c>ReadPointer</c>, reproduced exactly
/// because the offsets in a profile are only meaningful under the walk they were
/// derived for: the base is a <i>module offset</i>, it is dereferenced once, every
/// offset except the last is added and dereferenced, and the last offset is
/// <b>added without a dereference</b>. Applying a textbook "dereference every hop"
/// walk to these same numbers yields a readable address that means nothing —
/// precisely the failure mode that makes a wrong offset look like a working one.
/// </para>
/// <para>
/// <b>Module offsets, never absolute addresses.</b> The reference mixes the two:
/// its pointer bases are module-relative, while the <c>this</c> pointers it feeds
/// to the engine calls (<c>0x008F4904</c>, <c>0x00765EA8</c>) are absolute under
/// an assumed image base of <c>0x400000</c> — the same variables, written two
/// ways, correct only while the client loads where it prefers. Everything in a
/// profile is stated relative to the module base so that ASLR moves the client
/// without invalidating the profile.
/// </para>
/// </remarks>
public sealed class EnginePointerPath
{
    private readonly int[] _offsets;

    /// <param name="name">A short label, e.g. <c>player.position</c>.</param>
    /// <param name="moduleOffset">Offset from the client module's base to the root pointer.</param>
    /// <param name="offsets">
    /// At least one hop. All but the last are dereferenced; the last is added.
    /// </param>
    public EnginePointerPath(string name, uint moduleOffset, params int[] offsets)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(offsets);

        Name = name;
        ModuleOffset = moduleOffset;
        _offsets = (int[])offsets.Clone();
    }

    public string Name { get; }

    /// <summary>Offset from the module base to the root pointer.</summary>
    public uint ModuleOffset { get; }

    /// <summary>The hops, in order. All but the last are dereferenced.</summary>
    public IReadOnlyList<int> Offsets => _offsets;

    /// <summary>Whether this path can be walked, and why not when it cannot.</summary>
    public bool IsWellFormed(out string? problem)
    {
        if (ModuleOffset == 0)
        {
            problem = $"pointer_path_zero_base:{Name}";
            return false;
        }

        if (_offsets.Length == 0)
        {
            // The reference's walk reads offsets.back() unconditionally; an empty
            // list is undefined behaviour there and a refusal here.
            problem = $"pointer_path_no_offsets:{Name}";
            return false;
        }

        problem = null;
        return true;
    }
}
