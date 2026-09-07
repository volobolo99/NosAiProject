using System.Globalization;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.LiveIntegration;

namespace NosAi.ControlPanel;

/// <summary>How the surroundings list was answered, as three distinct drawings.</summary>
internal enum SurroundingsKind : byte
{
    /// <summary>Nothing was observed. Not the same as an empty map.</summary>
    NoObservation = 0,

    /// <summary>The runtime looked and saw no entities.</summary>
    NoEntitiesAround = 1,

    /// <summary>One or more entities were observed, each with an age.</summary>
    Populated = 2
}

/// <summary>One observed entity as the operator sees it, including the age of the position.</summary>
internal sealed record NearbyEntityRow(
    long EntityId,
    string Vnum,
    string Name,
    string Position,
    string Life,
    string Age,
    double AgeSeconds,
    string Source,
    string Species = "");

/// <summary>Operator-facing surroundings: observed entities, or why there are none to draw.</summary>
internal sealed class SurroundingsView
{
    public SurroundingsKind Kind { get; init; }
    public string Summary { get; init; } = "";
    public IReadOnlyList<DisplayField> Fields { get; init; } = Array.Empty<DisplayField>();
    public IReadOnlyList<NearbyEntityRow> Rows { get; init; } = Array.Empty<NearbyEntityRow>();
}

/// <summary>
/// Read-only surroundings view. Empty observation and an empty map are different
/// drawings: the first is UNKNOWN, the second is a looked-at absence. Age is
/// part of the drawing so a thirty-second-old position cannot look like one
/// that just arrived. No vnum is invented when the observation does not carry one.
/// </summary>
internal static class SurroundingsInspect
{
    /// <summary>The list itself was never published or could not be read.</summary>
    public const string NoObservationLabel = "nessuna osservazione";

    /// <summary>The list was read and contains nobody.</summary>
    public const string NoEntitiesAroundLabel = "nessuna entità attorno";

    /// <summary>
    /// L'osservazione non ha portato un vnum: nessun pacchetto di comparsa ha
    /// detto cosa sia questa entità. Nominato, invece che lasciato come un
    /// UNKNOWN nudo.
    /// </summary>
    /// <remarks>
    /// <b>Correzione del 2026-09-08.</b> Questo commento diceva che
    /// <see cref="SelectableEntity"/> non porta un vnum. Lo porta, e lo snapshot
    /// lo pubblica da sempre: era <c>GameplayWireReader.TryEntity</c> a
    /// scartarlo, quindi il pannello scriveva questo motivo anche per entità il
    /// cui numero era lì nel JSON. Ora il motivo compare solo quando il vnum
    /// manca davvero — che è il caso ordinario, perché solo <c>in</c> lo porta.
    /// </remarks>
    public const string VnumNotOnObservation = "vnum_not_on_observation";

    /// <summary>La specie non è stata dichiarata dall'osservazione.</summary>
    /// <remarks>
    /// Non è «mostro» per omissione: la stessa regola del vnum. Un'entità che
    /// arriva da una sorgente più vecchia dello snapshot con la specie compare
    /// così, e l'operatore vede che non lo sa nessuno.
    /// </remarks>
    public const string SpeciesNotOnObservation = "species_not_on_observation";

    /// <summary>Health was never stated on this sighting. Not zero and not full.</summary>
    public const string HpNotStated = "hp_not_stated";

    /// <summary>
    /// Il vnum c'è, il nome no: risolverlo vuole il catalogo di riferimento, che
    /// questa vista non apre.
    /// </summary>
    public const string NameNeedsCatalogue = "name_needs_reference_catalogue";

    /// <summary>
    /// Formats the surroundings. <paramref name="nowUtc"/> is the instant ages
    /// are measured against; the panel passes the system clock, tests pass a
    /// frozen one. No age bound is applied: stale vs fresh is the number shown.
    /// </summary>
    public static SurroundingsView Inspect(
        ClassifiedValue<IReadOnlyList<SelectableEntity>>? entities,
        DateTime nowUtc)
    {
        if (entities is null || !entities.HasValue)
        {
            string reason = entities?.FailureReason
                ?? GameplayObservation.NotPublishedReason;
            string value = $"{NoObservationLabel} · {reason}";
            return new SurroundingsView
            {
                Kind = SurroundingsKind.NoObservation,
                Summary = value,
                Fields = [new DisplayField("Attorno", $"UNKNOWN · {value}", "UNKNOWN")],
                Rows = Array.Empty<NearbyEntityRow>()
            };
        }

        IReadOnlyList<SelectableEntity> list = entities.Value;
        if (list.Count == 0)
        {
            return new SurroundingsView
            {
                Kind = SurroundingsKind.NoEntitiesAround,
                Summary = NoEntitiesAroundLabel,
                Fields = [new DisplayField("Attorno", $"{NoEntitiesAroundLabel} [DERIVED]", "DERIVED")],
                Rows = Array.Empty<NearbyEntityRow>()
            };
        }

        string source = entities.Source.ToWire();
        var rows = new NearbyEntityRow[list.Count];
        var fields = new DisplayField[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            NearbyEntityRow row = Row(list[i], nowUtc, source);
            rows[i] = row;
            fields[i] = new DisplayField(
                $"Entità {row.EntityId}",
                $"specie={row.Species} vnum={row.Vnum} nome={row.Name} pos={row.Position} vita={row.Life} età={row.Age}",
                source);
        }

        return new SurroundingsView
        {
            Kind = SurroundingsKind.Populated,
            Summary = string.Create(CultureInfo.InvariantCulture, $"{list.Count} osservate"),
            Fields = fields,
            Rows = rows
        };
    }

    /// <summary>Whole seconds since the position was stated, never negative.</summary>
    public static double AgeSeconds(DateTime observedAtUtc, DateTime nowUtc)
        => Math.Max(0, (nowUtc - observedAtUtc).TotalSeconds);

    /// <summary>Operator-facing age. Zero seconds and thirty seconds cannot print equal.</summary>
    public static string AgeLabel(double ageSeconds)
        => string.Create(CultureInfo.InvariantCulture, $"{ageSeconds:0}s");

    private static NearbyEntityRow Row(SelectableEntity entity, DateTime nowUtc, string source)
    {
        double age = AgeSeconds(entity.ObservedAtUtc, nowUtc);
        string vnum = entity.Vnum is { } number
            ? number.ToString(CultureInfo.InvariantCulture)
            : $"UNKNOWN · {VnumNotOnObservation}";

        // Il nome resta UNKNOWN anche con il vnum in mano: risolverlo vuole il
        // catalogo, che questa vista non ha. Il motivo cambia con il caso --
        // "nessuno ha detto cosa sia" e "so cosa e' ma non ho la tabella" sono
        // due ignoranze diverse.
        string name = entity.Vnum is null
            ? $"UNKNOWN · {VnumNotOnObservation}"
            : $"UNKNOWN · {NameNeedsCatalogue}";

        string species = string.IsNullOrEmpty(entity.Kind)
            ? $"UNKNOWN · {SpeciesNotOnObservation}"
            : entity.Kind!;

        string life = entity.HpRatio is { } ratio
            ? string.Create(CultureInfo.InvariantCulture, $"{ratio * 100:0.#}%")
            : $"UNKNOWN · {HpNotStated}";
        return new NearbyEntityRow(
            entity.EntityId,
            vnum,
            name,
            string.Create(CultureInfo.InvariantCulture, $"{entity.At.X},{entity.At.Y}"),
            life,
            AgeLabel(age),
            age,
            source,
            species);
    }
}
