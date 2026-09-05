using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace NosAi.Core.WorldModel;

/// <summary>
/// Canonical, culture-invariant text form of a <see cref="WorldModelSnapshot"/>
/// and the FNV-1a digest over it. The form is stable across processes and
/// days, which makes it the replay fingerprint: the same observations must
/// produce byte-identical text.
/// </summary>
/// <remarks>
/// This is a fingerprint and audit format, not a transport: no parser is
/// offered, so no field can be silently defaulted on the way back in.
/// </remarks>
public static class WorldModelCanonicalText
{
    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    public static string Write(WorldModelSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var sb = new StringBuilder(4096);
        Append(sb, snapshot);
        return sb.ToString();
    }

    public static ulong Digest(WorldModelSnapshot snapshot)
    {
        var text = Write(snapshot);
        var hash = FnvOffset;
        foreach (var ch in text)
        {
            hash ^= (byte)(ch & 0xFF);
            hash *= FnvPrime;
            hash ^= (byte)(ch >> 8);
            hash *= FnvPrime;
        }

        return hash;
    }

    private static void Append(StringBuilder sb, WorldModelSnapshot s)
    {
        Line(sb, "schema", s.SchemaVersion);
        Line(sb, "revision", s.Revision);
        Line(sb, "observedAt", s.ObservedAtUnixMillis);

        var p = s.Player;
        Fact(sb, "player.id", p.Id, v => v.ToString());
        Fact(sb, "player.name", p.Name, v => v);
        Fact(sb, "player.level", p.Level, Int);
        Fact(sb, "player.jobLevel", p.JobLevel, Int);
        Fact(sb, "player.position", p.Position, Cell);
        Fact(sb, "player.facing", p.Facing, v => ((byte)v).ToString(CultureInfo.InvariantCulture));
        Fact(sb, "player.hp", p.Hp, VitalText);
        Fact(sb, "player.mp", p.Mp, VitalText);
        Fact(sb, "player.alive", p.Alive, Bool);
        Fact(sb, "player.inCombat", p.InCombat, Bool);
        Fact(sb, "player.target", p.Target, v => v.ToString());
        Fact(sb, "player.lastAggressor", p.LastAggressor, v => v.ToString());

        var m = s.Map;
        Fact(sb, "map.id", m.Id, v => v.ToString());
        Fact(sb, "map.name", m.Name, v => v);
        Fact(sb, "map.bounds", m.Bounds, v => string.Create(CultureInfo.InvariantCulture, $"{v.Width}x{v.Height}"));
        Count(sb, "map.tiles", m.Tiles);
        foreach (var tile in m.Tiles.OrEmpty())
        {
            Fact(sb, "map.tile" + Cell(tile.Cell), tile.State, v => ((byte)v).ToString(CultureInfo.InvariantCulture));
        }

        Count(sb, "map.regions", m.Regions);
        var regionIndex = 0;
        foreach (var region in m.Regions.OrEmpty())
        {
            var key = "map.region[" + regionIndex.ToString(CultureInfo.InvariantCulture) + "]";
            Line(sb, key + ".vertices", region.Vertices.IsDefault ? "-" : string.Join(";", region.Vertices.Select(Cell)));
            Fact(sb, key + ".state", region.State, v => ((byte)v).ToString(CultureInfo.InvariantCulture));
            regionIndex++;
        }

        Count(sb, "map.portals", m.Portals);
        foreach (var portal in m.Portals.OrEmpty())
        {
            var key = "map.portal[" + portal.Id + "]";
            Fact(sb, key + ".cell", portal.Cell, Cell);
            Fact(sb, key + ".destMap", portal.DestinationMap, v => v.ToString());
            Fact(sb, key + ".destCell", portal.DestinationCell, Cell);
        }

        Count(sb, "mobs", s.Mobs);
        foreach (var mob in s.Mobs.OrEmpty())
        {
            var key = "mob[" + mob.Id + "]";
            Fact(sb, key + ".catalogue", mob.CatalogueId, Int);
            Fact(sb, key + ".name", mob.Name, v => v);
            Fact(sb, key + ".level", mob.Level, Int);
            Fact(sb, key + ".position", mob.Position, Cell);
            Fact(sb, key + ".hp", mob.Hp, VitalText);
            Fact(sb, key + ".hostile", mob.Hostile, Bool);
            Fact(sb, key + ".targetingPlayer", mob.TargetingPlayer, Bool);
            Fact(sb, key + ".presence", mob.Presence, v => ((byte)v).ToString(CultureInfo.InvariantCulture));
        }

        Count(sb, "npcs", s.Npcs);
        foreach (var npc in s.Npcs.OrEmpty())
        {
            var key = "npc[" + npc.Id + "]";
            Fact(sb, key + ".catalogue", npc.CatalogueId, Int);
            Fact(sb, key + ".name", npc.Name, v => v);
            Fact(sb, key + ".position", npc.Position, Cell);
            Fact(sb, key + ".interactable", npc.Interactable, Bool);
            Fact(sb, key + ".presence", npc.Presence, v => ((byte)v).ToString(CultureInfo.InvariantCulture));
        }

        Count(sb, "drops", s.Drops);
        foreach (var drop in s.Drops.OrEmpty())
        {
            var key = "drop[" + drop.Id + "]";
            Fact(sb, key + ".item", drop.Item, v => v.ToString());
            Fact(sb, key + ".quantity", drop.Quantity, Int);
            Fact(sb, key + ".position", drop.Position, Cell);
            Fact(sb, key + ".owner", drop.Owner, v => v.ToString());
            Fact(sb, key + ".presence", drop.Presence, v => ((byte)v).ToString(CultureInfo.InvariantCulture));
        }

        Count(sb, "quests", s.Quests);
        foreach (var quest in s.Quests.OrEmpty())
        {
            var key = "quest[" + Escape(quest.Id.Value) + "]";
            Fact(sb, key + ".title", quest.Title, v => v);
            Fact(sb, key + ".status", quest.Status, v => ((byte)v).ToString(CultureInfo.InvariantCulture));
            Count(sb, key + ".objectives", quest.Objectives);
            foreach (var objective in quest.Objectives.OrEmpty())
            {
                var okey = key + ".objective[" + Escape(objective.Key) + "]";
                Fact(sb, okey + ".description", objective.Description, v => v);
                Fact(sb, okey + ".progress", objective.Progress, Int);
                Fact(sb, okey + ".required", objective.Required, Int);
            }
        }

        Count(sb, "inventory", s.Inventory);
        foreach (var item in s.Inventory.OrEmpty())
        {
            var key = string.Create(CultureInfo.InvariantCulture, $"inventory[{(byte)item.Bag}/{item.Slot}]");
            Fact(sb, key + ".item", item.Item, v => v.ToString());
            Fact(sb, key + ".quantity", item.Quantity, Int);
            Fact(sb, key + ".rarity", item.Rarity, Int);
            Fact(sb, key + ".upgrade", item.Upgrade, Int);
        }

        Count(sb, "equipment", s.Equipment);
        foreach (var item in s.Equipment.OrEmpty())
        {
            var key = "equipment[" + ((byte)item.Slot).ToString(CultureInfo.InvariantCulture) + "]";
            Fact(sb, key + ".item", item.Item, v => v.ToString());
            Fact(sb, key + ".rarity", item.Rarity, Int);
            Fact(sb, key + ".upgrade", item.Upgrade, Int);
            Fact(sb, key + ".durability", item.Durability, Int);
        }

        Count(sb, "skills", s.Skills);
        foreach (var skill in s.Skills.OrEmpty())
        {
            var key = "skill[" + skill.Id + "]";
            Fact(sb, key + ".name", skill.Name, v => v);
            Fact(sb, key + ".mpCost", skill.MpCost, Int);
            Fact(sb, key + ".range", skill.Range, Int);
            Fact(sb, key + ".castTime", skill.CastTimeMillis, Int);
            Fact(sb, key + ".cooldown", skill.CooldownMillis, Int);
            Fact(sb, key + ".ready", skill.Ready, Bool);
            Fact(sb, key + ".readyAt", skill.ReadyAtUnixMillis, Long);
        }

        Count(sb, "statusEffects", s.StatusEffects);
        foreach (var effect in s.StatusEffects.OrEmpty())
        {
            var key = string.Create(CultureInfo.InvariantCulture, $"effect[{effect.Id}/{(byte)effect.Polarity}]");
            Fact(sb, key + ".name", effect.Name, v => v);
            Fact(sb, key + ".level", effect.Level, Int);
            Fact(sb, key + ".appliedAt", effect.AppliedAtUnixMillis, Long);
            Fact(sb, key + ".expiresAt", effect.ExpiresAtUnixMillis, Long);
        }

        Count(sb, "cooldowns", s.Cooldowns);
        foreach (var cooldown in s.Cooldowns.OrEmpty())
        {
            var key = string.Create(CultureInfo.InvariantCulture, $"cooldown[{(byte)cooldown.Scope}/{cooldown.Key}]");
            Fact(sb, key + ".startedAt", cooldown.StartedAtUnixMillis, Long);
            Fact(sb, key + ".readyAt", cooldown.ReadyAtUnixMillis, Long);
        }

        Count(sb, "resources", s.Resources);
        foreach (var resource in s.Resources.OrEmpty())
        {
            var key = "resource[" + ((byte)resource.Kind).ToString(CultureInfo.InvariantCulture) + "]";
            Fact(sb, key + ".amount", resource.Amount, Long);
            Fact(sb, key + ".maximum", resource.Maximum, Long);
        }

        Count(sb, "actions", s.Actions);
        foreach (var action in s.Actions.OrEmpty())
        {
            var key = "action[" + Escape(action.Id.Value) + "]";
            Line(sb, key + ".category", (byte)action.Category);
            Fact(sb, key + ".availability", action.Availability, v => ((byte)v).ToString(CultureInfo.InvariantCulture));
            Fact(sb, key + ".lastAttemptedAt", action.LastAttemptedAtUnixMillis, Long);
            Fact(sb, key + ".lastOutcome", action.LastOutcome, v => ((byte)v).ToString(CultureInfo.InvariantCulture));
            Fact(sb, key + ".consecutiveFailures", action.ConsecutiveFailures, Int);
        }

        Count(sb, "goals", s.Goals);
        foreach (var goal in s.Goals.OrEmpty())
        {
            var key = "goal[" + Escape(goal.Id.Value) + "]";
            Line(sb, key + ".kind", (byte)goal.Kind);
            Line(sb, key + ".priority", goal.Priority);
            Line(sb, key + ".relatedQuest", Escape(goal.RelatedQuest.Value));
            Fact(sb, key + ".status", goal.Status, v => ((byte)v).ToString(CultureInfo.InvariantCulture));
            Fact(sb, key + ".progress", goal.Progress, v => v.ToString("R", CultureInfo.InvariantCulture));
            Fact(sb, key + ".deadline", goal.DeadlineUnixMillis, Long);
        }
    }

