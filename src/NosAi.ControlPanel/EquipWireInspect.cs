using System.Globalization;
using System.Linq;

namespace NosAi.ControlPanel;

/// <summary>Una riga della timeline di <c>--wire-inspect</c>, già divisa nei suoi pezzi.</summary>
internal sealed record EquipWireLine(int Sequence, string Timestamp, string Opcode, IReadOnlyList<string> Fields)
{
    /// <summary>I campi come il filo li ha scritti, senza numero di sequenza né istante.</summary>
    public string Payload => string.Join(' ', Fields);
}

/// <summary>Un pezzo indossato, come il pacchetto <c>equip</c> lo scrive: slot e vnum.</summary>
internal readonly record struct EquippedPiece(int Slot, int Vnum)
{
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"slot {Slot} (vnum {Vnum})");
}

/// <summary>Cosa è cambiato fra due <c>equip</c> consecutivi.</summary>
internal sealed record EquipTransition(
    int Sequence,
    IReadOnlyList<EquippedPiece> Removed,
    IReadOnlyList<EquippedPiece> Added);

/// <summary>Cosa la registrazione ha osservato attorno a un equip/unequip.</summary>
internal sealed record EquipWireReading(
    IReadOnlyList<EquipWireLine> Lines,
    IReadOnlyList<int> InventoryKinds,
    IReadOnlyList<EquipTransition> Transitions,
    string Summary);

/// <summary>
/// Legge la timeline di <c>--wire-inspect --timeline equip,ivn</c> e ne mostra i
/// due lati: quali tipi di inventario il filo ha usato in <c>ivn</c>, e come è
/// cambiata la composizione di <c>equip</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Perché la timeline e non le sole righe inventario.</b> Fino al 2026-09-08
/// questa card rileggeva la registrazione con <c>--world-replay</c> e mostrava
/// solo le righe che contenevano <c>kind=</c>, cioè i soli <c>ivn</c>. Su una
/// registrazione reale (<c>data/equip_test_20260908_131656.noscap</c>) quel
/// filtro non poteva rispondere alla domanda di T-12: <c>ivn</c> porta sempre
/// <c>kind 0</c>, sia quando l'oggetto esce dalla borsa per essere indossato sia
/// quando vi rientra, e lo stato indossato viaggia invece nel pacchetto
/// <c>equip</c>, che <c>--world-replay</c> non stampa affatto. La card mostrava
/// la metà del filo che non contiene la risposta.
/// </para>
/// <para>
/// Questa classe non conclude nulla: elenca i fatti osservati (i tipi visti, i
/// pezzi comparsi e scomparsi) e lascia la lettura all'operatore, come la
/// coppia testo/id di <see cref="WireMessageInspect"/>.
/// </para>
/// </remarks>
internal static class EquipWireInspect
{
    /// <summary>Gli opcode che la card chiede alla timeline.</summary>
    public const string Opcodes = "equip,ivn";

    public const string EquipOpcode = "equip";
    public const string InventoryOpcode = "ivn";

    /// <summary>Quando la registrazione non contiene nessuno dei due pacchetti.</summary>
    public const string NothingObservedSummary =
        "Nessun pacchetto equip o ivn nella registrazione: l'azione non è stata osservata. Ripeti la registrazione.";

    public static EquipWireReading Read(string? timelineOutput)
    {
        var lines = Parse(timelineOutput).ToArray();
        if (lines.Length == 0)
            return new EquipWireReading(lines, Array.Empty<int>(), Array.Empty<EquipTransition>(), NothingObservedSummary);

        int[] kinds = lines
            .Where(l => string.Equals(l.Opcode, InventoryOpcode, StringComparison.Ordinal))
            .Select(InventoryKindOf)
            .Where(k => k >= 0)
            .Distinct()
            .OrderBy(k => k)
            .ToArray();

        EquipTransition[] transitions = Transitions(lines).ToArray();
        return new EquipWireReading(lines, kinds, transitions, Describe(lines, kinds, transitions));
    }

