using System.Text;
using NosAi.Runtime.Contracts;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Il campo 3 di <c>st</c> è il livello dell'entità, e lo dice il client stesso.
/// </summary>
/// <remarks>
/// <para>
/// <b>Come è stato stabilito, il 2026-09-08.</b> Il campo era fra i non
/// decodificati. Per ogni entità che una registrazione nomina sia in <c>in</c>
/// — l'unico pacchetto che dà il vnum — sia in <c>st</c>, il valore è stato
/// confrontato con <c>entity.level</c> del catalogo importato dai file del
/// client, per quel vnum: <b>26 confronti su quattro registrazioni, 26 concordi,
/// zero discordi</b>.
/// </para>
/// <para>
/// È un riscontro fra due fonti indipendenti — quello che il server manda sul
/// filo e quello che il client tiene su disco — e non fra il filo e sé stesso.
/// </para>
/// </remarks>
public sealed class EntityLevelFromStTests
{
    private static readonly DateTime Start = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private static ObservedPacket Packet(string line) => new(
        Start, NetworkDirection.Inbound, "127.0.0.1", 4002,
        Encoding.ASCII.GetBytes(line), DataSourceKind.Live);

    /// <summary>
    /// Una riga reale di <c>nostale_combat</c>: l'entità 3205 è il vnum 45, che
    /// il catalogo dà di livello 8, e il filo dice 8.
    /// </summary>
    [Fact]
    public void Il_livello_arriva_sullavvistamento()
    {
        var decoder = new NosTaleWorldProtocolDecoder();

        // Prima la posizione, altrimenti `st` non pubblica: e' la regola che
        // esisteva gia', non una aggiunta di questo test.
        decoder.Decode(Packet("in 3 45 3205 110 62 2 100 100"));
        DecodedObservations decoded = decoder.Decode(
            Packet("st 3 3205 8 0 100 100 310 52 310 52 0"));

        EntitySighting sighting = Assert.Single(decoded.Sightings);
        Assert.Equal(8, sighting.Level);
        Assert.Equal(45, sighting.Vnum);
    }

    /// <summary>
    /// Il livello sopravvive ai <c>mv</c> che seguono: sono la stessa entità, e
    /// nessun pacchetto ha detto il contrario.
    /// </summary>
    [Fact]
    public void Il_livello_sopravvive_al_movimento()
    {
        var decoder = new NosTaleWorldProtocolDecoder();
        decoder.Decode(Packet("in 3 45 3205 110 62 2 100 100"));
        decoder.Decode(Packet("st 3 3205 8 0 100 100 310 52 310 52 0"));

        DecodedObservations moved = decoder.Decode(Packet("mv 3 3205 111 63 5"));

        EntitySighting sighting = Assert.Single(moved.Sightings);
        Assert.Equal(8, sighting.Level);
    }

    /// <summary>
    /// <c>in</c> non porta un livello, e non ne inventa uno.
    /// </summary>
    /// <remarks>
    /// Non è una perdita: <c>in</c> porta il vnum, e il vnum dà il livello
    /// attraverso il catalogo. Quello che questo test fissa è che il campo resti
    /// nullo invece di ereditare un valore da un pacchetto che non l'ha detto.
    /// </remarks>
    [Fact]
    public void Un_in_da_solo_non_dichiara_un_livello()
    {
        var decoder = new NosTaleWorldProtocolDecoder();

        DecodedObservations decoded = decoder.Decode(
            Packet("in 3 45 3205 110 62 2 100 100"));

        EntitySighting sighting = Assert.Single(decoded.Sightings);
        Assert.Null(sighting.Level);
    }

    /// <summary>
    /// Sulle registrazioni vere il livello arriva, e arriva per abbastanza
    /// entità da non essere un caso.
    /// </summary>
    /// <remarks>
    /// Il riscontro contro il catalogo del client — 26 confronti, 26 concordi —
    /// è stato eseguito una volta, il 2026-09-08, e sta nelle <c>remarks</c> di
    /// <see cref="EntitySighting.Level"/>: rifarlo qui richiederebbe il volume
    /// dedicato montato, e un test che salta sulla maggior parte delle macchine
    /// non sorveglia niente. Quello che questo test tiene è che il campo continui
    /// ad arrivare, e per quante entità: il numero è asserito, perché un test che
    /// non ne vede nessuna passerebbe a vuoto.
    /// </remarks>
    /// <remarks>
    /// <b>Perché quindici e nove, e non sedici e tredici.</b> Le entità che
    /// <c>st</c> nomina sono sedici e tredici, ma un <c>st</c> pubblica un
    /// avvistamento solo se la posizione è già nota — regola che esisteva prima di
    /// questo campo. Le mancanti sono entità di cui il filo ha detto la vita e mai
    /// dove fossero: il livello viene comunque ricordato e riemergerebbe al primo
    /// <c>mv</c>, che dentro queste registrazioni non arriva.
    /// </remarks>
    [RecordedCaptureTheory("nostale_combat.noscap", "certificazione.noscap")]
    [InlineData("nostale_combat.noscap", 15)]
    [InlineData("certificazione.noscap", 9)]
    public void Il_livello_arriva_per_le_entita_che_st_nomina(string recording, int expected)
    {
        string path = RecordedCaptureFactAttribute.Resolve(recording)!;
        using IPacketSource packets = CaptureFile.Open(path);
        using var source = ReassembledObservationSource.ForNosTaleWorld(packets, DataSourceKind.Cached);
        var decoder = new NosTaleWorldProtocolDecoder();
        var levelled = new Dictionary<long, int>();

        while (source.TryObserve(out ObservedPacket packet))
        {
            foreach (EntitySighting sighting in decoder.Decode(packet).Sightings)
            {
                if (sighting.Level is { } level)
                    levelled[sighting.EntityId] = level;
            }
        }

        Assert.Equal(expected, levelled.Count);

        // Nessun livello negativo e nessuno oltre il massimo che il gioco usa:
        // un campo letto nel posto sbagliato uscirebbe da questa fascia.
        Assert.All(levelled.Values, level => Assert.InRange(level, 0, 255));
    }
}
