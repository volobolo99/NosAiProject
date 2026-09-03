using System.Globalization;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Navigation;

namespace NosAi.LiveIntegration;

/// <summary>
/// Four uint32s laid out as the third source described the stats block, read
/// from a window whose base <see cref="NosTaleClientLayout.TryResolveBases"/>
/// already knows.
/// </summary>
/// <remarks>
/// <para>
/// Intra-block offsets from <c>docs/MAPPA_MEMORIA_CLIENT_CANDIDATI.md</c> § 4.2,
/// not an RVA and not a location. The RVA that document also printed is the
/// one thing this type must not grow: a block is found by scanning a resolved
/// base, and the result is a distance from that base.
/// </para>
/// <para>
/// A parse that succeeds is still UNKNOWN. Concordance with the wire's
/// percentage is evidence for the operator, not a promotion this type can make.
/// </para>
/// </remarks>
public readonly record struct PlayerVitalsBlock(uint Hp, uint MaxHp, uint Mp, uint MaxMp)
{
    /// <summary>MaxMP sits at the start of the block.</summary>
    public const int MaxMpOffset = 0x00;

    /// <summary>MP sits four bytes after MaxMP.</summary>
    public const int MpOffset = 0x04;

    /// <summary>MaxHP sits this far past MaxMP. The HP pair is the same distance past MP.</summary>
    public const int MaxHpOffset = 0xF0;

    /// <summary>HP sits four bytes after MaxHP.</summary>
    public const int HpOffset = 0xF4;

    /// <summary>
    /// Bytes the block occupies from its start through the HP word.
    /// </summary>
    /// <remarks>
    /// A scan that would read past the window is not a short block: it is a
    /// start that does not belong to this base.
    /// </remarks>
    public const int Size = HpOffset + sizeof(uint);

    /// <summary>MaxHP − MaxMP, and HP − MP. Required to hold between two readings.</summary>
    public const int PairDistance = MaxHpOffset - MaxMpOffset;

    /// <summary>
    /// Largest value taken to be a vital rather than a pointer or a tick.
    /// </summary>
    /// <remarks>
    /// Measured HP on this client sat at 7305. The cap is two orders above that
    /// so a later character is not refused, and still far below a heap address.
    /// </remarks>
    public const uint MaxPlausible = 1_000_000;

    public const string TruncatedReason = "player_vitals_truncated";
    public const string MaxHpZeroReason = "player_vitals_max_hp_zero";
    public const string MaxMpZeroReason = "player_vitals_max_mp_zero";
    public const string HpAboveMaxPrefix = "player_vitals_hp_above_max";
    public const string MpAboveMaxPrefix = "player_vitals_mp_above_max";
    public const string HpImplausiblePrefix = "player_vitals_hp_implausible";
    public const string MpImplausiblePrefix = "player_vitals_mp_implausible";
    public const string MaxHpImplausiblePrefix = "player_vitals_max_hp_implausible";
    public const string MaxMpImplausiblePrefix = "player_vitals_max_mp_implausible";

    /// <summary>How far HP is from MaxMP. Equals <see cref="PairDistance"/> by construction.</summary>
    public int ObservedPairDistance => PairDistance;

    public int HpPercent => Percent(Hp, MaxHp);
    public int MpPercent => Percent(Mp, MaxMp);

    /// <summary>
    /// Integer percent the client's HUD uses: nearest, halves away from zero.
    /// </summary>
    public static int Percent(uint value, uint max)
    {
        if (max == 0)
            return 0;
        return (int)Math.Round(value * 100.0 / max, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Reads the four words at the candidate intra-block offsets and applies the
    /// range predicate. Nothing is remembered from a previous parse.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> bytes, out PlayerVitalsBlock block, out string? failureReason)
    {
        block = default;
        if (bytes.Length < Size)
        {
            failureReason = TruncatedReason;
            return false;
        }

        uint maxMp = BitConverter.ToUInt32(bytes[MaxMpOffset..]);
        uint mp = BitConverter.ToUInt32(bytes[MpOffset..]);
        uint maxHp = BitConverter.ToUInt32(bytes[MaxHpOffset..]);
        uint hp = BitConverter.ToUInt32(bytes[HpOffset..]);

        if (!TryRange(hp, maxHp, mp, maxMp, out failureReason))
            return false;

        block = new PlayerVitalsBlock(hp, maxHp, mp, maxMp);
        return true;
    }

    /// <summary>
    /// The permanent range predicate: maxima non-zero and plausible, currents
    /// inside them. Run on every read, not only while searching.
    /// </summary>
    public static bool TryRange(
        uint hp, uint maxHp, uint mp, uint maxMp, out string? failureReason)
    {
        if (maxHp == 0)
        {
            failureReason = MaxHpZeroReason;
            return false;
        }

        if (maxMp == 0)
        {
            failureReason = MaxMpZeroReason;
            return false;
        }

        if (maxHp > MaxPlausible)
        {
            failureReason = string.Create(CultureInfo.InvariantCulture, $"{MaxHpImplausiblePrefix}:{maxHp}");
            return false;
        }

        if (maxMp > MaxPlausible)
        {
            failureReason = string.Create(CultureInfo.InvariantCulture, $"{MaxMpImplausiblePrefix}:{maxMp}");
            return false;
        }

        if (hp > MaxPlausible)
        {
            failureReason = string.Create(CultureInfo.InvariantCulture, $"{HpImplausiblePrefix}:{hp}");
            return false;
        }

        if (mp > MaxPlausible)
        {
            failureReason = string.Create(CultureInfo.InvariantCulture, $"{MpImplausiblePrefix}:{mp}");
            return false;
        }

        if (hp > maxHp)
        {
            failureReason = string.Create(CultureInfo.InvariantCulture, $"{HpAboveMaxPrefix}:{hp}>{maxHp}");
            return false;
        }

        if (mp > maxMp)
        {
            failureReason = string.Create(CultureInfo.InvariantCulture, $"{MpAboveMaxPrefix}:{mp}>{maxMp}");
            return false;
        }

        failureReason = null;
        return true;
    }
}

/// <summary>
/// A stats block found as a distance from a base the attach resolved, and why
/// it is not a fact yet.
/// </summary>
/// <remarks>
/// <see cref="HasValue"/> means the four words parsed. It is never
/// <see cref="DataSourceKind.Live"/>: that jump needs the wire's percentage to
/// agree in a real session, and nothing in this type can grant it.
/// </remarks>
public readonly record struct PlayerVitalsCandidate(
    uint Hp,
    uint MaxHp,
    uint Mp,
    uint MaxMp,
    MapIdAnchorKind Anchor,
    int Offset,
    string Reason)
{
    public bool HasValue => Reason == NotEstablishedReason;

    public DataSourceKind Source => DataSourceKind.Unknown;

    public const string NotEstablishedReason = "player_vitals_not_established";
    public const string NotFoundReason = "player_vitals_not_found";
    public const string AmbiguousPrefix = "player_vitals_ambiguous";

    public int HpPercent => PlayerVitalsBlock.Percent(Hp, MaxHp);
    public int MpPercent => PlayerVitalsBlock.Percent(Mp, MaxMp);

    public PlayerVitalsBlock Block => new(Hp, MaxHp, Mp, MaxMp);

    public string DescribeOffset() => Anchor is MapIdAnchorKind.Heap
        ? string.Create(CultureInfo.InvariantCulture, $"heap 0x{Offset:X}")
        : string.Create(CultureInfo.InvariantCulture, $"{MapIdAnchors.NameOf(Anchor)}+0x{Offset:X}");

    public static PlayerVitalsCandidate Missing(string reason) =>
        new(0, 0, 0, 0, MapIdAnchorKind.Heap, 0, reason);

    public static PlayerVitalsCandidate From(in PlayerVitalsHit hit) => new(
        hit.Block.Hp, hit.Block.MaxHp, hit.Block.Mp, hit.Block.MaxMp,
        hit.Anchor, hit.Offset, NotEstablishedReason);
}

/// <summary>One structural candidate inside a resolved window.</summary>
public readonly record struct PlayerVitalsHit(
    MapIdAnchorKind Anchor, int Offset, PlayerVitalsBlock Block)
{
    public string Key => string.Create(CultureInfo.InvariantCulture,
        $"{MapIdAnchors.NameOf(Anchor)}+0x{Offset:X}");
}

/// <summary>
/// The predicates that run on every read once a candidate exists, not only
/// while the block is being searched for.
/// </summary>
public static class PlayerVitalsPredicate
{
    /// <summary>
    /// How far two integer percents may differ and still be the same vital.
    /// </summary>
    /// <remarks>
    /// One point is the rounding of <c>round(hp*100/max)</c>. The <c>st</c>
    /// field-5 discrepancy of about two points is a different packet field,
    /// ignored here the way the protocol decoder already ignores it.
    /// </remarks>
    public const int PercentTolerance = 1;

    public const string HpJumpedPrefix = "player_vitals_hp_jumped";
    public const string RatioMismatchPrefix = "player_vitals_ratio_mismatch";

    /// <summary>
    /// A drop or spike larger than the maximum itself is a pointer that moved,
    /// not a hit. A real blow cannot remove more HP than the bar holds.
    /// </summary>
    public static bool TryContinuity(
        in PlayerVitalsBlock previous, in PlayerVitalsBlock current, out string? failureReason)
    {
        uint ceiling = previous.MaxHp > current.MaxHp ? previous.MaxHp : current.MaxHp;
        uint delta = previous.Hp > current.Hp ? previous.Hp - current.Hp : current.Hp - previous.Hp;
        if (delta > ceiling)
        {
            failureReason = string.Create(CultureInfo.InvariantCulture,
                $"{HpJumpedPrefix}:{delta}_over_{ceiling}");
            return false;
        }

        failureReason = null;
        return true;
    }

    /// <summary>
    /// Memory absolute vs the wire's integer percent, within
    /// <see cref="PercentTolerance"/>.
    /// </summary>
    public static bool TryMatchPercent(
        int memoryPercent, int wirePercent, out string? failureReason)
    {
        int delta = memoryPercent > wirePercent ? memoryPercent - wirePercent : wirePercent - memoryPercent;
        if (delta > PercentTolerance)
        {
            failureReason = string.Create(CultureInfo.InvariantCulture,
                $"{RatioMismatchPrefix}:{memoryPercent}_not_{wirePercent}");
            return false;
        }

        failureReason = null;
        return true;
    }
}

/// <summary>
/// The damage oracle: HP (and only HP) falls while both maxima stay put.
/// </summary>
/// <remarks>
/// MP may move or not — a physical hit does not spend it. A word that moved
/// with HP but also moved its maximum is a counter, not the vitals block.
/// </remarks>
public static class PlayerVitalsOracle
{
    public static bool TookDamage(in PlayerVitalsHit before, in PlayerVitalsHit after)
        => before.Anchor == after.Anchor
           && before.Offset == after.Offset
           && after.Block.MaxHp == before.Block.MaxHp
           && after.Block.MaxMp == before.Block.MaxMp
           && after.Block.Hp < before.Block.Hp
           && PlayerVitalsPredicate.TryContinuity(before.Block, after.Block, out _);

    /// <summary>
    /// Keeps the hits whose offset still parses and whose HP fell while the
    /// maxima held. Pair distance is structural (always
    /// <see cref="PlayerVitalsBlock.PairDistance"/>); surviving here is that
    /// the same offset still has that shape.
    /// </summary>
    public static List<PlayerVitalsHit> Survivors(
        IReadOnlyList<PlayerVitalsHit> before, IReadOnlyList<PlayerVitalsHit> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var later = new Dictionary<string, PlayerVitalsHit>(after.Count, StringComparer.Ordinal);
        foreach (PlayerVitalsHit hit in after)
            later[hit.Key] = hit;

        var survivors = new List<PlayerVitalsHit>();
        foreach (PlayerVitalsHit previous in before)
        {
            if (later.TryGetValue(previous.Key, out PlayerVitalsHit next)
                && TookDamage(previous, next))
                survivors.Add(next);
        }

        return survivors;
    }
}

/// <summary>
/// Walks a window already read from a resolved base and keeps every start that
/// parses as the stats block.
/// </summary>
/// <remarks>
/// <para>
/// The loop bound is the window minus the block size, compared <i>before</i>
/// the slice: a length read from a wrong chain is four bytes of anything, and
/// must not size a read.
/// </para>
/// <para>
/// A start four bytes into a real block often still parses — it reads MP as
/// MaxMP and HP as MaxHP, with zeros for the currents. That is why a unique
/// structural hit is not concordance: the ghost's "maximum" is the real
/// current, so it moves when HP does, and
/// <see cref="PlayerVitalsOracle"/> drops it.
/// </para>
/// </remarks>
public static class PlayerVitalsScan
{
    /// <summary>How far past each resolved base the stats block is looked for.</summary>
    /// <remarks>
    /// <para>
    /// This is deliberately <b>not</b> <see cref="MapIdAnchors.StructWindow"/>.
    /// That constant answers a different question — how far an offset may sit
    /// from an anchor and still be called anchor-relative by the map id finder —
    /// and borrowing it capped this scan at 0x1000 by coincidence rather than by
    /// argument.
    /// </para>
    /// <para>
    /// 0x1000 was measured to be too narrow on a real client: with the wire
    /// reporting a maximum of 7305, not one candidate in either window carried
    /// that maximum. A stable maximum absent from the scan is the scan looking
    /// in the wrong place, so the bound is widened until the evidence says
    /// otherwise. Widening costs noise, not correctness: the oracle drops every
    /// extra candidate that does not behave like health.
    /// </para>
    /// </remarks>
    public const int DefaultWindowBytes = 0x10000;

    /// <summary>
    /// The widest window a caller may ask for.
    /// </summary>
    /// <remarks>
    /// A window sizes a read and a loop, so it is bounded before either. Kept
    /// well under <c>ProcessMemoryReader.MaxReadLength</c> so an oversized ask
    /// is refused here, by a number chosen for this scan, rather than by a
    /// generic read guard.
    /// </remarks>
    public const int MaxWindowBytes = 0x40000;

    /// <summary>Clamps a requested window into what this scan will read.</summary>
    public static int ClampWindow(int requestedBytes) => requestedBytes switch
    {
        < PlayerVitalsBlock.Size => PlayerVitalsBlock.Size,
        > MaxWindowBytes => MaxWindowBytes,
        _ => requestedBytes,
    };

    /// <summary>
    /// Last start offset still inside the struct window with room for HP.
    /// </summary>
    public static int LastStartOffset(int windowLength)
        => windowLength < PlayerVitalsBlock.Size ? -1 : windowLength - PlayerVitalsBlock.Size;

    public static void Collect(
        ReadOnlySpan<byte> window, MapIdAnchorKind anchor, List<PlayerVitalsHit> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        int last = LastStartOffset(window.Length);
        if (last < 0)
            return;

        if (last >= MaxWindowBytes)
            last = MaxWindowBytes - PlayerVitalsBlock.Size;

        for (int offset = 0; offset <= last; offset += sizeof(uint))
        {
            if (!PlayerVitalsBlock.TryParse(window[offset..], out PlayerVitalsBlock block, out _))
                continue;

            into.Add(new PlayerVitalsHit(anchor, offset, block));
        }
    }
}
