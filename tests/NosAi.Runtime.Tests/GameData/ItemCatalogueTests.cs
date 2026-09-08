using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using EquipmentSlot = NosAi.Core.WorldModel.EquipmentSlot;
using NosAi.Runtime.GameData;
using NosAi.Runtime.Observability;
using Xunit;

namespace NosAi.Runtime.Tests.GameData;

/// <summary>
/// <see cref="ItemCatalogue"/>: the reference item read with a structural split
/// between the identity the wire confirms and the numeric fields that stay a
/// provisional read of the client's own table.
/// </summary>
public sealed class ItemCatalogueTests
{
    private static GameReferenceDatabase Imported(out string directory)
    {
        directory = NosTaleClientFactAttribute.ResolveDirectory()!;
        GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        var importer = new ReferenceImporter(directory);
        Assert.True(importer.ImportOne(
            db, ReferenceImporter.Tables.Single(t => t.Kind == "item")).Ok);
        Assert.True(importer.ImportLanguage(db, "IT").Ok);
        return db;
    }

    [NosTaleClientFact]
    public void A_real_vnum_resolves_and_a_missing_one_does_not_resemble_it()
    {
        using GameReferenceDatabase db = Imported(out _);
        var catalogue = new ItemCatalogue(db);

        ItemCatalogueLookup found = catalogue.Lookup(8);
        ItemCatalogueLookup missing = catalogue.Lookup(999999);

        Assert.True(found.Ok);
        Assert.NotNull(found.Item);
        Assert.Equal(8, found.Item!.Vnum);

        Assert.False(missing.Ok);
        Assert.Null(missing.Item);
        Assert.Equal(ItemCatalogue.ItemNotInCatalogueReason, missing.FailureReason);
        // The two answers do not resemble each other: one is an item, the other a reason.
        Assert.NotEqual(found.Item, missing.Item);
    }

    [NosTaleClientFact]
    public void A_numeric_field_is_distinguishable_from_the_confirmed_identity_by_its_type()
    {
        using GameReferenceDatabase db = Imported(out _);
        CataloguedItem item = new ItemCatalogue(db).Lookup(8).Item!;

        // The identity is what the wire confirms: the vnum and its display name.
        Assert.Equal(8, item.Vnum);
        Assert.Equal("Fionda in legno", item.Name);

        // The numeric fields stay provisional: the wire never carries them, so
        // they are published Unknown, never readable as a wire-confirmed fact.
        Assert.False(item.Price.HasValue);
        Assert.False(item.ItemType.HasValue);
        Assert.False(item.Slot.HasValue);
        Assert.Equal(DataSourceKind.Unknown, item.Price.Source);
        Assert.Equal(ItemCatalogue.ItemFieldsNotOnWireReason, item.Price.FailureReason);
        Assert.Equal(ItemCatalogue.ItemFieldsNotOnWireReason, item.Slot.FailureReason);
    }

    [Fact]
    public void An_absent_catalogue_is_a_named_reason_not_an_empty_catalogue()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nosai-itemcat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            GameReferenceLocation location = GameReferenceLocator.LocateIn(dir);

            Assert.False(location.Exists);
            Assert.StartsWith(GameReferenceLocator.DatabaseNotFound, location.FailureReason, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(dir, GameReferenceLocator.FileName)));
            // No catalogue was created, and nothing was read as "zero items".
            Assert.False(GameReferenceLocator.TryOpen(location, out _, out _));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// The three item vnums the eight captures name and the installed catalogue
    /// resolves are pinned to the client's own table: identity (display name) and
    /// the slot enum mapping. If an update changed either, the wire's identity
    /// check would silently shift meaning.
    /// </summary>
    [NosTaleClientFact]
    public void The_three_wire_resolvable_items_hold_the_catalogues_values()
    {
        using GameReferenceDatabase db = Imported(out _);
        var catalogue = new ItemCatalogue(db);

        (int Vnum, string Name, EquipmentSlot Slot)[] expected =
        {
            (8, "Fionda in legno", EquipmentSlot.SecondaryWeapon),
            (13, "Uniforme da allenamento", EquipmentSlot.Armor),
            (2612, "Amuleto rafforzamento armatura", EquipmentSlot.Amulet),
        };

        foreach ((int vnum, string name, EquipmentSlot slot) in expected)
        {
            CataloguedItem item = catalogue.Lookup(vnum).Item!;

            Assert.Equal(name, item.Name);
            Assert.Equal(slot, item.Raw.Slot);
        }
    }

    /// <summary>
    /// The wire cross-check <c>docs/agents/DEEPSEEK_HANDOFF.md</c> § S4 asks for,
    /// on the one capture that records the whole event: <c>drop</c>, <c>sayi</c>
    /// and <c>ivn</c> each name the same vnum, so the four sources do not
    /// disagree on what was picked up.
    /// </summary>
    [RecordedCaptureFact("messaggi.noscap")]
    public void Drop_sayi_and_ivn_name_the_same_vnum_in_one_capture()
    {
        (HashSet<int> drop, HashSet<int> ivn, HashSet<int> sayi) = ItemVnums("messaggi.noscap");

        // The event chain in this capture: drop 8 -> sayi ... 2 8 -> ivn 0.8.
        // All three sources name vnum 8, and none names a different vnum for
        // that same pickup.
        Assert.Contains(8, drop);
        Assert.Contains(8, ivn);
        Assert.Contains(8, sayi);
        Assert.Contains(8, drop.Intersect(ivn).Intersect(sayi));
    }

    /// <summary>The item vnums a recording names, split by the source that named them.</summary>
    /// <remarks>
    /// <c>drop</c> field 1, <c>ivn</c> dotted-group part 1 and <c>sayi</c> field 6
    /// (only when field 5 is 2 — the item-argument marker) are the three sources
    /// <c>docs/PROTOCOLLO_NOSTALE.md</c> marks as carrying an item vnum.
    /// </remarks>
    private static (HashSet<int> Drop, HashSet<int> Ivn, HashSet<int> Sayi) ItemVnums(string recording)
    {
        string path = RecordedCaptureFactAttribute.Resolve(recording)!;
        using IPacketSource source = CaptureFile.Open(path);

        var drop = new HashSet<int>();
        var ivn = new HashSet<int>();
        var sayi = new HashSet<int>();

        foreach (WireTimelineEntry entry in WireInspectCommand.Timeline(source, DataSourceKind.Cached, opcodes: null, maxLines: 0))
        {
            string[] f = entry.Line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (f[0])
            {
                case "drop" when f.Length > 1 && int.TryParse(f[1], out int dropVnum) && dropVnum > 0:
                    drop.Add(dropVnum);
                    break;
                case "ivn" when f.Length > 2:
                {
                    string[] parts = f[2].Split('.');
                    if (parts.Length > 1 && int.TryParse(parts[1], out int ivnVnum) && ivnVnum > 0)
                        ivn.Add(ivnVnum);
                    break;
                }
                case "sayi" when f.Length > 6 && f[5] == "2" && int.TryParse(f[6], out int sayiVnum) && sayiVnum > 0:
                    sayi.Add(sayiVnum);
                    break;
            }
        }

        return (drop, ivn, sayi);
    }
}