    private static void Fact<T>(StringBuilder sb, string key, WorldFact<T> fact, Func<T, string> render)
    {
        sb.Append(key).Append('=')
          .Append(fact.Source.ToWire()).Append('|')
          .Append((byte)fact.Channel).Append('|')
          .Append(fact.Confidence.ToString("R", CultureInfo.InvariantCulture)).Append('|')
          .Append(fact.ObservedAtUnixMillis.ToString(CultureInfo.InvariantCulture)).Append('|')
          .Append(fact.TryGetValue(out var value) ? Escape(render(value)) : "∅").Append('|')
          .Append(Escape(fact.Reason))
          .Append('\n');
    }

    private static void Line(StringBuilder sb, string key, long value)
        => sb.Append(key).Append('=').Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');

    private static void Line(StringBuilder sb, string key, string value)
        => sb.Append(key).Append('=').Append(value).Append('\n');

    private static void Count<T>(StringBuilder sb, string key, ImmutableArray<T> items)
        => Line(sb, key + ".count", items.IsDefault ? 0 : items.Length);

    private static string Int(int v) => v.ToString(CultureInfo.InvariantCulture);
    private static string Long(long v) => v.ToString(CultureInfo.InvariantCulture);
    private static string Bool(bool v) => v ? "1" : "0";
    private static string Cell(MapCell c) => c.ToString();
    private static string VitalText(Vital v) => string.Create(CultureInfo.InvariantCulture, $"{v.Current}/{v.Maximum}");

    /// <summary>Escapes separators and newlines so a value cannot forge a line boundary.</summary>
    private static string Escape(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '|': sb.Append("\\|"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '=': sb.Append("\\="); break;
                case '[': sb.Append("\\["); break;
                case ']': sb.Append("\\]"); break;
                default: sb.Append(ch); break;
            }
        }

        return sb.ToString();
    }
}
