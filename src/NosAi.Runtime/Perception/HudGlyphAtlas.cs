using System.Globalization;
using System.Text;

namespace NosAi.Runtime.Perception;

/// <summary>
/// What one training pass learned, and what it refused to learn.
/// </summary>
/// <param name="Learned">Glyphs added to the atlas by this pass.</param>
/// <param name="AlreadyKnown">Glyphs the atlas already had, with the same character.</param>
/// <param name="FailureReason">
/// Why nothing was learned, or null when something was. A failed pass leaves the
/// atlas exactly as it was: a half-applied lesson is worse than none, because the
/// entries it did write would be the ones before the disagreement.
/// </param>
public sealed record HudGlyphTrainingResult(int Learned, int AlreadyKnown, string? FailureReason)
{
    public bool Succeeded => FailureReason is null;

    public static HudGlyphTrainingResult Refused(string reason) => new(0, 0, reason);
}

/// <summary>
/// The trained mapping from glyph bitmap to character, and the file it lives in.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> <see cref="ScreenVitalReader"/> has always been able to
/// find the numerals on the HUD and normalise them into glyph bitmaps, and
/// <see cref="GlyphHashOcrCache"/> has always been able to recognise a glyph it
/// was taught. Nothing ever taught it one. So every screen reading in the project
/// so far has ended <c>ocr_glyphs_not_trained</c>, and HP off the screen has been
/// a ratio and never a number — which is the difference ADR-0012 turns on, since
/// Gate 3 plans on HP and max HP as integers and a ratio cannot be turned into
/// them without inventing one of the two.
/// </para>
/// <para>
/// <b>Where the labels come from.</b> Not from a person typing what they see. The
/// world channel already reports <c>stat 7305 7305 1420 1420</c>
/// (<c>docs/PROTOCOLLO_NOSTALE.md</c>), so the network reading is the supervision
/// for the screen reading: the crop is labelled by a source that was checked
/// against this same HUD. Once the atlas is trained the screen reads on its own,
/// which is what makes it an <i>independent confirming source</i> rather than a
/// second opinion from the same wire — and the protocol document is explicit that
/// a confirming source is what the player's own position and target state need.
/// </para>
/// <para>
/// <b>Font-specific, machine-specific, and therefore not committed.</b> The
/// hashes are of bitmaps rendered by one client at one scale. The atlas belongs
/// beside the crops in <c>data/</c>, which is gitignored, and it is versioned by
/// the normalisation it was built under: change
/// <see cref="HudGlyphExtractor.NormalizedWidth"/> and every hash in an old file
/// is meaningless, so an old file is refused rather than half-matched.
/// </para>
/// </remarks>
public sealed class HudGlyphAtlas
{
    /// <summary>Where the atlas lives, relative to the repository root.</summary>
    public const string RelativePath = "data/perception/glyphs.atlas";

    private const string Magic = "nosai-glyph-atlas";
    private const int Version = 1;

    private readonly Dictionary<ulong, char> _entries = new();

    /// <summary>The normalisation the entries were hashed under.</summary>
    public int GlyphWidth { get; }

    /// <summary>The normalisation the entries were hashed under.</summary>
    public int GlyphHeight { get; }

    public int Count => _entries.Count;

    /// <summary>The characters the atlas can currently recognise, sorted.</summary>
    public IReadOnlyCollection<char> KnownCharacters => _entries.Values.Distinct().Order().ToArray();

    public HudGlyphAtlas()
        : this(HudGlyphExtractor.NormalizedWidth, HudGlyphExtractor.NormalizedHeight)
    {
    }

    private HudGlyphAtlas(int glyphWidth, int glyphHeight)
    {
        GlyphWidth = glyphWidth;
        GlyphHeight = glyphHeight;
    }

    /// <summary>
    /// Teaches the atlas one crop, given what the numerals in it say.
    /// </summary>
    /// <param name="glyphs">Glyph bitmaps from <see cref="HudGlyphExtractor.Extract"/>.</param>
    /// <param name="expectedText">
    /// What that crop reads, from a source that is not this reader — the world
    /// channel's <c>stat</c>, formatted <c>current/maximum</c>.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>A count disagreement refuses the whole pass.</b> The extractor splits on
    /// runs of ink, so two numerals printed with no gap between them arrive as one
    /// bitmap — measured on the real client, <c>1420/1420</c> merges its <c>0</c>
    /// and <c>/</c> into a single seven-pixel group while <c>7305/7305</c> separates
    /// cleanly into nine. Pairing nine characters onto eight bitmaps by position
    /// would teach the atlas that a merged <c>0/</c> is the character <c>0</c>, and
    /// every subsequent reading of a real <c>0</c> would then be wrong in a way
    /// nothing downstream could detect. Refusing costs one frame; the atlas is
    /// cumulative and the next frame whose values print with a gap teaches the
    /// same characters correctly.
    /// </para>
    /// <para>
    /// <b>A contradiction refuses too.</b> One bitmap that has to be two different
    /// characters means the normalisation is discarding what separates them, and
    /// no atlas built on it can be trusted.
    /// </para>
    /// </remarks>
    public HudGlyphTrainingResult Train(IReadOnlyList<byte[]> glyphs, string expectedText)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        ArgumentNullException.ThrowIfNull(expectedText);

