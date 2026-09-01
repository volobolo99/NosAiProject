using NosAi.Core;

namespace NosAi.Storage;

/// <summary>
/// One row of the Gate 1 journal, as read back
/// (docs/ROADMAP_ESECUTIVA.md S:2.2).
/// </summary>
/// <param name="Sequence">
/// The position <see cref="IEventJournal.Append"/> assigned. When passed
/// <em>into</em> <see cref="IEventJournal.Append"/>, this field is ignored: the
/// journal is the sole source of the total order, not the caller (a caller
/// that could pick its own sequence could also pick one out of order).
/// </param>
/// <param name="UnixMillis">Wall-clock time the record was appended.</param>
/// <param name="Stage">Which <see cref="PipelineStage"/> produced this record.</param>
/// <param name="Payload">The stage-specific bytes. Opaque to the journal.</param>
/// <param name="ChainHash">
/// <c>SHA256(previous ChainHash ‖ Sequence ‖ UnixMillis ‖ Stage ‖ Payload)</c>
/// (docs/ROADMAP_ESECUTIVA.md S:2.3). Also ignored as input to
/// <see cref="IEventJournal.Append"/>: it is computed by the journal, never
/// supplied by the caller, or a corrupted caller could stamp a record with
/// whatever hash makes it look uncorrupted.
/// </param>
public readonly record struct JournalRecord(
    long Sequence,
    long UnixMillis,
    PipelineStage Stage,
    ReadOnlyMemory<byte> Payload,
    ReadOnlyMemory<byte> ChainHash);

/// <summary>
/// The durable, tamper-evident record of everything that happened on the
/// critical path. Every append is chained to the one before it (INV-05): a
/// journal that only detected gaps, not alteration, would not answer "was this
/// record changed after the fact".
/// </summary>
public interface IEventJournal : IAsyncDisposable
{
    /// <summary>
    /// Appends a record, assigning it the next sequence number and computing
    /// its chain hash. Synchronous and single-writer by design
    /// (docs/ROADMAP_ESECUTIVA.md S:1.2 INV-07): the critical path calls this
    /// at cycle boundaries, not mid-cycle, so there is nothing here for
    /// <c>async</c> to usefully overlap with.
    /// </summary>
    /// <returns>The sequence number assigned to this record.</returns>
    long Append(in JournalRecord record);

    /// <summary>Reads records back in the order they were written, oldest first.</summary>
    IAsyncEnumerable<JournalRecord> ReplayAsync(long fromSequence, CancellationToken ct);

    /// <summary>
    /// Recomputes the hash chain and compares it against what is stored.
    /// </summary>
    /// <param name="fromSequence">
    /// Where to start recomputing from. The hash trusted as the starting point
    /// is whichever stored record immediately precedes this sequence (or the
    /// genesis hash, if none does) -- verification never trusts a caller-supplied
    /// starting hash, only one already committed to the journal.
    /// </param>
    /// <param name="firstBrokenSequence">
    /// The first sequence whose stored hash does not match the recomputed one,
    /// or -1 when the chain is intact.
    /// </param>
    bool VerifyChain(long fromSequence, out long firstBrokenSequence);
}
