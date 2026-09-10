using NosAi.Runtime.GameData;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Un fatto che gira solo dove il volume dedicato NOSAI-SSD e' montato.
/// </summary>
/// <remarks>
/// <para>
/// Serve accanto a <see cref="NosTaleClientFactAttribute"/>, non al suo posto: i due
/// cancelli guardano risorse diverse. Quello controlla la cartella degli archivi del
/// client, questo il volume dove l'importatore scrive <c>reference.db</c>.
/// </para>
/// <para>
/// Il 2026-09-11 due test erano marcati <c>[NosTaleClientFact]</c> ma leggevano il
/// catalogo di riferimento: su una macchina con il client installato e il volume non
/// montato il cancello non scattava, e i test fallivano con <c>nosai_ssd_not_found</c>.
/// Un fallimento diceva "il prodotto e' rotto" quando il fatto vero era "il disco non
/// c'e'": due affermazioni diverse, e la prima falsa.
/// </para>
/// <para>
/// Vale qui la stessa disciplina dell'altro attributo: un salto non e' mai la prova
/// che i dati reali siano stati letti. E' l'ammissione che non lo sono stati.
/// </para>
/// </remarks>
public sealed class NosAiVolumeFactAttribute : FactAttribute
{
    public NosAiVolumeFactAttribute()
    {
        // Si interroga il prodotto invece di reimplementare la ricerca del volume:
        // se domani la risoluzione cambia, il cancello la segue da solo.
        GameReferenceLocation location = GameReferenceLocator.Locate();
        if (!location.Exists)
            Skip = $"Catalogo di riferimento non disponibile ({location.FailureReason ?? "motivo non riferito"}): " +
                   $"serve il volume con etichetta {GameReferenceLocator.VolumeLabel} " +
                   $"e il file {GameReferenceLocator.FileName} sotto NosAi\\data.";
    }
}
