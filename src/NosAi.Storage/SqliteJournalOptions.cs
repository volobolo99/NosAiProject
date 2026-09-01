namespace NosAi.Storage;

/// <summary>
/// The fixed SQLite policy for the Gate 1 journal (INV-05,
/// docs/ROADMAP_ESECUTIVA.md S:1.2). The three pragma values are not
/// configurable per instance: a journal written under a weaker durability
/// policy is not a variant of this journal, it is a different guarantee, and
/// silently allowing that would make "the journal is durable" a claim this
/// type could no longer back up for every row it contains.
/// </summary>
/// <param name="VolumeLabel">The Windows volume label <see cref="VolumeLocator"/> resolves the database path against.</param>
/// <param name="FileName">The database file name within that volume's root.</param>
public sealed record SqliteJournalOptions(string VolumeLabel = "NOSAI-SSD", string FileName = "nosai.db")
{
    public string JournalMode => "WAL";
    public string Synchronous => "FULL";
    public int BusyTimeoutMs => 5000;
}
