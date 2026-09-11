using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NosAi.ControlPanel;

internal static class CombatChainCard
{
    public enum EngageLineKind
    {
        Refused,
        Action,
        Other,
    }

    private const string MobsPrefix = "mobs:";
    private const string EngagePrefix = "engage:";
    private const string CandidatePrefix = "candidate:";
    private const string RefusalMarker = "[REFUSED]";
    private const string WouldActMarker = "would_act=True";

    public static string DescribeElevation(bool elevated)
    {
        return elevated
            ? "Esecuzione con privilegi di amministratore: i rifiuti che vedrai sono reali e vanno presi come evidenza."
            : "Nessun privilegio di amministratore: ogni rifiuto successivo non è evidenza di nulla finché la Control Panel non viene rilanciata come amministratore.";
    }

    public static string SummariseCombatReport(string output)
    {
        var lines = (output ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        string? mobsLine = null;
        var engageLines = new List<string>();
        var candidateCount = 0;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith(MobsPrefix, StringComparison.Ordinal))
            {
                mobsLine ??= line;
            }
            else if (line.StartsWith(EngagePrefix, StringComparison.Ordinal))
            {
                engageLines.Add(line);
            }
            else if (line.StartsWith(CandidatePrefix, StringComparison.Ordinal))
            {
                candidateCount++;
            }
        }

        var wouldActCount = engageLines.Count(l => l.Contains(WouldActMarker, StringComparison.Ordinal));

        var mobsPart = mobsLine ?? "riga mobs: non stampata";
        var engagePart = engageLines.Count == 0 ? "righe engage: nessuna" : $"righe engage: {engageLines.Count}";
        var candidatePart = candidateCount == 0 ? "righe candidate: nessuna" : $"righe candidate: {candidateCount}";
        var wouldActPart = engageLines.Count == 0
            ? "engage would_act=True: non contabile, nessuna riga engage"
            : $"engage would_act=True: {wouldActCount}";

        return string.Join(" | ", mobsPart, engagePart, candidatePart, wouldActPart);
    }

    public static bool TryValidateEngage(string? targetId, string? skillId, string? rounds, out int roundCount, out string? refusal)
    {
        roundCount = 0;

        if (string.IsNullOrWhiteSpace(targetId))
        {
            refusal = "Target vuoto: inserisci l'entity id nel campo target.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(skillId))
        {
            refusal = "Skill vuota: inserisci l'intento skill.<id> nel campo skill.";
            return false;
        }

        if (!int.TryParse(rounds, NumberStyles.Integer, CultureInfo.InvariantCulture, out roundCount))
        {
            refusal = "Round non valido: il campo round deve contenere un intero.";
            return false;
        }

        if (roundCount < 1 || roundCount > 60)
        {
            refusal = "Round fuori intervallo: il campo round accetta valori da 1 a 60.";
            return false;
        }

        refusal = null;
        return true;
    }

    // Le righe di watch segnano i rifiuti con [REFUSED]; una pressione avvenuta
    // è riportata con would_act=True. Un rifiuto e un'azione sono fatti distinti.
    public static EngageLineKind ClassifyEngageLine(string line)
    {
        var text = line ?? string.Empty;

        if (text.Contains(RefusalMarker, StringComparison.Ordinal))
        {
            return EngageLineKind.Refused;
        }

        if (text.Contains(WouldActMarker, StringComparison.Ordinal))
        {
            return EngageLineKind.Action;
        }

        return EngageLineKind.Other;
    }
}
