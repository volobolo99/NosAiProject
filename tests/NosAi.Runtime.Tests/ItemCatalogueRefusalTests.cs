using System.Text;
using NosAi.Runtime.GameData;
using Xunit;
using EquipmentSlot = NosAi.Core.WorldModel.EquipmentSlot;

namespace NosAi.Runtime.Tests;

/// <summary>
/// I rifiuti e le promesse di <see cref="ItemCatalogue"/>, ognuno provato, sul
/// modello di <see cref="SkillCatalogueRefusalTests"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Da cosa nasce.</b> <see cref="ItemCatalogue"/> dichiara tre costanti
/// <c>…Reason</c> nuove, e <see cref="RefusalReasonRegisterTests"/> ne chiede
/// una prova o una dichiarazione motivata. Sono raggiungibili — quindi vanno
/// provate, non dichiarate.
/// </para>
/// </remarks>
public sealed class ItemCatalogueRefusalTests
{
    private static GameReferenceDatabase WithItem(int vnum, params (string Name, string[] Values)[] rows)
    {
        GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        var fields = rows.Select(r => new NosField(r.Name, r.Values)).ToList();
        db.Import("item", "test.NOS", "Item.dat", "C:/test", [new NosRecord(vnum, fields)],
            Encoding.UTF8.GetBytes($"payload-{Guid.NewGuid()}"));
        return db;
    }

    [Fact]
    public void Un_vnum_che_il_catalogo_non_ha_e_rifiutato_col_proprio_motivo()
    {
        using GameReferenceDatabase db = WithItem(8, ("VNUM", ["8"]));

        ItemCatalogueLookup lookup = ItemCatalogue.Build(db, vnum: 999999);

        Assert.False(lookup.Ok);
        Assert.Equal(ItemCatalogue.ItemNotInCatalogueReason, lookup.FailureReason);
        // Il motivo non basta: chi chiede non deve ricevere anche mezzo oggetto.
        Assert.Null(lookup.Item);
    }

    /// <summary>
    /// <c>item_undecodable</c> non può accadere, e questo test è ciò che lo
    /// dimostra invece di lasciarlo credere — lo stesso ragionamento di
    /// <see cref="SkillCatalogueRefusalTests"/>.
    /// </summary>
    [Fact]
    public void Una_riga_presente_si_decodifica_sempre_anche_senza_campi()
    {
        using GameReferenceDatabase db = WithItem(8, ("NAME", ["zts8e"]));

        ItemCatalogueLookup lookup = ItemCatalogue.Build(db, vnum: 8);

        Assert.True(lookup.Ok, lookup.FailureReason);
        Assert.NotEqual(ItemCatalogue.ItemUndecodableReason, lookup.FailureReason);

        // Nessun campo presente non significa campi a zero: ognuno dichiara la
        // propria assenza.
        Assert.False(lookup.Item!.Price.HasValue);
        Assert.False(lookup.Item.Slot.HasValue);
    }

    /// <summary>
    /// I campi numerici restano provvisori, e lo dicono — è il test che vale
    /// l'intero contratto.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Il filo porta il vnum dell'oggetto e nient'altro: nessun prezzo, nessun
    /// tipo, nessuno slot. Un campo numerico letto da <c>Item.dat</c> e
    /// pubblicato come confermato avrebbe l'aspetto di un dato verificato
    /// quando nessuna osservazione lo ha mai toccato. Questo test esiste perché
    /// nessuno lo trasformi in un numero: chi farà la misura futura lo legge
    /// grezzo in <see cref="CataloguedItem.Raw"/>.
    /// </para>
    /// </remarks>
    [Fact]
    public void I_campi_numerici_restano_provvisori_col_proprio_motivo()
    {
        using GameReferenceDatabase db = WithItem(8,
            ("VNUM", ["8", "70"]),
            ("INDEX", ["0", "0", "5", "5", "8", "8"]));

        ItemCatalogueLookup lookup = ItemCatalogue.Build(db, vnum: 8);

        Assert.True(lookup.Ok, lookup.FailureReason);

        // Tutti restano sconosciuti, non uno solo: finché nessuna misura
        // conferma un significato, dichiararne uno «il tipo» o «il prezzo»
        // sarebbe già aver scelto.
        Assert.False(lookup.Item!.Price.HasValue);
        Assert.False(lookup.Item.ItemType.HasValue);
        Assert.False(lookup.Item.InventoryType.HasValue);
        Assert.False(lookup.Item.ItemSubType.HasValue);
        Assert.False(lookup.Item.Slot.HasValue);
        Assert.Equal(ItemCatalogue.ItemFieldsNotOnWireReason, lookup.Item.Price.FailureReason);
        Assert.Equal(ItemCatalogue.ItemFieldsNotOnWireReason, lookup.Item.Slot.FailureReason);

        // I numeri restano però leggibili grezzi: chi farà la misura deve
        // poterli confrontare senza riaprire il file del client.
        Assert.Equal(70, lookup.Item.Raw.Price);
        Assert.Equal(EquipmentSlot.SecondaryWeapon, lookup.Item.Raw.Slot);
    }
}