        if (expectedText.Length == 0)
            return HudGlyphTrainingResult.Refused("expected_text_empty");

        if (glyphs.Count == 0)
            return HudGlyphTrainingResult.Refused("no_glyphs_in_roi");

        if (glyphs.Count != expectedText.Length)
        {
            return HudGlyphTrainingResult.Refused(
                $"glyph_count_mismatch:{glyphs.Count}_for_{expectedText.Length}_characters");
        }

        var pending = new Dictionary<ulong, char>();
        var known = 0;

        for (var i = 0; i < glyphs.Count; i++)
        {
            byte[] glyph = glyphs[i];
            if (glyph.Length != GlyphWidth * GlyphHeight)
                return HudGlyphTrainingResult.Refused($"glyph_size_mismatch:{glyph.Length}_bytes");

            ulong hash = GlyphHashOcrCache.HashGlyph(glyph);
            char character = expectedText[i];

            if (_entries.TryGetValue(hash, out char existing))
            {
                if (existing != character)
                    return HudGlyphTrainingResult.Refused($"glyph_conflicts_with_atlas:{existing}_vs_{character}");
                known++;
                continue;
            }

            if (pending.TryGetValue(hash, out char sameBatch))
            {
                if (sameBatch != character)
                    return HudGlyphTrainingResult.Refused($"glyph_conflicts_within_crop:{sameBatch}_vs_{character}");
                continue;
            }

            pending[hash] = character;
        }

        // Applied only now: a pass that disagreed anywhere leaves the atlas
        // untouched rather than partly taught.
        foreach ((ulong hash, char character) in pending)
            _entries[hash] = character;

        return new HudGlyphTrainingResult(pending.Count, known, null);
    }

    /// <summary>Builds an OCR cache that recognises everything this atlas knows.</summary>
    /// <remarks>
    /// A fresh cache each time, because <see cref="GlyphHashOcrCache"/> memoises a
    /// miss as <c>'?'</c>; handing an existing cache new entries would leave it
    /// answering from the memory of not knowing them.
    /// </remarks>
    public GlyphHashOcrCache ToOcrCache()
    {
        var cache = new GlyphHashOcrCache();
        foreach ((ulong hash, char character) in _entries)
            cache.TrainHash(hash, character);
        return cache;
    }

    /// <summary>
    /// Loads the atlas at <paramref name="path"/>, or returns an empty one with a
    /// reason.
    /// </summary>
    /// <remarks>
    /// A missing file is not an error: it is the state before the first training
    /// pass, and it is reported as such so the operator is told to train rather
    /// than told something is broken.
    /// </remarks>
    public static HudGlyphAtlas Load(string path, out string? failureReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        failureReason = null;

        if (!File.Exists(path))
        {
            failureReason = "atlas_not_trained_yet";
            return new HudGlyphAtlas();
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (IOException ex)
        {
            failureReason = $"atlas_unreadable:{ex.GetType().Name}";
            return new HudGlyphAtlas();
        }

        if (lines.Length == 0 || !lines[0].StartsWith(Magic, StringComparison.Ordinal))
        {
            failureReason = "atlas_header_unrecognised";
            return new HudGlyphAtlas();
        }

        string[] header = lines[0].Split(' ');
        if (header.Length != 4
            || !int.TryParse(header[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int version)
            || !int.TryParse(header[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width)
            || !int.TryParse(header[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height))
        {
            failureReason = "atlas_header_unrecognised";
            return new HudGlyphAtlas();
        }

        if (version != Version)
        {
            failureReason = $"atlas_version_unsupported:{version}";
            return new HudGlyphAtlas();
        }

        // The hashes are of bitmaps normalised to a particular size. Under a
        // different one they are hashes of something else, and matching them would
        // be recognising a glyph by the shape it used to be.
        if (width != HudGlyphExtractor.NormalizedWidth || height != HudGlyphExtractor.NormalizedHeight)
        {
            failureReason = $"atlas_normalisation_changed:{width}x{height}";
            return new HudGlyphAtlas();
        }

        var atlas = new HudGlyphAtlas(width, height);
        for (var i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Length == 0)
                continue;

            int space = line.IndexOf(' ');
            if (space <= 0 || space == line.Length - 1)
            {
                failureReason = $"atlas_entry_malformed:line_{i + 1}";
                return new HudGlyphAtlas();
            }

            if (!ulong.TryParse(line.AsSpan(0, space), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong hash))
            {
                failureReason = $"atlas_entry_malformed:line_{i + 1}";
                return new HudGlyphAtlas();
            }

            atlas._entries[hash] = line[space + 1];
        }

        return atlas;
    }

    /// <summary>Writes the atlas, creating the directory if it is missing.</summary>
    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var text = new StringBuilder();
        text.Append(Magic).Append(' ').Append(Version).Append(' ')
            .Append(GlyphWidth).Append(' ').Append(GlyphHeight).Append('\n');

        // Sorted, so the same atlas is the same bytes and a diff shows what was
        // learned rather than what the dictionary happened to reorder.
        foreach ((ulong hash, char character) in _entries.OrderBy(entry => entry.Key))
            text.Append(hash.ToString("X16", CultureInfo.InvariantCulture)).Append(' ').Append(character).Append('\n');

        File.WriteAllText(path, text.ToString());
    }
}
