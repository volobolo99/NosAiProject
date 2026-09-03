using System.Globalization;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;

namespace NosAi.LiveIntegration;

/// <summary>
/// What the wire last said about the controlled character's vitals.
/// </summary>
/// <remarks>
/// <para>
/// The spec names percentages on <c>cond</c> and <c>st</c>. Observed
/// <c>cond</c> does not carry HP or MP at all — type, id and speed only —
/// so this table does not invent a percent there. <c>stat</c> carries the
/// player's absolute vitals (confirmed against the HUD); the percentage
/// compared to memory is derived from those four numbers. <c>st</c> is used
/// only when it names this character. <c>in</c> type 1 carries hp%/mp% when
/// that shape is present.
/// </para>
/// <para>
/// Last packet that named the player wins. Absolute fields stay null when
/// the packet only had percents.
/// </para>
/// </remarks>
public readonly record struct WirePlayerVitals(
    int? Hp,
    int? MaxHp,
    int? Mp,
    int? MaxMp,
    int? HpPercent,
    int? MpPercent,
    string Opcode)
{
    public bool HasPercent => HpPercent is >= 0 and <= 100;
}

/// <summary>The second source for player HP/MP: the world channel.</summary>
public static class WirePlayerVitalsParser
{
    /// <summary>
    /// Compares a memory candidate to the wire row. A match is a visible fact
    /// for the operator, not a promotion.
    /// </summary>
    public static string Compare(in PlayerVitalsCandidate memory, WirePlayerVitals? wire)
    {
        bool hasMem = memory.HasValue;
        bool hasWire = wire is { } row && (row.HasPercent || row.Hp is not null);

        if (!hasMem && !hasWire)
            return "—";
        if (!hasMem)
            return "wire-only";
        if (!hasWire)
            return "mem-only";

        WirePlayerVitals w = wire!.Value;

        if (w.Hp is { } absHp && w.MaxHp is { } absMax
            && (absHp < 0 || absMax < 0 || (uint)absHp != memory.Hp || (uint)absMax != memory.MaxHp))
            return "MISMATCH";

        if (w.Mp is { } absMp && w.MaxMp is { } absMaxMp
            && (absMp < 0 || absMaxMp < 0 || (uint)absMp != memory.Mp || (uint)absMaxMp != memory.MaxMp))
            return "MISMATCH";

        if (w.HasPercent
            && !PlayerVitalsPredicate.TryMatchPercent(memory.HpPercent, w.HpPercent!.Value, out _))
            return "MISMATCH";

        if (w.MpPercent is >= 0 and <= 100
            && !PlayerVitalsPredicate.TryMatchPercent(memory.MpPercent, w.MpPercent.Value, out _))
            return "MISMATCH";

        return "match";
    }

    /// <summary>
    /// Reads every framed world line and keeps the last packet that named the
    /// player. When <paramref name="playerId"/> is null only <c>stat</c> counts:
    /// that opcode is the player's by definition, and <c>st</c>/<c>in</c>
    /// without an id would mix in other entities.
    /// </summary>
    public static WirePlayerVitals? FromCapture(string path, long? playerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        WirePlayerVitals? last = null;
        using IPacketSource packets = CaptureFile.Open(path);
        var engine = new GameTrafficCaptureEngine(
            packets, NosTaleWorldFramer.Factory(DataSourceKind.Cached));

        engine.FrameProduced += frame =>
        {
            if (frame.Frame.Source == DataSourceKind.Unknown)
                return;

            foreach (string line in NosTaleWorldDecoder.Decode(frame.Frame.Body.Span))
            {
                if (TryParsePacket(line, playerId, out WirePlayerVitals entry))
                    last = entry;
            }
        };

        engine.Run();
        return last;
    }

    public static bool TryParsePacket(string text, long? playerId, out WirePlayerVitals entry)
    {
        entry = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string[] fields = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 2)
            return false;

        return fields[0] switch
        {
            "stat" => TryParseStat(fields, out entry),
            "st" => TryParseOther(fields, playerId, out entry),
            "in" => TryParseEnter(fields, playerId, out entry),
            _ => false,
        };
    }

    private static bool TryParseStat(string[] fields, out WirePlayerVitals entry)
    {
        entry = default;
        if (fields.Length < 5)
            return false;
        if (!TryInt(fields[1], out int hp)
            || !TryInt(fields[2], out int maxHp)
            || !TryInt(fields[3], out int mp)
            || !TryInt(fields[4], out int maxMp)
            || maxHp <= 0 || maxMp <= 0
            || hp < 0 || mp < 0
            || hp > maxHp || mp > maxMp)
            return false;

        entry = new WirePlayerVitals(
            hp, maxHp, mp, maxMp,
            PlayerVitalsBlock.Percent((uint)hp, (uint)maxHp),
            PlayerVitalsBlock.Percent((uint)mp, (uint)maxMp),
            "stat");
        return true;
    }

    private static bool TryParseOther(string[] fields, long? playerId, out WirePlayerVitals entry)
    {
        entry = default;
        if (playerId is null || fields.Length < 11)
            return false;
        if (!long.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long id)
            || id != playerId.Value)
            return false;
        if (!TryInt(fields[7], out int hp)
            || !TryInt(fields[8], out int mp)
            || !TryInt(fields[9], out int maxHp)
            || !TryInt(fields[10], out int maxMp)
            || maxHp <= 0 || maxMp <= 0
            || hp < 0 || mp < 0
            || hp > maxHp || mp > maxMp)
            return false;

        // Field 5 is the percent the protocol document tells us not to use.
        entry = new WirePlayerVitals(
            hp, maxHp, mp, maxMp,
            PlayerVitalsBlock.Percent((uint)hp, (uint)maxHp),
            PlayerVitalsBlock.Percent((uint)mp, (uint)maxMp),
            "st");
        return true;
    }

    private static bool TryParseEnter(string[] fields, long? playerId, out WirePlayerVitals entry)
    {
        entry = default;
        if (playerId is null || fields.Length < 9 || fields[1] != "1")
            return false;
        if (!long.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long id)
            || id != playerId.Value)
            return false;
        if (!TryInt(fields[7], out int hpPercent) || hpPercent is < 0 or > 100)
            return false;

        int? mpPercent = null;
        if (fields.Length >= 9 && TryInt(fields[8], out int parsedMp) && parsedMp is >= 0 and <= 100)
            mpPercent = parsedMp;

        entry = new WirePlayerVitals(
            Hp: null, MaxHp: null, Mp: null, MaxMp: null,
            HpPercent: hpPercent, MpPercent: mpPercent, Opcode: "in");
        return true;
    }

    private static bool TryInt(string text, out int value)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}
