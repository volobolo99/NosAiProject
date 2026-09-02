using System.Globalization;
using System.Reflection;
using System.Text;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.GameData;
using NosAi.Runtime.Perception.Network;

namespace NosAi.Runtime.Observability;

/// <summary>One distinct entity as a recording last stated it.</summary>
/// <param name="EntityId">Wire entity id.</param>
/// <param name="VnumText">
/// The vnum, <c>vnum non letto</c> when the sighting contract does not carry it,
/// or <c>vnum assente</c> when the contract carries a null.
/// </param>
/// <param name="NameText">Resolved display name, or an explicit UNKNOWN reason.</param>
/// <param name="X">Last stated map X.</param>
/// <param name="Y">Last stated map Y.</param>
/// <param name="HpText">Last HP ratio, or UNKNOWN when the sighting had none.</param>
/// <param name="PositionAgeText">Age of the position against the recording's last stamp.</param>
/// <param name="HpAgeText">Age of the health against the recording's last stamp.</param>
public readonly record struct WorldReplayEntityRow(
    long EntityId,
    string VnumText,
    string NameText,
    double X,
    double Y,
    string HpText,
    string PositionAgeText,
    string HpAgeText);

/// <summary>What <c>--world-replay</c> collected from the observation contract.</summary>
/// <param name="Summary">The existing census <see cref="WorldChannelReplay"/> already printed.</param>
/// <param name="Entities">Last sighting per distinct entity id, ordinal by id.</param>
/// <param name="Hits">Every <see cref="PlayerHit"/> the report published.</param>
/// <param name="SkillsReady">Every <c>sr</c>.</param>
/// <param name="Inventory">Every <c>ivn</c>.</param>
/// <param name="Pickups">Every <c>get</c>.</param>
/// <param name="GroundItems">Every <c>drop</c>.</param>
/// <param name="SelectionCount">How many <c>ct</c> selections the observation carried.</param>
/// <param name="SelectionReason">
/// Why selections are zero when they are: the decoder does not publish <c>ct</c>
/// on <see cref="NetworkObservationReport"/> today, and an empty list is not a
/// count of zero looks.
/// </param>
/// <param name="ObservedPackets">Packets the observer took.</param>
/// <param name="DecodedPackets">Packets that produced a non-empty decode.</param>
/// <param name="UndecodablePackets">Packets admitted and not decoded.</param>
/// <param name="UnreadableFrames">Frames the framer could not cut.</param>
/// <param name="FailureReason">Why the file could not be read, or null.</param>
public sealed record WorldReplayReport(
    WorldChannelReplaySummary? Summary,
    IReadOnlyList<WorldReplayEntityRow> Entities,
    IReadOnlyList<PlayerHit> Hits,
    IReadOnlyList<SkillReady> SkillsReady,
    IReadOnlyList<InventorySlotReading> Inventory,
    IReadOnlyList<ItemPickup> Pickups,
    IReadOnlyList<GroundItem> GroundItems,
    int SelectionCount,
    string SelectionReason,
    long ObservedPackets,
    long DecodedPackets,
    long UndecodablePackets,
    long UnreadableFrames,
    string? FailureReason)
{
    /// <summary>True when the recording was opened and drained.</summary>
    public bool Ok => FailureReason is null;
}

/// <summary>
/// Prints everything a recording's observation now carries, on top of
/// <see cref="WorldChannelReplay"/>'s census (CLI <c>--world-replay</c>).
/// </summary>
/// <remarks>
/// <para>
/// The five contracts on <see cref="NetworkObservationReport"/> —
/// <see cref="PlayerHit"/>, <see cref="SkillReady"/>, <see cref="InventorySlotReading"/>,
/// <see cref="ItemPickup"/>, <see cref="GroundItem"/> — are read as published.
/// An empty array is printed as zero with a reason; it is not filled from
/// another source and it is not treated as a fault of this command.
/// </para>
/// <para>
/// <c>vnum non letto</c> and <c>vnum assente</c> are different lines: the first
/// is a sighting type that does not carry a vnum field, the second is a field
/// present and null. The command does not invent a vnum from neighbouring
/// fields.
/// </para>
/// </remarks>
public static class WorldReplayCommand
{
    /// <summary>The operator flag.</summary>
    public const string Flag = "--world-replay";

    /// <summary><c>ct</c> is not a property of <see cref="NetworkObservationReport"/>.</summary>
    public const string CtNotOnObservation = "ct_not_on_observation";

