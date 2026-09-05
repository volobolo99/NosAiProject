namespace NosAi.Core.WorldModel;

/// <summary>
/// Versione del contratto World Model (AP-01). Ogni <see cref="WorldModelSnapshot"/>
/// dichiara la versione con cui è stato prodotto, così un consumer (fusion, planner,
/// replay, dashboard) può rifiutare uno snapshot incompatibile invece di
/// interpretarlo in modo silenziosamente errato.
/// </summary>
/// <remarks>
/// Regola di compatibilità: stesso <see cref="Major"/> = layout compatibile;
/// <see cref="Minor"/> superiore = campi aggiunti in coda o nuovi valori enum, mai
/// campi rimossi o risemantizzati. Un cambio di significato di un campo esistente
/// richiede un incremento di <see cref="Major"/>.
/// </remarks>
public readonly record struct ContractVersion(ushort Major, ushort Minor) : IComparable<ContractVersion>
{
    /// <summary>Versione corrente dei contratti definiti in questo assembly.</summary>
    public static ContractVersion Current => new(1, 0);

    /// <summary>Versione nulla: uno snapshot che la dichiara non è stato prodotto da un builder conforme.</summary>
    public static ContractVersion None => default;

    /// <summary>Un lettore compilato contro <paramref name="reader"/> può leggere uno snapshot prodotto con questa versione.</summary>
    public bool IsReadableBy(ContractVersion reader)
        => Major != 0 && reader.Major == Major && reader.Minor >= Minor;

    public int CompareTo(ContractVersion other)
    {
        int major = Major.CompareTo(other.Major);
        return major != 0 ? major : Minor.CompareTo(other.Minor);
    }

    public override string ToString() => $"{Major}.{Minor}";
}
