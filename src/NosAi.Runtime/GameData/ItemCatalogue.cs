using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.GameData;

/// <summary>
/// One item from the reference catalogue, with each field marked by whether its
/// in-game meaning has been cross-checked against the wire (AP-05/A2+A4) or is
/// still a provisional read of the client's own table.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ItemReferenceDecoder"/> reads <c>Item.dat</c>'s tags; the positions
/// come from a community source and are confirmed only for the enum mapping
/// behind <see cref="ItemReference.Slot"/> (three independent sources — see that
/// type's own remarks). This type is where the wire/non-wire distinction is made
/// structural: the wire carries the item's <b>vnum</b> — in <c>drop</c>, in
/// <c>ivn</c>, and in <c>sayi</c> field 6 when field 5 is 2 — and nothing else.
/// It never carries a price, a type or a slot, so those fields are published as
/// <see cref="ClassifiedValue{T}.Unknown"/> with a named reason rather than
/// being readable as if the wire had confirmed them.
/// </para>
/// <para>
/// What the wire does confirm is the item's <b>identity</b>: the same vnum named
/// by <c>drop</c>, <c>sayi</c> and <c>ivn</c> of one capture resolves to the same
/// display name. That confirmation lives in <see cref="Vnum"/> plus
/// <see cref="Name"/>, not in any numeric field.
/// </para>
/// </remarks>
public sealed record CataloguedItem
{
    /// <summary>The item's vnum, the key the wire carries (<c>drop</c> field 1, <c>ivn</c> dotted group, <c>sayi</c> field 6).</summary>
    public required int Vnum { get; init; }

    /// <summary>The client's own display name, or null when the language table has none.</summary>
    public string? Name { get; init; }

    /// <summary><c>VNUM[1]</c>. Provisional: the wire never carries an item's price.</summary>
    public required ClassifiedValue<int> Price { get; init; }

    /// <summary><c>INDEX[0]</c>. Provisional: the wire never carries an inventory type.</summary>
    public required ClassifiedValue<int> InventoryType { get; init; }

    /// <summary><c>INDEX[1]</c>. Provisional: the wire never carries an item type.</summary>
    public required ClassifiedValue<int> ItemType { get; init; }

    /// <summary><c>INDEX[2]</c>. Provisional: the wire never carries an item sub-type.</summary>
    public required ClassifiedValue<int> ItemSubType { get; init; }

    /// <summary>
    /// <c>INDEX[3]</c>, the raw equipment-slot code. Provisional: the enum mapping
    /// behind it is confirmed by external sources, but no wire observation
    /// confirms which slot an item actually occupies. The typed value, when the
    /// raw code is defined, is <see cref="ItemReference.Slot"/> on
    /// <see cref="Raw"/>.
    /// </summary>
    public required ClassifiedValue<int> Slot { get; init; }

    /// <summary>The full raw decode, for a report that needs the uninterpreted catalogue.</summary>
    public required ItemReference Raw { get; init; }
}

/// <summary>An item lookup: the item, or the named reason there is none.</summary>
/// <remarks>
/// <c>Ok</c> false is never an empty item. It is a distinct state — the vnum is
/// not in the catalogue, or the record is there but undecodable — and the reason
/// says which, the same way <see cref="GameReferenceLocator"/> distinguishes a
/// missing volume from a missing file.
/// </remarks>
public sealed record ItemCatalogueLookup(CataloguedItem? Item, string? FailureReason)
{
    public bool Ok => Item is not null;

    public static ItemCatalogueLookup NotFound(string reason) => new(null, reason);
}

/// <summary>
/// Reads the reference catalogue and returns one item with the
/// confirmed/provisional split of <see cref="CataloguedItem"/>.
/// </summary>
/// <remarks>
/// Read-only: it never writes, migrates or imports. The catalogue itself is opened
/// by the caller through <see cref="GameReferenceLocator"/>, so the "no catalogue
/// at all" state is reported before this type is ever handed a database — a missing
/// catalogue is not an empty one, and no lookup here pretends otherwise.
/// </remarks>
public sealed class ItemCatalogue
{
    /// <summary>The vnum is not in the imported <c>item</c> table.</summary>
    public const string ItemNotInCatalogueReason = "item_not_in_catalogue";

    /// <summary>
    /// The record exists but <see cref="ItemReferenceDecoder"/> could not decode
    /// it. Unreachable today — a present row always carries a vnum — and kept so a
    /// future shape change cannot hand a half-built item to a caller unnoticed.
    /// </summary>
    public const string ItemUndecodableReason = "item_undecodable";

    /// <summary>
    /// Shared by every numeric field of <see cref="CataloguedItem"/>: the wire
    /// carries the item's vnum, never its statistics, so the in-game meaning of
    /// <c>VNUM[1]</c>, <c>INDEX[0..3]</c> and the rest stays a provisional read
    /// of the client's own <c>Item.dat</c>.
    /// </summary>
    public const string ItemFieldsNotOnWireReason = "item_fields_not_carried_by_wire";

    private readonly GameReferenceDatabase _database;
    private readonly string _language;

    public ItemCatalogue(GameReferenceDatabase database, string language = "IT")
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _language = language;
    }

    /// <summary>Looks up one vnum and splits its fields into confirmed and provisional.</summary>
    public ItemCatalogueLookup Lookup(int vnum) => Build(_database, vnum, _language);

    /// <summary>
    /// The pure build, separated so a test can point it at an in-memory database
    /// without touching a drive letter.
    /// </summary>
    public static ItemCatalogueLookup Build(GameReferenceDatabase database, int vnum, string language = "IT")
    {
        ArgumentNullException.ThrowIfNull(database);

        IReadOnlyList<NosField>? fields = database.Lookup("item", vnum);
        if (fields is null)
            return ItemCatalogueLookup.NotFound(ItemNotInCatalogueReason);

        ItemReference? item = ItemReferenceDecoder.Decode(new NosRecord(vnum, fields));
        // Decode returns null only for a record with no VNUM; this vnum came from
        // the database's own (kind, vnum) key, so the branch is unreachable today.
        // The check stays so a future shape change cannot hand a half-built item
        // to a caller as if it were a complete one.
        if (item is null)
            return ItemCatalogueLookup.NotFound(ItemUndecodableReason);

        var confirmed = new CataloguedItem
        {
            Vnum = vnum,
            Name = database.DisplayName("item", vnum, language),
            Price = ClassifiedValue<int>.Unknown(ItemFieldsNotOnWireReason),
            InventoryType = ClassifiedValue<int>.Unknown(ItemFieldsNotOnWireReason),
            ItemType = ClassifiedValue<int>.Unknown(ItemFieldsNotOnWireReason),
            ItemSubType = ClassifiedValue<int>.Unknown(ItemFieldsNotOnWireReason),
            Slot = ClassifiedValue<int>.Unknown(ItemFieldsNotOnWireReason),
            Raw = item,
        };
        return new ItemCatalogueLookup(confirmed, null);
    }
}
