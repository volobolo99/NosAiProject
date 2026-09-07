using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Un astante non diventa un bersaglio perché il catalogo non sa distinguerlo.
/// </summary>
/// <remarks>
/// <para>
/// <b>Da cosa nasce.</b> Il 2026-09-08 il layout delle entità di tipo 2 è stato
/// stabilito — 2688 passi di <c>mv</c> con la stessa mediana e lo stesso
/// novantesimo percentile di quelli dei mostri, su <c>data/messaggi.noscap</c> —
/// e con esso è arrivata la ragione per cui restavano fuori: quelle entità sono
/// un varco («Caverna dei Conigli»), due NPC («Pir», «Graham») e un pet
/// («Baby^panda»).
/// </para>
/// <para>
/// <b>E il catalogo non le distingue.</b> Tutte e quattro stanno nella tabella
/// <c>monster</c>, e i loro RaceType sono 0, 3, 2 e 3: nessuno è l'8 che
/// <c>MonsterReference.IsSpecialNonMonsterEntity</c> cerca per scartare gli
/// «Special NPCs». <see cref="TargetEstablishment.ClassifyByCatalogue"/> le
/// chiamerebbe <see cref="CatalogueClass.Monster"/>. L'unico discriminante
/// misurato è la specie letta sul filo, ed è quella che questi test verificano
/// arrivi fino alla decisione.
/// </para>
/// </remarks>
public sealed class BystanderTargetingTests
{
    private static readonly DateTime Now = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private static SelectableEntity Bystander(long id = 2328703, int? vnum = 1488) =>
        new(id, new MapPoint(105, 100), 1.0, Now, vnum, Vitals: null,
            Kind: EntitySighting.BystanderKind);

    private static SelectableEntity Monster(long id = 313816, int? vnum = 36) =>
        new(id, new MapPoint(105, 100), 1.0, Now, vnum, Vitals: null,
            Kind: EntitySighting.MonsterKind);

    /// <summary>Il test che dice il perché di tutto il resto.</summary>
    [Fact]
    public void Un_astante_non_e_un_bersaglio_stabilito()
    {
        TargetVerdict verdict = TargetEstablishment.Assess(
            Bystander(), hitBy: null, selected: null, catalogue: null);

        Assert.False(verdict.IsEstablished);
        Assert.Equal(TargetEstablishment.BystanderReason, verdict.Reason);
    }

    /// <summary>
    /// L'evidenza batte la classificazione: se un astante ci ha colpito, quello
    /// che è successo pesa più di come è etichettato.
    /// </summary>
    /// <remarks>
    /// È la stessa gerarchia che <see cref="TargetEstablishment.Assess"/> applica
    /// già al vnum mai osservato, e il motivo per cui il controllo della specie
    /// sta <i>dopo</i> le due prove per evidenza e non prima. Un'entità che
    /// attacca non è un astante, comunque il filo l'abbia etichettata.
    /// </remarks>
    [Fact]
    public void Un_astante_che_ci_ha_colpito_resta_stabilito()
    {
        TargetVerdict verdict = TargetEstablishment.Assess(
            Bystander(),
            ClassifiedValue<Aggressor>.Live(new Aggressor(2328703, 2), Now),
            selected: null,
            catalogue: null);

        Assert.True(verdict.IsEstablished);
        Assert.Equal(TargetEvidence.AttackedUs, verdict.Evidence);
    }

    [Fact]
    public void Un_astante_su_cui_abbiamo_gia_agito_resta_stabilito()
    {
        TargetVerdict verdict = TargetEstablishment.Assess(
            Bystander(),
            hitBy: null,
            ClassifiedValue<TargetedEntity>.Live(new TargetedEntity(2328703, 2), Now),
            catalogue: null);

        Assert.True(verdict.IsEstablished);
        Assert.Equal(TargetEvidence.WeActedOnIt, verdict.Evidence);
    }

    /// <summary>
    /// Un mostro non cambia comportamento: senza catalogo si ferma dove si fermava
    /// prima, e non per la specie.
    /// </summary>
    [Fact]
    public void Un_mostro_non_e_toccato_dal_controllo_della_specie()
    {
        TargetVerdict verdict = TargetEstablishment.Assess(
            Monster(), hitBy: null, selected: null, catalogue: null);

        Assert.False(verdict.IsEstablished);
        Assert.NotEqual(TargetEstablishment.BystanderReason, verdict.Reason);
    }

    /// <summary>
    /// Specie non dichiarata non è «astante», e non è nemmeno «mostro»: la
    /// decisione torna alle regole di prima.
    /// </summary>
    /// <remarks>
    /// La stessa regola del vnum nullo: un'entità che arriva da una sorgente che
    /// la specie non la dichiara non viene classificata per omissione. Il pannello
    /// costruisce le proprie <see cref="SelectableEntity"/> dallo snapshot HTTP,
    /// che la specie non la porta, e non deve per questo vedere tutto come
    /// attaccabile né niente come astante.
    /// </remarks>
    [Fact]
    public void Specie_non_dichiarata_non_e_astante()
    {
        var unknownSpecies = new SelectableEntity(
            313816, new MapPoint(105, 100), 1.0, Now, Vnum: 36);

        TargetVerdict verdict = TargetEstablishment.Assess(
            unknownSpecies, hitBy: null, selected: null, catalogue: null);

        Assert.NotEqual(TargetEstablishment.BystanderReason, verdict.Reason);
    }
}
