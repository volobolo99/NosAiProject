using System.Globalization;
using System.Text;
using NosAi.Runtime.Contracts;

namespace NosAi.LiveIntegration;

/// <summary>
/// A name read from an entity object, and why it is not a fact yet.
/// </summary>
/// <remarks>
/// <para>
/// Phase 1 of <c>docs/SPEC_ESTENSIONE_LAYOUT_MEMORIA.md</c>. A printable string
/// at the candidate chain is a <b>candidate</b>. It is never
/// <see cref="DataSourceKind.Live"/>: that jump needs a second independent
/// source to agree in a real session, and nothing in this type can grant it.
/// </para>
/// <para>
/// <see cref="HasValue"/> means a string was parsed, not that anyone may decide
/// on it. Downstream that needs a name waits for the concordance the operator
/// command shows, or it does without.
/// </para>
/// </remarks>
/// <param name="Value">The parsed ANSI string, or null when the read was refused.</param>
/// <param name="Reason">
/// Always set. Either the named refusal of the read, or
/// <see cref="NotEstablishedReason"/> when a string is sitting here as a candidate.
/// </param>
public readonly record struct EntityNameCandidate(string? Value, string Reason)
{
    /// <summary>A string was parsed. It is still UNKNOWN.</summary>
    public bool HasValue => Value is { Length: > 0 };

    /// <summary>Always unknown until a real session records concordance.</summary>
    public DataSourceKind Source => DataSourceKind.Unknown;

    /// <summary>The string read, classified UNKNOWN because nothing has established it.</summary>
    public const string NotEstablishedReason = "entity_name_not_established";

    /// <summary>This kind has no demonstrated chain yet.</summary>
    public const string ChainNotEstablishedPrefix = "entity_name_chain_not_established";

    public static EntityNameCandidate Missing(string reason) => new(null, reason);

    public static EntityNameCandidate Candidate(string value) => new(value, NotEstablishedReason);
}

/// <summary>
/// The predicate on the bytes a name chain pointed at.
/// </summary>
/// <remarks>
/// A wrong pointer still returns readable bytes. The checks here — length, a
/// terminator, and the printable set — are what stop those bytes from becoming
/// a short-looking name. They run on every parse; nothing is remembered.
/// </remarks>
public static class EntityNameText
{
    /// <summary>Largest name the client is taken to store, including the terminator.</summary>
    public const int MaxBytes = 64;

    public const string EmptyReason = "entity_name_empty";
    public const string UnterminatedReason = "entity_name_unterminated";
    public const string NotPrintablePrefix = "entity_name_not_printable";

    /// <summary>
    /// Parses one ANSI C string from <paramref name="bytes"/>.
    /// </summary>
    /// <remarks>
    /// Stops at the first <c>\0</c>. A byte outside ASCII printable (0x20–0x7E)
    /// is a wrong chain, not an accented name: widening the set waits on a real
    /// name that this predicate refused.
    /// </remarks>
    public static bool TryParseAnsi(ReadOnlySpan<byte> bytes, out string? name, out string? failureReason)
    {
        name = null;

        if (bytes.IsEmpty)
        {
            failureReason = EmptyReason;
            return false;
        }

        for (int i = 0; i < bytes.Length; i++)
        {
            byte b = bytes[i];
            if (b == 0)
            {
                if (i == 0)
                {
                    failureReason = EmptyReason;
                    return false;
                }

                name = Encoding.ASCII.GetString(bytes[..i]);
                failureReason = null;
                return true;
            }

            if (b < 0x20 || b > 0x7E)
            {
                failureReason = string.Create(CultureInfo.InvariantCulture,
                    $"{NotPrintablePrefix}:0x{b:X2}");
                return false;
            }
        }

        failureReason = UnterminatedReason;
        return false;
    }
}
