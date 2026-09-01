using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Navigation;

/// <summary>One extracted grid file and the hash of its exact bytes.</summary>
/// <param name="MapId">The map the file describes.</param>
/// <param name="Sha256">Lowercase hex of SHA-256 over the file as written.</param>
/// <remarks>
/// The hash is over the <i>file</i>, not over the parsed grid. A parser that changes
/// how it reads a byte must not be able to leave the identity unmoved, and a file
/// that changes in a byte the current parser ignores must still count as changed.
/// </remarks>
public readonly record struct MapGridFile(int MapId, string Sha256);

/// <summary>
/// The identity of the whole set of extracted map grids, and the rule that makes a
/// client patch invalidate them without any code that watches for patches.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why identity rather than a version number.</b> The grids are
/// <see cref="DataSourceKind.Cached"/> with provenance "client file": true for as
/// long as the build they came out of is the build that is running, and silently
/// false the moment it is not. A wrong grid is worse than a missing one — it walks
/// the character into a wall that moved, and does it confidently — so the invariant
/// cannot be "remember to re-extract after a patch". It has to be a fact the runtime
/// checks and cannot forget.
/// </para>
/// <para>
/// <b>How it invalidates.</b> The set hash is folded into the client build identity
/// alongside the fingerprint of the client itself. A patch changes the client
/// fingerprint, so the recorded identity no longer matches the computed one; the
/// grids are then <b>not loaded</b>. Not reloaded, not used with a warning: absent.
/// Every <see cref="MapGrid"/> is <c>default</c>, every cell answers blocked,
/// <see cref="StaticGeometryLayer.Compose"/> returns
/// <see cref="Pathfinding.TileType.Unobserved"/>, and planning produces nothing —
/// which is the behaviour <c>docs/CONTROLLO_PERSONAGGIO_ATTUAZIONE.md</c> § 3 asks
/// for and the reason those two types fail closed on a default instance.
/// </para>
/// <para>
/// It cuts the other way too, which is the half that is easy to leave out: editing
/// or truncating a <c>.grid</c> file without touching the client changes the set
/// hash and invalidates just as hard. The check is not "is the client the one we
/// extracted from" but "are these the grids we extracted, from that client".
/// </para>
/// </remarks>
public sealed record MapGridSetIdentity
{
    /// <summary>
    /// The layout this identity was computed under.
    /// </summary>
    /// <remarks>
    /// Part of the hash so that a change in how the set is folded — a new field, a
    /// different order — cannot leave two differently-computed identities comparing
    /// equal. The same reason <c>ScreenProjectionCalibration</c> carries a version:
    /// a stale artefact must be refused, never reinterpreted.
    /// </remarks>
    public const int FormatVersion = 1;

    /// <summary>The wire form of an identity nobody has computed.</summary>
    public const string UnknownHash = "";

    // Separators that cannot occur inside a hex hash or a decimal id, so that
    // ("1", "ab") and ("1a", "b") cannot fold to the same bytes. The same
    // precaution GameReferenceDatabase takes, and for the same reason: a hash whose
    // inputs are ambiguously joined hides a real change behind an apparent no-op.
    private const char FieldSeparator = '';
    private const char RecordSeparator = '';

    private MapGridSetIdentity(string setHash, ImmutableArray<MapGridFile> files, string clientFingerprint)
    {
        SetHash = setHash;
        Files = files;
        ClientFingerprint = clientFingerprint;
    }

    /// <summary>Lowercase hex SHA-256 over the whole set.</summary>
    public string SetHash { get; }

    /// <summary>The files that went into it, ordered by map id.</summary>
    public ImmutableArray<MapGridFile> Files { get; }

    /// <summary>
    /// Whatever identifies the client build the grids were extracted from.
    /// </summary>
    /// <remarks>
    /// Opaque here on purpose. What fingerprints a client build — the hash of its
    /// executable, its version resource, the hash of the archives — is the
    /// extractor's business, and pinning it in this type would make a change of
    /// method a change to the navigation contract.
    /// </remarks>
    public string ClientFingerprint { get; }