    /// <summary>
    /// Le righe che <c>--wire-inspect --timeline</c> stampa hanno la forma
    /// <c>#1078 2026-09-08T11:17:04Z equip 0 0 2.221.0.0.0.0.0 …</c>. Tutto ciò
    /// che non comincia con <c>#</c> è intestazione o riepilogo, e non è un
    /// pacchetto.
    /// </summary>
    private static IEnumerable<EquipWireLine> Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            yield break;

        foreach (string raw in output.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length < 2 || line[0] != '#')
                continue;

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
                continue;

            if (!int.TryParse(parts[0].AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int sequence))
                continue;

            yield return new EquipWireLine(sequence, parts[1], parts[2], parts[3..]);
        }
    }

    /// <summary>Il primo campo di <c>ivn</c> è il tipo di inventario; -1 quando non è un numero.</summary>
    private static int InventoryKindOf(EquipWireLine line) =>
        line.Fields.Count > 0
        && int.TryParse(line.Fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int kind)
            ? kind
            : -1;

    /// <summary>
    /// I pezzi di un <c>equip</c>: i campi puntati <c>slot.vnum.…</c>. I due
    /// campi iniziali (<c>0 0</c>) non sono pezzi e non contengono punti.
    /// </summary>
    private static IReadOnlyList<EquippedPiece> PiecesOf(EquipWireLine line)
    {
        var pieces = new List<EquippedPiece>();
        foreach (string field in line.Fields)
        {
            string[] parts = field.Split('.');
            if (parts.Length < 2)
                continue;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot))
                continue;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int vnum))
                continue;
            pieces.Add(new EquippedPiece(slot, vnum));
        }

        return pieces;
    }

    private static IEnumerable<EquipTransition> Transitions(IReadOnlyList<EquipWireLine> lines)
    {
        IReadOnlyList<EquippedPiece>? previous = null;
        foreach (EquipWireLine line in lines.Where(l => string.Equals(l.Opcode, EquipOpcode, StringComparison.Ordinal)))
        {
            IReadOnlyList<EquippedPiece> current = PiecesOf(line);
            if (previous is not null)
            {
                EquippedPiece[] removed = previous.Where(p => !current.Contains(p)).ToArray();
                EquippedPiece[] added = current.Where(p => !previous.Contains(p)).ToArray();
                if (removed.Length > 0 || added.Length > 0)
                    yield return new EquipTransition(line.Sequence, removed, added);
            }

            previous = current;
        }
    }

    private static string Describe(
        IReadOnlyList<EquipWireLine> lines,
        IReadOnlyList<int> kinds,
        IReadOnlyList<EquipTransition> transitions)
    {
        int equipCount = lines.Count(l => string.Equals(l.Opcode, EquipOpcode, StringComparison.Ordinal));
        int inventoryCount = lines.Count(l => string.Equals(l.Opcode, InventoryOpcode, StringComparison.Ordinal));

        string kindText = kinds.Count == 0
            ? "nessun ivn leggibile"
            : string.Create(CultureInfo.InvariantCulture,
                $"tipi visti in ivn: {string.Join(", ", kinds)}{(kinds.Count == 1 ? " (uno solo)" : "")}");

        string changeText = transitions.Count == 0
            ? "nessun cambio di equipaggiamento fra un equip e il successivo"
            : string.Join("; ", transitions.Select(DescribeTransition));

        return string.Create(CultureInfo.InvariantCulture,
            $"{equipCount} pacchetti equip, {inventoryCount} ivn. {kindText}. Cambi: {changeText}.");
    }

    private static string DescribeTransition(EquipTransition transition)
    {
        var parts = new List<string>(2);
        if (transition.Removed.Count > 0)
            parts.Add($"tolto {string.Join(" e ", transition.Removed)}");
        if (transition.Added.Count > 0)
            parts.Add($"messo {string.Join(" e ", transition.Added)}");
        return string.Create(CultureInfo.InvariantCulture, $"#{transition.Sequence} {string.Join(", ", parts)}");
    }
}
