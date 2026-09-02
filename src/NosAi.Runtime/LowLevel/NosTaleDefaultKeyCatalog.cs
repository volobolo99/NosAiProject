namespace NosAi.Runtime.LowLevel;

/// <summary>
/// How <see cref="NosTaleDefaultKeyCatalog"/> classifies one declared key.
/// </summary>
/// <remarks>
/// The names are the three classes of
/// <c>docs/TASTI_E_BERSAGLIO.md</c> § 1.2–1.3, plus the companion and bar rows
/// that sit beside them in the same table. No fourth combat class is added.
/// </remarks>
public enum DefaultKeyClass
{
    /// <summary>Opens a client window. Italian table: <c>interfaccia</c>.</summary>
    Interface = 0,

    /// <summary>Cycles a selection. Italian table: <c>selezione</c>.</summary>
    Selection = 1,

    /// <summary>Selects and attacks. Italian table: <c>selezione + atto</c>.</summary>
    SelectionAndAct = 2,

    /// <summary>Commands the NosMate. Italian table: <c>compagno</c>.</summary>
    Companion = 3,

    /// <summary>Switches the quick bar. Italian table: <c>barra</c>.</summary>
    Bar = 4,

    /// <summary>
    /// Quick-bar slots the client ships empty. Italian table:
    /// <c>vuoti per progetto</c>.
    /// </summary>
    EmptyByDesign = 5,
}

/// <summary>One row of the default-key table, transcribed as the document wrote it.</summary>
/// <param name="Key">The key glyph from the table's first column, not a virtual-key code.</param>
/// <param name="DeclaredEffect">The declared effect, in the document's own words.</param>
/// <param name="Class">The table's class column.</param>
public readonly record struct DefaultKeyDeclaration(
    string Key,
    string DeclaredEffect,
    DefaultKeyClass Class);

/// <summary>
/// The default NosTale keys, transcribed from <c>docs/TASTI_E_BERSAGLIO.md</c> § 1.2.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where the rows come from.</b> Collected on 2 September 2026 from the public
/// guides listed in that document's § 8 (Magic Game World PC controls, the
/// Nostale Wiki inventory and options pages, and the NosTale game-interface
/// guide). They are third-party sources, not the publisher's, and
/// <b>no row has been verified against the client on this machine</b>.
/// </para>
/// <para>
/// <b>How they are held.</b> The same treatment
/// <see cref="NosAi.LiveIntegration.NosTaleClientLayout"/> gives the offsets
/// published by <c>NosSmooth.Local</c>: a starting hypothesis, not trusted on
/// authority. This type does not bind a key, does not confirm an effect, and
/// does not change the intent state machine. Confirmation is a later command.
/// </para>
/// <para>
/// The last table row is kept as one declaration — <c>1…0, Q, W, E, R, T</c> —
/// because the document wrote one row, not sixteen. Expanding it here would be
/// deciding which glyph is which slot.
/// </para>
/// </remarks>
public static class NosTaleDefaultKeyCatalog
{
    /// <summary>Calendar date the source document recorded for the collection.</summary>
    public const string CollectedOn = "2026-09-02";

    /// <summary>Section transcribed. Nothing outside it is a row here.</summary>
    public const string SourceSection = "docs/TASTI_E_BERSAGLIO.md § 1.2";

    /// <summary>The fifteen table rows, in document order.</summary>
    public static readonly IReadOnlyList<DefaultKeyDeclaration> Entries =
    [
        new("I", "inventario", DefaultKeyClass.Interface),
        new("K", "finestra abilità", DefaultKeyClass.Interface),
        new("P", "scheda personaggio", DefaultKeyClass.Interface),
        new("O", "missioni", DefaultKeyClass.Interface),
        new("L", "miniland", DefaultKeyClass.Interface),
        new("N", "messaggistica / amici", DefaultKeyClass.Interface),
        new("F12", "guida di gioco", DefaultKeyClass.Interface),
        new("F6", "seleziona il giocatore successivo", DefaultKeyClass.Selection),
        new("F7", "seleziona l'NPC successivo", DefaultKeyClass.Selection),
        new("F8", "seleziona il mostro successivo", DefaultKeyClass.Selection),
        new("Spazio", "seleziona il mostro successivo e attacca con l'attacco primario", DefaultKeyClass.SelectionAndAct),
        new("Z", "come sopra, con l'attacco secondario", DefaultKeyClass.SelectionAndAct),
        new("A", "manda il NosMate in un punto", DefaultKeyClass.Companion),
        new("Tab", "passa all'altra barra rapida", DefaultKeyClass.Bar),
        new("1…0, Q, W, E, R, T", "slot rapidi", DefaultKeyClass.EmptyByDesign),
    ];
}
