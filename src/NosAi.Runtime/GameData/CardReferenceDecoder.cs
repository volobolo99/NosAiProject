namespace NosAi.Runtime.GameData;

/// <summary>
/// One entry of the client's own BCard catalogue: the identifier a
/// <see cref="BCardApplication"/> points at, and the language key the
/// client labels that effect's category with. This is catalog data — the
/// same "real reference, not a live fact" category
/// <see cref="GameReferenceDatabase"/> already documents for every table it
/// imports — and it resolves the one thing the skill/item/monster decoders
/// leave dangling: those decoders tell a caller <i>that</i> a skill applies
/// BCard <c>N</c>, and this is the decoder that says <i>which</i> BCard
/// <c>N</c> is.
/// </summary>
/// <remarks>
/// <para>
/// The tag layout comes from the same community reference used by the other
/// decoders — <c>https://nt-research.github.io/docs/NOS files/NSgtdData/bcard_dat</c>
/// ("BCard.dat"). That page documents this table's tags verbatim:
/// <c>VNUM</c> (the BCard vnum), <c>ICON</c> ("always -1, doesn't work
/// sadly"), <c>NAME</c> (the BCard category code name), <c>DESC</c> (the
/// per-sub display type), <c>SUBJ1..SUBJN</c> (subject code names) and
/// <c>LIST1-1..LISTN-2</c> (display-string code names). As with the other
/// decoders, that page is a lead, not a ground truth; the position of the
/// two tags this decoder promotes has been cross-checked against a real
/// client's own data (see below), and everything else is deliberately left
/// out for the reasons given.
/// </para>
/// <para>
/// Only two fields are promoted. <see cref="Vnum"/> is the identifier every
/// <see cref="BCardApplication.BCardVnum"/> points at — a reference that
/// resolves is the proof that this decoder's <c>VNUM</c> position is right,
/// and that check is performed by <c>CardReferenceDecoderTests</c> against
/// the installed client. The catalogue itself is dense over
/// <c>VNUM</c> 1..131 (131 records, no gaps), and every
/// <see cref="BCardApplication.BCardVnum"/> a real <c>Skill.dat</c> skill
/// applies that falls inside that range resolves here. (<see cref="BCardApplication.BCardVnum"/>
/// values of zero or less are the empty-slot sentinel the other decoders
/// already accept — <c>Item.dat</c>'s <c>BUFF</c> and <c>Skill.dat</c>'s
/// <c>BASIC</c> write <c>-1</c> or <c>0</c> for a slot with no effect — so
/// they are not references into this catalogue and are never expected to
/// resolve.)
/// </para>
/// <para>
/// One boundary is reported, not resolved, by the cross-check test: a
/// further 92 of the 3 766 above-zero references that real skills apply
/// point at 17 vnums (1180–1290) that exist in <b>neither</b> this table
/// (1..131) nor <c>Card.dat</c> (0..4440). Whether those are references
/// into a further, unimported table or a Skill.dat layout this project has
/// not yet characterised is an open question the test surfaces as evidence
/// rather than guesses here; they are out of this decoder's contract.
/// </para>
/// <para>
/// <see cref="NameKey"/> is the raw <c>NAME</c> value, a <c>zts…</c> language
/// key (for example the entry a skill's effect points at carries
/// <c>zts23e</c>, not a literal code name) — the same convention as
/// <see cref="SkillReference.NameKey"/> and <see cref="MonsterReference.NameKey"/>.
/// The displayed category name is resolved through
/// <c>NSlangData_&lt;LANG&gt;.NOS</c> (the existing
/// <see cref="ReferenceImporter.ImportLanguage"/> /
/// <see cref="GameReferenceDatabase.DisplayName"/> path already maps the
/// <c>bcard</c> kind to <c>_code_&lt;lang&gt;_BCard.txt</c>); this decoder
/// keeps the key raw rather than duplicating that resolution.
/// </para>
/// <para>
/// The mechanical effect itself is <b>not</b> in this file, and no decoder
/// can invent it: the source states BCard.dat is "pure clientside ... only
/// carries how should given BCard string be displayed in the client". The
/// semantic meaning of a BCard lives in the server, not in the client, so
/// what this decoder exposes is the category identity (its key), not a
/// combat interpretation. A caller that needs "increases movement speed by
/// X" must add a verified semantic layer elsewhere; presenting the display
/// strings as facts would be the guess this project's rules exist to
/// prevent.
/// </para>
/// <para>
/// Tags the source documents but this decoder does not decode, and why:
/// <c>ICON</c> is always -1 (zero information content — the same reason
/// <see cref="MonsterReferenceDecoder"/> skips <c>PARTNER</c>); <c>DESC</c>
/// governs how the client renders the effect's two values into a string
/// (variable substitution), not what the effect is, so it is rendering
/// metadata; <c>SUBJ1..SUBJN</c> are, in the source's own words, "useless
/// for the client ... only helps programmers while creating/using BCards";
/// <c>LIST1-1..LISTN-2</c> are the localized display strings (resolved
/// through <c>NSlangData_&lt;LANG&gt;.NOS</c>, not this table) and are
/// therefore text-rendering data, not effect data. <c>END</c> is the record
/// terminator, not a value. None of these is turned into a zero, a false,
/// or an "Unknown" placeholder: they are absent from the contract, and a
/// caller asking for them gets no property at all rather than a fabricated
/// one.
/// </para>
/// </remarks>
public sealed record CardReference(
    int Vnum,
    string NameKey);

/// <summary>
/// Decodes one <c>BCard.dat</c> table <see cref="NosRecord"/> into a
/// <see cref="CardReference"/>. Pure and deterministic: the same record
/// always decodes to the same result, and a tag the record does not carry
/// leaves the field null or empty rather than a guessed value.
/// </summary>
/// <remarks>
/// The <c>VNUM</c> position is cross-checked by <c>CardReferenceDecoderTests</c>
/// against the installed client (every in-range
/// <see cref="BCardApplication.BCardVnum"/> a real skill applies resolves;
/// a small out-of-range remainder is reported, not resolved). See
/// <see cref="CardReference"/>'s own remarks for the source of the tag
/// layout and for what is deliberately left out.
/// </remarks>
public static class CardReferenceDecoder
{
    /// <summary>
    /// Decodes <paramref name="record"/>, or returns null when it carries
    /// no <c>VNUM</c> — a BCard this decoder cannot identify is not worth
    /// a half-built result.
    /// </summary>
    public static CardReference? Decode(NosRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Vnum is not int vnum)
            return null;

        string nameKey = record.Field("NAME")?.Value(0) ?? string.Empty;

        return new CardReference(vnum, nameKey);
    }
}
