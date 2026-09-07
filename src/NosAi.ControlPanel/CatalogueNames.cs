using System.IO;
using NosAi.Runtime.GameData;

namespace NosAi.ControlPanel;

/// <summary>
/// Risolve un vnum nel nome che il catalogo del client gli dà, tenendo il
/// catalogo aperto una volta sola.
/// </summary>
/// <remarks>
/// <para>
/// <b>Perché esiste.</b> Fino al 2026-09-08 la vista «Attorno» scriveva
/// <c>name_needs_reference_catalogue</c> per <i>ogni</i> entità, con il commento
/// «il catalogo, che questa vista non ha». Il catalogo era sul disco, pieno
/// (2705 mostri, 98,2% con un nome risolvibile) e un'altra classe dello stesso
/// pannello lo apriva già. Il motivo mostrato all'operatore era falso.
/// </para>
/// <para>
/// <b>Perché una classe e non due righe.</b> Il catalogo è un file da 168 MB e
/// il pannello si aggiorna in continuazione: aprirlo a ogni giro sarebbe un
/// costo per ogni entità mostrata. Qui si apre alla prima richiesta e si ricorda
/// ogni risposta, comprese quelle negative — «questo vnum non c'è» è una risposta
/// e non va richiesta due volte.
/// </para>
/// <para>
/// <b>Il catalogo assente non è un errore.</b> Se non c'è, ogni risoluzione dà
/// null e chi chiama scrive il proprio motivo: la vista continua a funzionare
/// senza nomi, come faceva prima.
/// </para>
/// </remarks>
internal sealed class CatalogueNames : IDisposable
{
    /// <summary>La lingua il cui testo è importato nel catalogo.</summary>
    private const string Language = "IT";

    /// <summary>
    /// La tabella dove stanno le entità del mondo — mostri, NPC, varchi e pet.
    /// </summary>
    /// <remarks>
    /// Anche gli astanti stanno qui: le quattro entità di tipo 2 osservate il
    /// 2026-09-08 (un varco, due NPC e un pet) sono tutte righe di <c>monster</c>,
    /// ed è precisamente il motivo per cui il catalogo non basta a distinguerle e
    /// la specie va letta sul filo.
    /// </remarks>
    private const string EntityTable = "monster";

    private readonly Dictionary<int, string?> _memo = new();
    private GameReferenceDatabase? _catalogue;
    private bool _tried;
    private bool _disposed;

    /// <summary>Il nome del vnum, o null quando il catalogo manca o non ce l'ha.</summary>
    public string? Of(int vnum)
    {
        if (_disposed)
            return null;
        if (_memo.TryGetValue(vnum, out string? remembered))
            return remembered;

        string? name = Resolve(vnum);
        _memo[vnum] = name;
        return name;
    }

    private string? Resolve(int vnum)
    {
        if (!_tried)
        {
            _tried = true;
            GameReferenceLocation location = GameReferenceLocator.Locate();
            if (location.Exists && location.Path is { } path)
            {
                try
                {
                    _catalogue = GameReferenceDatabase.OpenExisting(path);
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException)
                {
                    // Un catalogo che non si apre e un catalogo assente danno la
                    // stessa risposta a chi chiama -- nessun nome -- e nessuno dei
                    // due e' un motivo per far cadere il pannello.
                    _catalogue = null;
                }
            }
        }

        return _catalogue?.DisplayName(EntityTable, vnum, Language);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _catalogue?.Dispose();
        _catalogue = null;
    }
}
