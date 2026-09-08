using System.Linq;
using NosAi.ControlPanel;
using Xunit;

namespace NosAi.ControlPanel.Tests;

/// <summary>
/// La card T-12 legge la timeline di una registrazione vera. La fixture è
/// l'output letterale di
/// <c>--wire-inspect data/equip_test_20260908_131656.noscap --timeline equip,ivn</c>
/// sulla cattura del 2026-09-08 (equipaggia, togli, togli, rimetti): quattro
/// pacchetti <c>equip</c> e quattro <c>ivn</c>, tutti e quattro di tipo 0.
/// </summary>
public sealed class EquipWireInspectTests
{
    private const string RealTimeline = """
#1078 2026-09-08T11:17:04.6856861Z equip 0 0 2.221.0.0.0.0.0 4.715.0.2.0.0.0 9.224.0.0.0.0.0 10.279.0.0.0.0.0 11.284.0.0.0.0.0 12.902.0.5.0.0.0
#1079 2026-09-08T11:17:04.6856861Z ivn 0 18.0.0.0.0.0.0
#1820 2026-09-08T11:17:11.0856396Z ivn 0 18.715.0.2.0.0.0
#1821 2026-09-08T11:17:11.0856396Z equip 0 0 2.221.0.0.0.0.0 9.224.0.0.0.0.0 10.279.0.0.0.0.0 11.284.0.0.0.0.0 12.902.0.5.0.0.0
#2547 2026-09-08T11:17:16.9858907Z ivn 0 21.902.0.5.0.0.0
#2548 2026-09-08T11:17:16.9858907Z equip 0 0 2.221.0.0.0.0.0 9.224.0.0.0.0.0 10.279.0.0.0.0.0 11.284.0.0.0.0.0
#3261 2026-09-08T11:17:23.2858443Z equip 0 0 2.221.0.0.0.0.0 9.224.0.0.0.0.0 10.279.0.0.0.0.0 11.284.0.0.0.0.0 12.902.0.5.0.0.0
#3262 2026-09-08T11:17:23.2858443Z ivn 0 21.0.0.0.0.0.0
""";

    [Fact]
    public void OgniPacchettoDellaTimelineDiventaUnaRiga()
    {
        EquipWireReading reading = EquipWireInspect.Read(RealTimeline);

        Assert.Equal(8, reading.Lines.Count);
        Assert.Equal(4, reading.Lines.Count(l => l.Opcode == EquipWireInspect.EquipOpcode));
        Assert.Equal(4, reading.Lines.Count(l => l.Opcode == EquipWireInspect.InventoryOpcode));
        Assert.Equal(1078, reading.Lines[0].Sequence);
    }

    /// <summary>
    /// Il fatto che chiude la seconda metà di T-12: su una registrazione che
    /// contiene sia un equip sia due unequip, <c>ivn</c> non cambia mai tipo.
    /// </summary>
    [Fact]
    public void SuUnEquipRealeIvnUsaUnSoloTipo()
    {
        EquipWireReading reading = EquipWireInspect.Read(RealTimeline);

        Assert.Equal(new[] { 0 }, reading.InventoryKinds);
        Assert.Contains("uno solo", reading.Summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// Lo stato indossato cambia in <c>equip</c>: 715 esce dallo slot 4, 902
    /// esce dallo slot 12 e vi rientra. Tre transizioni, non quattro: il primo
    /// pacchetto non ha un precedente con cui confrontarsi.
    /// </summary>
    [Fact]
    public void LeTransizioniDiEquipDiconoQualePezzoEUscitoEQualeERientrato()
    {
        EquipWireReading reading = EquipWireInspect.Read(RealTimeline);

        Assert.Equal(3, reading.Transitions.Count);

        Assert.Equal(new EquippedPiece(4, 715), Assert.Single(reading.Transitions[0].Removed));
        Assert.Empty(reading.Transitions[0].Added);

        Assert.Equal(new EquippedPiece(12, 902), Assert.Single(reading.Transitions[1].Removed));
        Assert.Empty(reading.Transitions[1].Added);

        Assert.Equal(new EquippedPiece(12, 902), Assert.Single(reading.Transitions[2].Added));
        Assert.Empty(reading.Transitions[2].Removed);
    }

    [Fact]
    public void IDueCampiInizialiDiEquipNonSonoPezzi()
    {
        EquipWireReading reading = EquipWireInspect.Read(RealTimeline);

        // «0 0» aprono ogni equip e non portano punto: se venissero contati come
        // pezzi, ogni pacchetto ne dichiarerebbe due in piu' e nessuna
        // transizione tornerebbe.
        Assert.DoesNotContain(reading.Transitions.SelectMany(t => t.Removed.Concat(t.Added)), p => p.Vnum == 0);
    }

    [Fact]
    public void UnaRegistrazioneSenzaQueiPacchettiLoDiceInveceDiTacere()
    {
        EquipWireReading reading = EquipWireInspect.Read("nessun pacchetto\ntotale: 0\n");

        Assert.Empty(reading.Lines);
        Assert.Equal(EquipWireInspect.NothingObservedSummary, reading.Summary);
    }

    [Fact]
    public void UnOutputVuotoNonEUnEccezione()
    {
        Assert.Equal(EquipWireInspect.NothingObservedSummary, EquipWireInspect.Read(null).Summary);
        Assert.Equal(EquipWireInspect.NothingObservedSummary, EquipWireInspect.Read("").Summary);
    }
}
