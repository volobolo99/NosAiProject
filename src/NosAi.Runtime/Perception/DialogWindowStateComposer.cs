using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Perception;

/// <summary>One reading of the dialog-window ROI, and when the pixels were captured.</summary>
public readonly record struct DialogWindowObservation(
    DialogWindowReading Reading,
    DateTime ObservedAtUtc);

/// <summary>
/// Turns one <see cref="DialogWindowReading"/> into a classified presence
/// fact. Unlike <see cref="TargetStateComposer"/>, there is no wire-side
/// signal to reconcile against: no NosTale opcode for dialog/quest text was
/// found in this repository (verified by grep on
/// <c>docs/PROTOCOLLO_NOSTALE.md</c>/<c>NosTaleWorldProtocolDecoder.cs</c>,
/// docs/agents/DEEPSEEK_TASKS.md "Candidati da investigare") -- the screen
/// is the only source there is, so this composer only translates the
/// reading's own state, it never contradicts or confirms it against
/// anything else.
/// </summary>
/// <remarks>
/// <b>Precisazione del 2026-09-07.</b> «Nessun opcode per il testo» resta vero,
/// e non vuol dire che il testo non ci sia: <c>NSlangData_&lt;LANG&gt;.NOS</c>
/// contiene <c>_code_&lt;lang&gt;_quest.txt</c> e
/// <c>_code_&lt;lang&gt;_npctalk.txt</c> — 3639 testi di missione e 22 358
/// battute di NPC, ora importati nel catalogo. Quello che manca non è più il
/// testo: è il <i>collegamento</i> fra un id sul filo e la riga giusta di quelle
/// tabelle, che nessuno ha ancora stabilito. È una distanza più corta di quella
/// che questa classe descriveva, e resta una distanza: finché il collegamento
/// non è misurato, lo schermo è ancora la sola fonte sulla presenza di un
/// pannello.
/// </remarks>
public static class DialogWindowStateComposer
{
    /// <summary>The reader failed and said why, but the reason was lost on the way.</summary>
    public const string UnreadableReason = "dialog_window_unreadable";

    /// <summary>
    /// Composes whether a dialog window is open. An uncalibrated ROI reports
    /// <see cref="DialogRoiCalibration.NotCalibratedReason"/> before the
    /// reading is even inspected -- the same "checked before the reading"
    /// discipline <see cref="TargetStateComposer.Compose"/> already applies,
    /// for the same reason: an unaimed reader must never publish a confident
    /// <see langword="false"/> over pixels nobody confirmed.
    /// </summary>
    public static ClassifiedValue<bool> Compose(DialogRoiCalibration calibration, DialogWindowObservation observation)
    {
        ArgumentNullException.ThrowIfNull(calibration);

        if (!calibration.IsCalibrated)
            return ClassifiedValue<bool>.Unknown(DialogRoiCalibration.NotCalibratedReason, observedAtUtc: observation.ObservedAtUtc);

        DialogWindowReading reading = observation.Reading;
        return reading.State switch
        {
            DialogWindowState.Present => ClassifiedValue<bool>.Derived(true, observation.ObservedAtUtc),
            DialogWindowState.Absent => ClassifiedValue<bool>.Derived(false, observation.ObservedAtUtc),
            _ => ClassifiedValue<bool>.Unknown(reading.FailureReason ?? UnreadableReason, observedAtUtc: observation.ObservedAtUtc)
        };
    }
}
