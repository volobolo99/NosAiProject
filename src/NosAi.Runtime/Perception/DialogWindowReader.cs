namespace NosAi.Runtime.Perception;

/// <summary>Cosa la ROI del pannello di dialogo ha detto di sé.</summary>
public enum DialogWindowState : byte
{
    /// <summary>I pixel non erano leggibili. Non è "nessuna finestra".</summary>
    Unreadable = 0,

    /// <summary>La regione diverge in modo significativo dalla base calibrata: qualcosa si è aperto.</summary>
    Present = 1,

    /// <summary>La regione corrisponde ancora alla base calibrata: nessuna finestra sopra.</summary>
    Absent = 2
}

/// <param name="State">L'esito della lettura.</param>
/// <param name="Divergence">
/// Distanza euclidea, nello spazio dei colori medi B/G/R (0-441 di ampiezza
/// massima), tra la ROI attuale e la base calibrata. <see langword="null"/>
/// solo quando <see cref="State"/> è <see cref="DialogWindowState.Unreadable"/>
/// -- il valore effettivamente misurato, non un giudizio: è il chiamante
/// (tramite <see cref="DialogWindowReader.DefaultPresentThreshold"/> o una
/// soglia propria) a decidere cosa conta come "presente".
/// </param>
/// <param name="FailureReason">Il motivo nominato, o <see langword="null"/> per una lettura riuscita.</param>
public readonly record struct DialogWindowReading(
    DialogWindowState State,
    double? Divergence,
    string? FailureReason);

/// <summary>
/// Legge la ROI del pannello di dialogo confrontando il colore medio attuale
/// (B/G/R sull'intero crop) con la base calibrata di
/// <see cref="DialogRoiCalibration"/> -- mai una firma di colore specifica
/// del gioco (vedi il commento di classe di <see cref="DialogRoiCalibration"/>
/// per il perché). Deterministico e puro: nessuna lettura precedente, nessun
/// stato nascosto.
/// </summary>
public static class DialogWindowReader
{
    /// <summary>Crop più piccolo di questo non produce una media significativa.</summary>
    public const int MinWidth = 4;

    /// <summary>Crop più piccolo di questo non produce una media significativa.</summary>
    public const int MinHeight = 4;

    /// <summary>
    /// Soglia di divergenza sopra la quale la ROI si considera cambiata
    /// rispetto alla base calibrata. **Scelta di giudizio, non una costante
    /// misurata**: nessun client reale era disponibile per calibrarla contro
    /// un vero pannello di dialogo NosTale in questa sessione. Un valore
    /// troppo basso legge rumore/antialiasing come una finestra aperta; uno
    /// troppo alto perde un pannello dai colori simili allo sfondo vuoto.
    /// Va verificata e corretta con un vero crop calibrato prima di fidarsi
    /// del risultato su un client reale.
    /// </summary>
    public const double DefaultPresentThreshold = 24.0;

    /// <summary>Legge la ROI del pannello di dialogo da un buffer BGRA.</summary>
    /// <param name="bgra">Pixel della sola ROI, quattro byte per pixel.</param>
    /// <param name="baselineMeanB">Media canale blu della base calibrata (<see cref="DialogRoiCalibration.BaselineMeanB"/>).</param>
    /// <param name="baselineMeanG">Media canale verde della base calibrata.</param>
    /// <param name="baselineMeanR">Media canale rosso della base calibrata.</param>
    /// <param name="presentThreshold">Sovrascrive <see cref="DefaultPresentThreshold"/>, se un chiamante ne ha calibrata una propria.</param>
    public static DialogWindowReading Read(
        ReadOnlySpan<byte> bgra,
        int width,
        int height,
        double baselineMeanB,
        double baselineMeanG,
        double baselineMeanR,
        double presentThreshold = DefaultPresentThreshold)
    {
        if (width < MinWidth || height < MinHeight)
            return Unreadable("crop_too_small");

        // In long arithmetic, non int -- stessa disciplina di TargetFrameReader:
        // un crop enorme non deve mai avvolgere silenziosamente a una lunghezza
        // attesa piccola e superare un controllo che dovrebbe fallire.
        long expected = (long)width * height * 4;
        if (bgra.Length != expected)
            return Unreadable("crop_truncated");

        long pixelCount = (long)width * height;
        long sumB = 0, sumG = 0, sumR = 0;
        for (int i = 0; i < bgra.Length; i += 4)
        {
            sumB += bgra[i];
            sumG += bgra[i + 1];
            sumR += bgra[i + 2];
        }

        double meanB = sumB / (double)pixelCount;
        double meanG = sumG / (double)pixelCount;
        double meanR = sumR / (double)pixelCount;

        double db = meanB - baselineMeanB;
        double dg = meanG - baselineMeanG;
        double dr = meanR - baselineMeanR;
        double divergence = Math.Sqrt(db * db + dg * dg + dr * dr);

        DialogWindowState state = divergence > presentThreshold
            ? DialogWindowState.Present
            : DialogWindowState.Absent;

        return new DialogWindowReading(state, divergence, null);
    }

    private static DialogWindowReading Unreadable(string reason) => new(DialogWindowState.Unreadable, null, reason);
}
