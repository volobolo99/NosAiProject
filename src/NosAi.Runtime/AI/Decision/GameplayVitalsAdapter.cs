// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// AI — Wiring: memory PlayerVitals (Fase 2) -> decision facts
// ============================================================================
//
// This is the observe->decide seam for the player's own HP and MP. It is a
// faithful translator, not a second opinion: it mirrors the phase's own
// classification and never invents trust the phase withheld.
//
// PlayerVitalsCandidate.Source is Unknown by construction — Fase 2 grants a
// trusted value only once memory and the wire concord in a real session, and
// nothing in that type can grant it. So today every fact this adapter produces
// is UNKNOWN, and the decision rules that read player.hp_ratio / player.mp_ratio
// stay dormant. That is the correct, honest behaviour: the runtime must not act
// on numbers the phase has not established. The day Fase 2 graduates its Source
// (concordance recorded), the ratio flows with that provenance and nothing here
// or in the rules changes.
//
// Stateless on purpose: it never caches a previous reading, so the "cache that
// silently becomes a fallback when the current read is refused" trap cannot
// exist here — a refusal is a refusal, mapped straight to UNKNOWN.

using System;
using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.AI.Decision;

/// <summary>Maps a memory <see cref="PlayerVitalsCandidate"/> into decision facts.</summary>
public static class GameplayVitalsAdapter
{
    /// <summary>Fact key: the player's HP as a 0..1 ratio.</summary>
    public const string PlayerHpRatioFact = "player.hp_ratio";

    /// <summary>Fact key: the player's MP as a 0..1 ratio.</summary>
    public const string PlayerMpRatioFact = "player.mp_ratio";

    /// <summary>Reason attached when a max value is zero (belt-and-braces; the phase also refuses this).</summary>
    public const string MaxZeroReason = "player_vitals_max_zero";

    /// <summary>Writes player.hp_ratio and player.mp_ratio into the context.</summary>
    public static DecisionContext Populate(DecisionContext context, PlayerVitalsCandidate vitals)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.With(PlayerHpRatioFact, MapHp(vitals));
        context.With(PlayerMpRatioFact, MapMp(vitals));
        return context;
    }

    /// <summary>The HP ratio fact for a candidate, classified as the phase classifies it.</summary>
    public static ClassifiedValue<double> MapHp(PlayerVitalsCandidate vitals)
        => RatioCore(vitals.Hp, vitals.MaxHp, vitals.Source, vitals.HasValue, vitals.Reason);

    /// <summary>The MP ratio fact for a candidate, classified as the phase classifies it.</summary>
    public static ClassifiedValue<double> MapMp(PlayerVitalsCandidate vitals)
        => RatioCore(vitals.Mp, vitals.MaxMp, vitals.Source, vitals.HasValue, vitals.Reason);

    /// <summary>
    /// The core translation, shared by HP and MP.
    /// </summary>
    /// <remarks>
    /// <paramref name="hasValue"/> is the phase's "structural read succeeded" flag;
    /// <paramref name="source"/> is the phase's trust verdict. A fact carries a
    /// number only when the read succeeded AND the phase trusts it (Source is not
    /// Unknown). Every other case — a refusal, or a structurally-read-but-not-yet-
    /// established value — becomes UNKNOWN with the phase's own reason, so a rule
    /// is skipped rather than run against an unestablished number.
    /// </remarks>
    internal static ClassifiedValue<double> RatioCore(uint value, uint max, DataSourceKind source, bool hasValue, string reason)
    {
        if (!hasValue) return ClassifiedValue<double>.Unknown(reason);
        if (max == 0) return ClassifiedValue<double>.Unknown(MaxZeroReason);

        double ratio = Math.Clamp(value / (double)max, 0.0, 1.0);
        return source switch
        {
            DataSourceKind.Live => ClassifiedValue<double>.Live(ratio),
            DataSourceKind.Derived => ClassifiedValue<double>.Derived(ratio),
            DataSourceKind.Cached => ClassifiedValue<double>.Cached(ratio, DateTime.UtcNow),
            DataSourceKind.Simulated => ClassifiedValue<double>.Simulated(ratio),
            // Unknown: the numbers are present but the phase has not established
            // them. The value is deliberately dropped, not passed through dressed
            // as a fact.
            _ => ClassifiedValue<double>.Unknown(reason),
        };
    }
}
