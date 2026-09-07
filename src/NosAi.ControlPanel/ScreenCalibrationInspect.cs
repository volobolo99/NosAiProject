using System.Globalization;
using System.IO;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;

namespace NosAi.ControlPanel;

/// <summary>
/// Che cosa il repository sa oggi della proiezione schermo → mappa, letto dai
/// file veri e mai riassunto in un valore d'esempio.
/// </summary>
/// <remarks>
/// <para>
/// Ogni riga qui viene da <c>data/perception/screen-projection.calibration</c> o
/// da <c>data/perception/screen-samples.txt</c>. Dove un file manca o non si
/// legge, la riga dice <c>UNKNOWN</c> con il motivo: è la stessa regola del resto
/// del pannello, e vale soprattutto qui, perché una calibrazione assente e una
/// calibrazione sbagliata portano lo stesso personaggio nello stesso posto
/// sbagliato, ma solo la prima si vede.
/// </para>
/// <para>
/// <b>Perché il pannello mostra anche i campioni.</b> Il 2026-09-07 una sessione
/// di cinque clic è stata rifiutata con <c>scale_not_determined:72x43pct</c> e
/// l'operatore l'ha scoperto solo lanciando un secondo comando a mano. Il numero
/// di campioni e le direzioni che coprono sono ciò che decide quel rifiuto:
/// stanno qui perché si vedano prima, non dopo.
/// </para>
/// </remarks>
internal static class ScreenCalibrationInspect
{
    /// <summary>Il file dei campioni, relativo alla radice del repository.</summary>
    public const string SamplesRelativePath = ScreenProjectionProbe.SamplesRelativePath;

    /// <summary>La calibrazione scritta, relativa alla radice del repository.</summary>
    public const string CalibrationRelativePath = "data/perception/screen-projection.calibration";

    public static IReadOnlyList<DisplayField> Inspect(string repoRoot)
    {
        var fields = new List<DisplayField>();
        ReadCalibration(repoRoot, fields);
        ReadSamples(repoRoot, fields);
        return fields;
    }

    private static void ReadCalibration(string repoRoot, List<DisplayField> fields)
    {
        string path = Path.Combine(repoRoot, CalibrationRelativePath);
        if (!File.Exists(path))
        {
            fields.Add(new DisplayField("Calibrazione", "UNKNOWN · calibration_file_missing", "UNKNOWN"));
            return;
        }

        ScreenProjectionCalibration calibration = ScreenProjectionCalibration.Load(path, out string? failure);
        if (!calibration.IsCalibrated)
        {
            fields.Add(new DisplayField("Calibrazione", $"UNKNOWN · {failure ?? "calibration_not_loaded"}", "UNKNOWN"));
            return;
        }

        double pitchX = Math.Sqrt((calibration.A * calibration.A) + (calibration.D * calibration.D));
        double pitchY = Math.Sqrt((calibration.B * calibration.B) + (calibration.E * calibration.E));

        // «PRESENTE» senza data non distingue una calibrazione di ieri da una di
        // sei mesi fa, e il file la porta.
        fields.Add(new DisplayField("Calibrazione", calibration.CalibratedAtUtc is { } when
            ? string.Create(CultureInfo.InvariantCulture, $"PRESENTE · calibrata il {when.ToLocalTime():yyyy-MM-dd HH:mm}")
            : "PRESENTE · senza data (file di un formato precedente)", "Screen"));
        fields.Add(new DisplayField("Passo casella", string.Create(CultureInfo.InvariantCulture,
            $"{pitchX:F2} x {pitchY:F2} px"), "Screen"));
        fields.Add(new DisplayField("Ancora personaggio", string.Create(CultureInfo.InvariantCulture,
            $"({calibration.C:F0}, {calibration.F:F0}) px"), "Screen"));
        fields.Add(new DisplayField("Regime", string.Create(CultureInfo.InvariantCulture,
            $"{calibration.ClientWidth}x{calibration.ClientHeight} @ {calibration.ClientDpi} DPI · {calibration.Regime}"), "Screen"));
        // `VerifiedAgainstSamples` NON e' il numero di campioni: e' quanti ce ne
        // sono oltre a quelli che il fit consuma (tre per l'affine, quattro per la
        // prospettica). Scriverlo come «verificata su N campioni» dava 16 dove i
        // campioni usati erano 19 -- un numero piu' basso del vero, accanto a un
        // «20 righe · 20 utilizzabili» che lo contraddiceva.
        int consumed = calibration.G != 0 || calibration.H != 0
            ? ScreenProjectionCalibration.PerspectiveMinimumPairs
            : ScreenProjectionCalibration.MinimumSamples;
        fields.Add(new DisplayField("Verificata su", string.Create(CultureInfo.InvariantCulture,
            $"{calibration.VerifiedAgainstSamples + consumed} campioni usati ({calibration.VerifiedAgainstSamples} oltre i {consumed} che il fit consuma) · residuo peggiore {calibration.WorstResidualPixels:F1} px"), "Screen"));

        // Una calibrazione che ha lasciato fuori dei campioni non e' la stessa
        // cosa di una che li ha usati tutti, e chi la legge deve poterlo vedere
        // senza aprire il file.
        fields.Add(new DisplayField("Campioni scartati", calibration.DiscardedSamples == 0
            ? "nessuno"
            : string.Create(CultureInfo.InvariantCulture,
                $"{calibration.DiscardedSamples} fuori bersaglio"), "Screen"));
    }