    /// <summary>No <c>in</c>/<c>mv</c>/<c>st</c> sighting was published.</summary>
    public const string NothingSighted = "nothing_sighted";

    /// <summary>The <see cref="PlayerHit"/> contract arrived empty.</summary>
    public const string PlayerHitEmpty = "player_hit_empty";

    /// <summary>The <see cref="SkillReady"/> contract arrived empty.</summary>
    public const string SkillReadyEmpty = "skill_ready_empty";

    /// <summary>The <see cref="InventorySlotReading"/> contract arrived empty.</summary>
    public const string InventoryEmpty = "inventory_slot_empty";

    /// <summary>The <see cref="ItemPickup"/> contract arrived empty.</summary>
    public const string PickupEmpty = "item_pickup_empty";

    /// <summary>The <see cref="GroundItem"/> contract arrived empty.</summary>
    public const string GroundItemEmpty = "ground_item_empty";

    /// <summary>The sighting type does not declare a vnum member.</summary>
    public const string VnumNotRead = "vnum non letto";

    /// <summary>The sighting declares a vnum member and it is null.</summary>
    public const string VnumAbsent = "vnum assente";

    /// <summary>Language <see cref="GameReferenceDatabase.DisplayName"/> already uses in live tests.</summary>
    public const string CatalogLanguage = "IT";

    /// <summary>Missing path or unreadable bytes. Matches the other capture probes.</summary>
    public const int ExitUnreadable = 2;

    /// <summary>Console entry.</summary>
    public static int Run(string path, GameReferenceDatabase? catalog = null)
    {
        WorldReplayReport report = InspectFile(path, catalog);
        Console.Write(Format(report));
        return report.Ok ? 0 : ExitUnreadable;
    }

    /// <summary>Reads a <c>.noscap</c> path without writing anything.</summary>
    public static WorldReplayReport InspectFile(string path, GameReferenceDatabase? catalog = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Recording not found: {path}");
            return Failed("recording_not_found");
        }

