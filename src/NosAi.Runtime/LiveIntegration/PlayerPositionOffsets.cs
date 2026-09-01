using System.Globalization;
using System.Text;

namespace NosAi.LiveIntegration;

/// <summary>
/// Where the player's own coordinates sit in the client's memory, as the operator
/// found them.
/// </summary>
/// <remarks>
/// <para>
/// The result of F1-9, which cannot be done from here: an address is identified
/// by surviving several narrowings across changes the operator causes in game
/// (<c>--memory-scan</c>, <c>--memory-narrow</c>, <c>--memory-dump</c>). This is
/// where that result is written down so the runtime can use it.
/// </para>
/// <para>
/// <b>Relative to a module base, never absolute.</b> ASLR moves the image on
/// every start, so an absolute address found yesterday points at something else
/// today — and it would still read four bytes and return a plausible number,
/// which is the failure ADR-0014 says every memory provider must be able to
/// detect.
/// </para>
/// <para>
/// <b>Not committed.</b> Offsets belong to one build of one client on one
/// machine, so the file lives in gitignored <c>data/</c> beside the glyph atlas
/// and the screen calibration, for the reason ADR-0017 gives for the atlas.
/// </para>
/// </remarks>
public sealed record PlayerPositionOffsets
{
    /// <summary>Where the offsets live, relative to the repository root.</summary>
    public const string RelativePath = "data/memory/player-position.offsets";

    /// <summary>Reported while the operator has not found them.</summary>
    public const string NotFoundReason = "player_position_offsets_not_found";

    /// <summary>
    /// Reported for offsets that have never survived a restart of the client.
    /// </summary>
    /// <remarks>
    /// F1-9 is explicit: an offset that has not been re-verified after a restart
    /// is not an offset, it is an address that worked once. Enforced here rather
    /// than left as advice, because the reading it produces is a plausible number
    /// and nothing downstream could tell it from a real one.
    /// </remarks>
    public const string NotReverifiedReason = "player_position_offsets_not_reverified_after_restart";

    private const string Magic = "nosai-player-position-offsets";
    private const int Version = 1;

    private PlayerPositionOffsets(
        bool isPresent,
        string moduleName,
        int offsetX,
        int offsetY,
        int? offsetMapId,
        int verifiedRestarts,
        DateTime? foundAtUtc)
    {
        IsPresent = isPresent;
        ModuleName = moduleName;
        OffsetX = offsetX;
        OffsetY = offsetY;
        OffsetMapId = offsetMapId;
        VerifiedRestarts = verifiedRestarts;
        FoundAtUtc = foundAtUtc;
    }

    /// <summary>Whether a file was read. False is not "guess an address".</summary>
    public bool IsPresent { get; }

    /// <summary>The module the offsets are relative to, for example the client executable.</summary>
    public string ModuleName { get; }

    public int OffsetX { get; }
    public int OffsetY { get; }

    /// <summary>The current map id, when the operator also found it. Null otherwise.</summary>
    public int? OffsetMapId { get; }

    /// <summary>How many client restarts these offsets have been re-checked across.</summary>
    public int VerifiedRestarts { get; }

    /// <summary>When they were found, or null when there are none.</summary>
    public DateTime? FoundAtUtc { get; }

    /// <summary>
    /// Whether these may be read from at all.
    /// </summary>
    /// <remarks>
    /// Present is not the same as usable: offsets recorded once and never
    /// re-checked after a restart describe an address that happened to hold the
    /// right value, and using them would produce readings nothing can falsify.
    /// </remarks>
    public bool IsUsable => IsPresent && VerifiedRestarts >= 1;

    /// <summary>Why they cannot be read from, or null when they can.</summary>
    public string? UnusableReason => IsPresent
        ? (IsUsable ? null : NotReverifiedReason)
        : NotFoundReason;

    /// <summary>The state before F1-9 has been done.</summary>
    public static PlayerPositionOffsets Missing { get; } =
        new(false, string.Empty, 0, 0, null, 0, null);

