namespace NosAi.Core.WorldModel;

/// <summary>
/// Classe di origine di un fatto del World Model. Allineata 1:1, per nome e per
/// ordinale, a <c>NosAi.Runtime.Contracts.DataSourceKind</c> in modo che la fusione
/// (A2) possa convertire senza tabella di mappatura. <see cref="Unknown"/> non è
/// zero, false o vuoto: è l'assenza dichiarata di un valore.
/// </summary>
public enum FactSourceKind : byte
{
    /// <summary>Letto dal client in questo ciclo (rete, memoria, schermo, telemetria locale).</summary>
    Live = 0,

    /// <summary>Calcolato deterministicamente da fatti LIVE/CACHED (es. velocità da due posizioni).</summary>
    Derived = 1,

    /// <summary>Osservazione reale precedente ripubblicata senza nuova lettura.</summary>
    Cached = 2,

    /// <summary>Prodotto da simulazione o predizione. Si può pianificare, mai agire.</summary>
    Simulated = 3,

    /// <summary>Nessun valore. Il motivo è in <see cref="Fact{T}.FailureReason"/>.</summary>
    Unknown = 4
}

/// <summary>
/// Canale fisico che ha prodotto l'osservazione. Allineato a
/// <c>NosAi.Core.Memory.MemoryProvenance</c> e <c>NosAi.Core.Testing.ObservationSource</c>.
/// </summary>
public enum ObservationChannel : byte
{
    Network = 0,
    Memory = 1,
    Screen = 2,
    Local = 3,
    Operator = 4,
    Unknown = 5
}

/// <summary>Provenienza completa di un fatto: classe di origine + canale + sensore.</summary>
/// <param name="Kind">Classe di origine (LIVE/DERIVED/CACHED/SIMULATED/UNKNOWN).</param>
/// <param name="Channel">Canale fisico. Per DERIVED è il canale dominante dell'input; per SIMULATED è <see cref="ObservationChannel.Unknown"/>.</param>
/// <param name="SensorId">
/// Identificatore stabile del sensore/adapter che ha prodotto il fatto (0 = non
/// dichiarato). È un numero e non una stringa per restare a zero allocazioni sul
/// percorso critico; la tabella nomi vive nel registro sensori del runtime.
/// </param>
public readonly record struct FactProvenance(FactSourceKind Kind, ObservationChannel Channel, ushort SensorId)
{
    public static FactProvenance Live(ObservationChannel channel, ushort sensorId = 0) => new(FactSourceKind.Live, channel, sensorId);
    public static FactProvenance Derived(ObservationChannel channel, ushort sensorId = 0) => new(FactSourceKind.Derived, channel, sensorId);
    public static FactProvenance Cached(ObservationChannel channel, ushort sensorId = 0) => new(FactSourceKind.Cached, channel, sensorId);
    public static FactProvenance Simulated(ushort sensorId = 0) => new(FactSourceKind.Simulated, ObservationChannel.Unknown, sensorId);
    public static FactProvenance Unknown => new(FactSourceKind.Unknown, ObservationChannel.Unknown, 0);

    /// <summary>Vero per LIVE, DERIVED e CACHED: dati reali, su cui il runtime può decidere di agire se freschi.</summary>
    public bool IsReal => Kind is FactSourceKind.Live or FactSourceKind.Derived or FactSourceKind.Cached;

    public bool IsSimulated => Kind == FactSourceKind.Simulated;

    public bool IsUnknown => Kind == FactSourceKind.Unknown;
}

/// <summary>Testo di wire delle classi di origine, identico a quello del runtime.</summary>
public static class FactSourceKindText
{
    public static string ToWire(this FactSourceKind kind) => kind switch
    {
        FactSourceKind.Live => "LIVE",
        FactSourceKind.Derived => "DERIVED",
        FactSourceKind.Cached => "CACHED",
        FactSourceKind.Simulated => "SIMULATED",
        _ => "UNKNOWN"
    };

    public static bool TryParseWire(ReadOnlySpan<char> text, out FactSourceKind kind)
    {
        if (text.Equals("LIVE", StringComparison.Ordinal)) { kind = FactSourceKind.Live; return true; }
        if (text.Equals("DERIVED", StringComparison.Ordinal)) { kind = FactSourceKind.Derived; return true; }
        if (text.Equals("CACHED", StringComparison.Ordinal)) { kind = FactSourceKind.Cached; return true; }
        if (text.Equals("SIMULATED", StringComparison.Ordinal)) { kind = FactSourceKind.Simulated; return true; }
        if (text.Equals("UNKNOWN", StringComparison.Ordinal)) { kind = FactSourceKind.Unknown; return true; }
        kind = FactSourceKind.Unknown;
        return false;
    }
}