        try
        {
            return Inspect(() => CaptureFile.Open(path), catalog);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Recording not readable: {path} ({ex.GetType().Name})");
            return Failed($"recording_unreadable:{ex.GetType().Name}");
        }
    }

    /// <summary>
    /// Drains a packet source twice: once for the existing census, once for the
    /// observation tables. The factory must yield a fresh source each call.
    /// </summary>
    public static WorldReplayReport Inspect(Func<IPacketSource> openSource, GameReferenceDatabase? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(openSource);

        WorldChannelReplaySummary summary = WorldChannelReplay.Replay(openSource);
        var entities = new Dictionary<long, EntitySighting>();
        var hits = new List<PlayerHit>();
        var skills = new List<SkillReady>();
        var inventory = new List<InventorySlotReading>();
        var pickups = new List<ItemPickup>();
        var ground = new List<GroundItem>();
        long observed = 0, decoded = 0, undecodable = 0;
        DateTime? asOf = null;
        int selections = 0;
        string selectionReason = CtNotOnObservation;

        IPacketSource packets = openSource();
        var endpoint = new GameEndpoint(packets.ServerAddress.ToString(), packets.ServerPort);
        using var observationSource = ReassembledObservationSource.ForNosTaleWorld(packets, DataSourceKind.Cached);
        var observer = new GameTrafficObserver(
            observationSource, new ScopedGameTrafficFilter(endpoint), new NosTaleWorldProtocolDecoder());

        while (true)
        {
            NetworkObservationReport report = observer.ObservePending(1);
            if (report.ObservedPackets == 0)
                break;

            observed += report.ObservedPackets;
            decoded += report.DecodedPackets;
            undecodable += report.UndecodablePackets;

            foreach (EntitySighting sighting in report.Sightings)
            {
                entities[sighting.EntityId] = sighting;
                asOf = Later(asOf, sighting.PositionObservedAtUtc);
                asOf = Later(asOf, sighting.HpObservedAtUtc);
            }

            if (report.LastPlayerHit is { } hit)
            {
                hits.Add(hit);
                asOf = Later(asOf, hit.ObservedAtUtc);
            }

            foreach (SkillReady ready in report.SkillsReady)
            {
                skills.Add(ready);
                asOf = Later(asOf, ready.ObservedAtUtc);
            }

            foreach (InventorySlotReading slot in report.InventorySlots)
            {
                inventory.Add(slot);
                asOf = Later(asOf, slot.ObservedAtUtc);
            }

            foreach (ItemPickup pickup in report.Pickups)
            {
                pickups.Add(pickup);
                asOf = Later(asOf, pickup.ObservedAtUtc);
            }

            foreach (GroundItem item in report.GroundItems)
            {
                ground.Add(item);
                asOf = Later(asOf, item.ObservedAtUtc);
            }

            asOf = Later(asOf, report.PlayerAttackedAtUtc);
            (int extra, string? reason) = CountSelections(report);
            if (reason is null)
            {
                selections += extra;
                if (extra > 0)
                    selectionReason = "";
            }
        }

        if (selections == 0 && selectionReason.Length == 0)
            selectionReason = CtNotOnObservation;

        DateTime stamp = asOf ?? default;
        var rows = new List<WorldReplayEntityRow>(entities.Count);
        foreach (EntitySighting sighting in entities.Values.OrderBy(s => s.EntityId))
            rows.Add(ToRow(sighting, stamp, catalog));

        return new WorldReplayReport(
            summary,
            rows,
            hits,
            skills,
            inventory,
            pickups,
            ground,
            selections,
            selectionReason,
            observed,
            decoded,
            undecodable,
            summary.UnreadableFrames,
            FailureReason: null);
    }

    /// <summary>The operator-facing block. Stable enough to assert against.</summary>
    public static string Format(WorldReplayReport report)
    {
        var text = new StringBuilder();
        if (report.FailureReason is { } failure)
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"unreadable: {failure}"));
            return text.ToString();
        }

        if (report.Summary is { } summary)
            text.Append(summary.Describe());

        text.AppendLine("observation:");
        AppendCounted(text, "entities", report.Entities.Count, NothingSighted);
        foreach (WorldReplayEntityRow row in report.Entities)
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  id={row.EntityId}  {row.VnumText}  name={row.NameText}  pos={row.X},{row.Y}  hp={row.HpText}  pos_age={row.PositionAgeText}  hp_age={row.HpAgeText}"));
        }

        AppendCounted(text, "aggressors", report.Hits.Count, PlayerHitEmpty);
        foreach (PlayerHit hit in report.Hits)
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  hits=1  id={hit.By.EntityId}  type={hit.By.EntityType}  at={hit.ObservedAtUtc:O}"));
        }

        AppendCounted(text, "selections (ct)", report.SelectionCount, report.SelectionReason);
        AppendCounted(text, "cooldowns (sr)", report.SkillsReady.Count, SkillReadyEmpty);
        foreach (SkillReady ready in report.SkillsReady)
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  slot={ready.Slot}  at={ready.ObservedAtUtc:O}"));
        }

        AppendCounted(text, "inventory (ivn)", report.Inventory.Count, InventoryEmpty);
        foreach (InventorySlotReading slot in report.Inventory)
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  kind={slot.InventoryKind}  slot={slot.Slot}  vnum={slot.Vnum}  amount={slot.Amount}  rarity={slot.Rarity}  at={slot.ObservedAtUtc:O}"));
        }

        AppendCounted(text, "pickups (get)", report.Pickups.Count, PickupEmpty);
        foreach (ItemPickup pickup in report.Pickups)
        {
            string byPlayer = pickup.ByPlayer is { } known
                ? known ? "true" : "false"
                : "UNKNOWN";
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  takerType={pickup.TakerType}  takerId={pickup.TakerId}  dropId={pickup.DropId}  byPlayer={byPlayer}  at={pickup.ObservedAtUtc:O}"));
        }

        AppendCounted(text, "ground (drop)", report.GroundItems.Count, GroundItemEmpty);
        foreach (GroundItem item in report.GroundItems)
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  vnum={item.Vnum}  dropId={item.DropId}  pos={item.X},{item.Y}  amount={item.Amount}  ownerId={item.OwnerId}  at={item.ObservedAtUtc:O}"));
        }

        text.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"packets: observed={report.ObservedPackets}  decoded={report.DecodedPackets}  undecodable={report.UndecodablePackets}  unreadable_frames={report.UnreadableFrames}"));
        return text.ToString();
    }

    private static WorldReplayReport Failed(string reason) => new(
        Summary: null,
        Entities: Array.Empty<WorldReplayEntityRow>(),
        Hits: Array.Empty<PlayerHit>(),
        SkillsReady: Array.Empty<SkillReady>(),
        Inventory: Array.Empty<InventorySlotReading>(),
        Pickups: Array.Empty<ItemPickup>(),
        GroundItems: Array.Empty<GroundItem>(),
        SelectionCount: 0,
        SelectionReason: CtNotOnObservation,
        ObservedPackets: 0,
        DecodedPackets: 0,
        UndecodablePackets: 0,
        UnreadableFrames: 0,
        FailureReason: reason);

    private static void AppendCounted(StringBuilder text, string label, int count, string emptyReason)
    {
        if (count == 0)
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{label}: 0  reason={emptyReason}"));
            return;
        }

        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{label}: {count}"));
    }

    private static WorldReplayEntityRow ToRow(
        EntitySighting sighting, DateTime asOf, GameReferenceDatabase? catalog)
    {
        string vnumText = DescribeVnum(sighting, out int? vnum);
        string name = DescribeName(sighting.Kind, vnum, vnumText, catalog);
        string hp = sighting.HpRatio is { } ratio
            ? string.Create(CultureInfo.InvariantCulture, $"{ratio:0.00}")
            : "UNKNOWN (hp_not_on_sighting)";
        string posAge = AgeText(asOf, sighting.PositionObservedAtUtc, "position_not_stamped");
        string hpAge = sighting.HpRatio is null
            ? "UNKNOWN (hp_not_on_sighting)"
            : AgeText(asOf, sighting.HpObservedAtUtc, "hp_not_stamped");
        return new WorldReplayEntityRow(
            sighting.EntityId, vnumText, name, sighting.X, sighting.Y, hp, posAge, hpAge);
    }

    /// <summary>
    /// Distinguishes a missing member from a null member. Does not invent a
    /// number from neighbouring packet fields.
    /// </summary>
    internal static string DescribeVnum(EntitySighting sighting, out int? vnum)
    {
        vnum = null;
        PropertyInfo? property = typeof(EntitySighting).GetProperty("Vnum");
        if (property is null)
            return VnumNotRead;

        object? boxed = property.GetValue(sighting);
        if (boxed is null)
            return VnumAbsent;

        if (boxed is int n)
        {
            vnum = n;
            return string.Create(CultureInfo.InvariantCulture, $"vnum={n}");
        }

        return VnumNotRead;
    }

    private static string DescribeName(
        string kind, int? vnum, string vnumText, GameReferenceDatabase? catalog)
    {
        if (vnumText == VnumNotRead)
            return "UNKNOWN (vnum non letto)";
        if (vnumText == VnumAbsent || vnum is null)
            return "UNKNOWN (vnum assente)";
        if (catalog is null)
            return "UNKNOWN (catalog_not_loaded)";

        string catalogKind = kind.ToLowerInvariant();
        string? name = catalog.DisplayName(catalogKind, vnum.Value, CatalogLanguage);
        return name is { Length: > 0 } ? name : "UNKNOWN (catalog_unknown)";
    }

    private static string AgeText(DateTime asOf, DateTime? stamped, string missingReason)
    {
        if (asOf == default || stamped is null)
            return string.Create(CultureInfo.InvariantCulture, $"UNKNOWN ({missingReason})");

        long ms = (long)(asOf - stamped.Value).TotalMilliseconds;
        if (ms < 0)
            ms = 0;
        return string.Create(CultureInfo.InvariantCulture, $"{ms}ms");
    }

    private static DateTime? Later(DateTime? current, DateTime? candidate)
    {
        if (candidate is null)
            return current;
        if (current is null || candidate.Value > current.Value)
            return candidate;
        return current;
    }

    /// <summary>
    /// <c>ct</c> is not on the report type today. If a parallel session published
    /// a collection named for it, count that; otherwise the zero is
    /// <see cref="CtNotOnObservation"/>, not an invented empty look.
    /// </summary>
    private static (int Count, string? Reason) CountSelections(NetworkObservationReport report)
    {
        foreach (string name in new[] { "Selections", "TargetSelections", "CurrentTargets" })
        {
            PropertyInfo? property = typeof(NetworkObservationReport).GetProperty(name);
            if (property is null)
                continue;
            if (property.GetValue(report) is System.Collections.IEnumerable items)
            {
                int n = 0;
                foreach (object? _ in items)
                    n++;
                return (n, null);
            }
        }

        return (0, CtNotOnObservation);
    }
}