    /// <summary>Offsets the operator found and re-verified.</summary>
    public static PlayerPositionOffsets Found(
        string moduleName,
        int offsetX,
        int offsetY,
        int? offsetMapId,
        int verifiedRestarts,
        DateTime foundAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        if (offsetX < 0 || offsetY < 0)
            throw new ArgumentOutOfRangeException(nameof(offsetX), "An offset from a module base is not negative.");
        if (offsetMapId is < 0)
            throw new ArgumentOutOfRangeException(nameof(offsetMapId), "An offset from a module base is not negative.");
        if (verifiedRestarts < 0)
            throw new ArgumentOutOfRangeException(nameof(verifiedRestarts));

        return new PlayerPositionOffsets(
            true, moduleName, offsetX, offsetY, offsetMapId, verifiedRestarts, foundAtUtc);
    }

    /// <summary>Loads the offsets, or returns <see cref="Missing"/> with a reason.</summary>
    public static PlayerPositionOffsets Load(string path, out string? failureReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        failureReason = null;

        if (!File.Exists(path))
        {
            failureReason = NotFoundReason;
            return Missing;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (IOException ex)
        {
            failureReason = $"player_position_offsets_unreadable:{ex.GetType().Name}";
            return Missing;
        }

        if (lines.Length < 2 || !lines[0].StartsWith(Magic, StringComparison.Ordinal))
        {
            failureReason = "player_position_offsets_header_unrecognised";
            return Missing;
        }

        string[] header = lines[0].Split(' ');
        if (header.Length != 2
            || !int.TryParse(header[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int version))
        {
            failureReason = "player_position_offsets_header_unrecognised";
            return Missing;
        }

        if (version != Version)
        {
            failureReason = $"player_position_offsets_version_unsupported:{version}";
            return Missing;
        }

        string[] fields = lines[1].Split(' ');
        if (fields.Length != 6
            || string.IsNullOrWhiteSpace(fields[0])
            || !TryOffset(fields[1], out int offsetX)
            || !TryOffset(fields[2], out int offsetY)
            || !DateTime.TryParse(fields[5], CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime foundAt)
            || !int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int restarts)
            || restarts < 0)
        {
            failureReason = "player_position_offsets_entry_malformed";
            return Missing;
        }

        int? offsetMapId = null;
        if (!string.Equals(fields[3], "-", StringComparison.Ordinal))
        {
            if (!TryOffset(fields[3], out int mapOffset))
            {
                failureReason = "player_position_offsets_entry_malformed";
                return Missing;
            }

            offsetMapId = mapOffset;
        }

        var offsets = new PlayerPositionOffsets(
            true, fields[0], offsetX, offsetY, offsetMapId, restarts, foundAt);

        // Present and unusable is a state worth reporting, not one to hide: the
        // operator has done half of F1-9 and needs to be told which half.
        failureReason = offsets.UnusableReason;
        return offsets;
    }

    /// <summary>Writes the offsets, creating the directory if needed.</summary>
    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!IsPresent)
            throw new InvalidOperationException("There are no offsets to write.");

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var text = new StringBuilder();
        text.Append(Magic).Append(' ').Append(Version).Append('\n');
        text
            .Append(ModuleName).Append(' ')
            .Append("0x").Append(OffsetX.ToString("X", CultureInfo.InvariantCulture)).Append(' ')
            .Append("0x").Append(OffsetY.ToString("X", CultureInfo.InvariantCulture)).Append(' ')
            .Append(OffsetMapId is { } map
                ? "0x" + map.ToString("X", CultureInfo.InvariantCulture)
                : "-")
            .Append(' ')
            .Append(VerifiedRestarts.ToString(CultureInfo.InvariantCulture)).Append(' ')
            .Append(FoundAtUtc!.Value.ToString("O", CultureInfo.InvariantCulture)).Append('\n');

        File.WriteAllText(path, text.ToString());
    }

    private static bool TryOffset(string field, out int value)
    {
        if (field.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(
                field.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)
                && value >= 0;
        }

        return int.TryParse(field, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
            && value >= 0;
    }
}