    /// <summary>The grids are always cached client data, and never live.</summary>
    public static DataSourceKind Classification => DataSourceKind.Cached;

    /// <summary>Where the grids came from, for anything that reports provenance.</summary>
    public static string Provenance => "client-file";

    /// <summary>
    /// Folds a set of extracted files and a client fingerprint into one identity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Order-independent by construction: the files are sorted by map id before
    /// hashing, so an extractor that walks a directory in a different order produces
    /// the same identity for the same content. Two entries for one map id are
    /// refused rather than resolved — there is no correct choice between them, and
    /// picking one would make the identity depend on which was seen first.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// A duplicate map id, an empty or non-hex hash, or an empty client fingerprint.
    /// An identity computed over malformed inputs would still compare equal to
    /// itself, which is exactly how a broken extraction survives the check it exists
    /// to fail.
    /// </exception>
    public static MapGridSetIdentity Compute(IEnumerable<MapGridFile> files, string clientFingerprint)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientFingerprint);

        var ordered = new List<MapGridFile>(files);
        ordered.Sort(static (a, b) => a.MapId.CompareTo(b.MapId));

        var seen = new HashSet<int>();
        var builder = new StringBuilder();

        builder.Append(FormatVersion.ToString(CultureInfo.InvariantCulture)).Append(FieldSeparator);
        builder.Append(clientFingerprint).Append(RecordSeparator);

        foreach (MapGridFile file in ordered)
        {
            if (!seen.Add(file.MapId))
            {
                throw new ArgumentException(
                    $"Map {file.MapId} appears twice in the set. Which of the two is the grid is "
                    + "not a question this can answer.",
                    nameof(files));
            }

            if (!IsHex(file.Sha256))
            {
                throw new ArgumentException(
                    $"Map {file.MapId} carries a hash that is not hex: '{file.Sha256}'.",
                    nameof(files));
            }

            builder.Append(file.MapId.ToString(CultureInfo.InvariantCulture)).Append(FieldSeparator);
            builder.Append(file.Sha256.ToLowerInvariant()).Append(RecordSeparator);
        }

        string hash = Hash(Encoding.UTF8.GetBytes(builder.ToString()));

        return new MapGridSetIdentity(
            hash,
            ordered.ConvertAll(f => f with { Sha256 = f.Sha256.ToLowerInvariant() }).ToImmutableArray(),
            clientFingerprint);
    }

    /// <summary>SHA-256 of a grid file's exact bytes, lowercase hex.</summary>
    public static string HashFile(ReadOnlySpan<byte> fileBytes)
    {
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(fileBytes, digest);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    /// <summary>
    /// Whether the grids recorded against <paramref name="recorded"/> may be loaded
    /// now, and if not, what to say about it.
    /// </summary>
    /// <remarks>
    /// Fails closed on every ambiguity: a missing record, a missing current identity,
    /// or any mismatch all answer no. There is no "probably the same" here — the cost
    /// of being wrong is a character walking into geometry that moved.
    /// </remarks>
    public static bool MayLoad(
        MapGridSetIdentity? recorded,
        MapGridSetIdentity? current,
        out string? refusalReason)
    {
        if (recorded is null)
        {
            refusalReason = "map_grids_no_recorded_identity";
            return false;
        }

        if (current is null)
        {
            refusalReason = "map_grids_current_identity_unknown";
            return false;
        }

        if (!string.Equals(recorded.ClientFingerprint, current.ClientFingerprint, StringComparison.Ordinal))
        {
            refusalReason =
                $"client_build_changed:{Short(recorded.ClientFingerprint)}_to_{Short(current.ClientFingerprint)}";
            return false;
        }

        if (!string.Equals(recorded.SetHash, current.SetHash, StringComparison.Ordinal))
        {
            refusalReason =
                $"map_grid_set_changed:{Short(recorded.SetHash)}_to_{Short(current.SetHash)}";
            return false;
        }

        refusalReason = null;
        return true;
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string Short(string hash) =>
        hash.Length <= 12 ? hash : hash[..12];

    private static bool IsHex(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        foreach (char c in value)
        {
            bool hex = c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!hex)
                return false;
        }

        return true;
    }
}