    private static void ReadSamples(string repoRoot, List<DisplayField> fields)
    {
        string path = Path.Combine(repoRoot, SamplesRelativePath);
        if (!File.Exists(path))
        {
            fields.Add(new DisplayField("Campioni", "UNKNOWN · samples_file_missing", "UNKNOWN"));
            return;
        }

        string[] lines = File.ReadAllLines(path);
        if (lines.Length == 0
            || !string.Equals(lines[0].Trim(), ScreenProjectionProbe.SamplesHeader, StringComparison.Ordinal))
        {
            fields.Add(new DisplayField(
                "Campioni", $"UNKNOWN · {ScreenProjectionProbe.SamplesVersionUnsupportedReason}", "UNKNOWN"));
            return;
        }

        // Raggruppati per regime, perché è il regime a decidere quali entrano nel
        // fit: un file con dodici righe di cui quattro di un'altra geometria non
        // ha dodici campioni utilizzabili, e dirlo come "12" sarebbe una bugia
        // aritmeticamente corretta.
        var byRegime = new Dictionary<string, List<ScreenProjectionSample>>(StringComparer.Ordinal);
        var geometry = new Dictionary<string, (int Width, int Height)>(StringComparer.Ordinal);
        int malformed = 0;
        foreach (string line in lines.Skip(1))
        {
            string[] f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (f.Length != 7
                || !int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int dx)
                || !int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int dy)
                || !int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int px)
                || !int.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int py))
            {
                malformed++;
                continue;
            }

            if (!int.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int w)
                || !int.TryParse(f[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h)
                || w <= 0 || h <= 0)
            {
                malformed++;
                continue;
            }

            string regime = $"{w}x{h} @ {f[6]} DPI";
            geometry[regime] = (w, h);
            if (!byRegime.TryGetValue(regime, out List<ScreenProjectionSample>? bucket))
                byRegime[regime] = bucket = new List<ScreenProjectionSample>();
            bucket.Add(new ScreenProjectionSample(new MapPoint(dx, dy), px, py));
        }

        if (byRegime.Count == 0)
        {
            fields.Add(new DisplayField("Campioni", "0 · nessuna riga valida", "Screen"));
            return;
        }

        foreach ((string regime, List<ScreenProjectionSample> rows) in byRegime)
        {
            (int w, int h) = geometry[regime];
            var coach = new ScreenSampleCoach(w, h);
            foreach (ScreenProjectionSample row in rows)
                coach.Offer(row, characterWasAtRest: true);

            ScreenSampleCoverage coverage = coach.Coverage;
            fields.Add(new DisplayField(
                $"Campioni {regime}",
                string.Create(CultureInfo.InvariantCulture,
                    $"{rows.Count} righe · {coverage.Accepted} utilizzabili · "
                    + $"{coverage.SectorsFilled}/{ScreenSampleCoach.SectorCount} direzioni · "
                    + $"il piu' lontano a {coverage.FarthestTiles} caselle"),
                "Screen"));
            fields.Add(new DisplayField($"Prossimo passo {regime}", coach.Advice, "Screen"));
        }

        if (malformed > 0)
            fields.Add(new DisplayField("Righe illeggibili", malformed.ToString(CultureInfo.InvariantCulture), "Screen"));
    }
}
