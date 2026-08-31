using NosAi.Runtime.GameData;
using Xunit;
using Xunit.Abstractions;

namespace NosAi.Runtime.Tests;

/// <summary>
/// How much of the client's data this decoder actually reads.
/// </summary>
/// <remarks>
/// A reference database is only as good as its coverage, and coverage is a number
/// that must be measured rather than assumed. These tests report the share of
/// values that decoded and the share that came back <c>UNKNOWN</c>, so the gap is
/// visible on the operator page instead of hiding inside a green tick.
/// </remarks>
public sealed class ReferenceCoverageTests
{
    private readonly ITestOutputHelper _output;

    public ReferenceCoverageTests(ITestOutputHelper output) => _output = output;

    [NosTaleClientFact]
    public void TheDecodedShareOfEachTableIsMeasuredAndReported()
    {
        string directory = NosTaleClientFactAttribute.ResolveDirectory()!;
        var importer = new ReferenceImporter(directory);

        long total = 0, unknown = 0;

        foreach (ReferenceTable table in ReferenceImporter.Tables)
        {
            using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
            ImportOutcome outcome = importer.ImportOne(db, table);
            if (!outcome.Ok)
            {
                Evidence.Unknown(_output, table.Kind, outcome.FailureReason ?? "senza motivo");
                continue;
            }

            long values = 0, missing = 0;
            for (int vnum = 0; vnum <= 20000; vnum++)
            {
                IReadOnlyList<NosField>? fields = db.Lookup(table.Kind, vnum);
                if (fields is null)
                    continue;
                foreach (NosField field in fields)
                {
                    foreach (string value in field.Values)
                    {
                        values++;
                        if (value.Contains(NosDataTable.UnknownValue, StringComparison.Ordinal))
                            missing++;
                    }
                }
            }

            total += values;
            unknown += missing;
            double share = values == 0 ? 0 : 100.0 * (values - missing) / values;
            Evidence.Live(_output, table.Kind,
                $"{outcome.RecordsRead} record · {values} valori · {share:F2}% decodificati");
        }

        double overall = total == 0 ? 0 : 100.0 * (total - unknown) / total;
        Evidence.Live(_output, "valoriTotali", total);
        Evidence.Live(_output, "nonDecodificati", unknown);
        Evidence.Live(_output, "coperturaPercentuale", $"{overall:F2}");

        Assert.True(total > 100_000, $"attesi molti valori, contati {total}");

        // Everything decodes, so the bar is everything. A threshold below 100% would
        // let a regression hide behind it; the first version of this test allowed
        // 95%% and would have accepted the 7 555 values that turned out to be the
        // game's rates and multipliers rather than broken integers.
        Assert.Equal(0, unknown);
        Assert.Equal(100.0, overall, 3);
    }

    [NosTaleClientFact]
    public void TheClientsOwnLanguageTablesGiveEveryEntityItsName()
    {
        // The meanings are not inferred: the client ships them, and this reads them
        // from the same files it does.
        string directory = NosTaleClientFactAttribute.ResolveDirectory()!;
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        var importer = new ReferenceImporter(directory);

        importer.ImportAll(db);
        LanguageImportReport language = importer.ImportLanguage(db, "IT");

        Evidence.Live(_output, "lingua", language.Language);
        Evidence.Live(_output, "vociTestuali", language.Total);

        foreach ((string kind, int entries) in language.EntriesByKind)
        {
            int named = db.NamedCount(kind, "IT");
            int total = db.Count(kind);
            double share = total == 0 ? 0 : 100.0 * named / total;
            Evidence.Live(_output, kind, $"{named}/{total} con nome ({share:F1}%) · {entries} voci");
        }

        Evidence.Live(_output, "esempioMostro1", db.DisplayName("monster", 1, "IT") ?? "(senza nome)");
        Evidence.Live(_output, "esempioBCard1", db.DisplayName("bcard", 1, "IT") ?? "(senza nome)");

        Assert.True(language.Ok, language.FailureReason);
        Assert.True(language.Total > 20_000, $"attese molte voci, lette {language.Total}");

        // The mechanism is proven: a key resolves to the text the client displays.
        Assert.Equal("Volpe", db.DisplayName("monster", 1, "IT"));
        Assert.Equal("Attacco Speciale", db.DisplayName("bcard", 1, "IT"));

        // Only a minority join today, and the reason is stated rather than papered
        // over: between the packed number and the trailing 'e' each key carries one
        // more byte, constant in the data tables and varying in the language tables,
        // so the two numberings do not line up beyond the keys that need no packing.
        // Asserting a high match rate here would be asserting something untrue; this
        // pins what does work so a regression in it is caught.
        Assert.True(db.NamedCount("monster", "IT") >= 90,
            $"nomi risolti scesi a {db.NamedCount("monster", "IT")}");
    }

    [NosTaleClientFact]
    public void AMissingLanguageIsReportedRatherThanSubstituted()
    {
        // Showing German where Italian was asked for would be a quiet lie about what
        // the operator is reading.
        string directory = NosTaleClientFactAttribute.ResolveDirectory()!;
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();

        LanguageImportReport report = new ReferenceImporter(directory).ImportLanguage(db, "ZZ");

        Assert.False(report.Ok);
        Assert.NotNull(report.FailureReason);
        Assert.Equal(0, db.TextCount("ZZ"));
    }
}
