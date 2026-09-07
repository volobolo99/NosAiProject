using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NosAi.ControlPanel;
using NosAi.Runtime.GameData;
using Xunit;

namespace NosAi.ControlPanel.Tests;

/// <summary>
/// A fact that runs only where a recorded capture is present under <c>data/</c>.
/// </summary>
/// <remarks>
/// Stessa politica di <c>NosAi.Runtime.Tests.RecordedCaptureFactAttribute</c> e
/// per la stessa ragione: <c>data/</c> è gitignored — un <c>.noscap</c> è la
/// sessione reale di un operatore e il repository non la porta — e un
/// <c>return</c> anticipato non è un salto: xUnit registrerebbe il test come
/// <b>superato</b> su ogni macchina che la registrazione non ce l'ha. È ripetuta
/// qui invece di condivisa perché i due progetti di test non si referenziano, ed
/// è la stessa scelta già fatta per <c>QuiescedMachineFactAttribute</c>.
/// </remarks>
public sealed class WireCaptureFactAttribute : FactAttribute
{
    public WireCaptureFactAttribute(string recording)
    {
        if (Resolve(recording) is null)
            Skip = $"Registrazione assente: {recording} non trovata sotto data/ (data/ e' gitignored).";
    }

    /// <summary>The full path of that recording, or null when there is none.</summary>
    public static string? Resolve(string recording)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;

        if (directory is null) return null;
        string path = Path.Combine(directory.FullName, "data", recording);
        return File.Exists(path) ? path : null;
    }
}

/// <summary>
/// Il pannello mostra insieme quello che il filo ha detto e quello che il
/// catalogo contiene — e non li dichiara la stessa cosa.
/// </summary>
/// <remarks>
/// <para>
/// <b>Il difetto che questi test sorvegliano.</b> T-16 è aperto perché l'id di
/// <c>sayi</c> non indicizza il testo del client: verificato due volte, come
/// chiave diretta e a scarto costante. Un accostamento fra un id e una riga che
/// «sembra giusta» è la scorciatoia che chiuderebbe T-16 sbagliando, e questa
/// classe di aiuto è esattamente il posto dove quella scorciatoia potrebbe
/// entrare. I test qui fissano che non ci sia: nessun metodo dice quale riga
/// corrisponda a quale id.
/// </para>
/// </remarks>
public sealed class WireMessageInspectTests
{
    private const string Messages = "messaggi.noscap";

    [Fact]
    public void Una_cattura_che_non_esiste_e_un_motivo_e_non_un_elenco_vuoto()
    {
        WireMessageRead read = WireMessageInspect.Read(
            Path.Combine(Path.GetTempPath(), $"assente-{Guid.NewGuid():N}.noscap"), "nota", "IT");

        Assert.False(read.Ok);
        Assert.Contains("non è sul disco", read.Reason, StringComparison.Ordinal);
        // Un elenco vuoto con Ok=true direbbe «il filo non ha detto niente», che
        // è un'affermazione diversa e falsa.
        Assert.Empty(read.Messages);
    }

    [Fact]
    public void Le_parole_corte_non_si_cercano_perche_non_restringono()
    {
        IReadOnlyList<string> words = WireMessageInspect.WordsOf("Hai raccolto la Fionda in legno!");

        Assert.Contains("raccolto", words);
        Assert.Contains("fionda", words);
        Assert.DoesNotContain("hai", words);
        Assert.DoesNotContain("in", words);
        Assert.DoesNotContain("la", words);
    }

    /// <summary>Le più lunghe per prime, e mai due volte la stessa.</summary>
    [Fact]
    public void Le_parole_sono_ordinate_dalla_piu_lunga_e_senza_ripetizioni()
    {
        IReadOnlyList<string> words = WireMessageInspect.WordsOf(
            "raccolto raccolto equipaggiamento pozione");

        Assert.Equal(new[] { "equipaggiamento", "raccolto", "pozione" }, words);
    }

    [Fact]
    public void Una_nota_vuota_non_produce_parole()
    {
        Assert.Empty(WireMessageInspect.WordsOf(""));
        Assert.Empty(WireMessageInspect.WordsOf("   \r\n  "));
        Assert.Empty(WireMessageInspect.WordsOf("ho un po' di hp"));
    }

    /// <summary>
    /// Senza nota il blocco lo dice, invece di mostrare solo gli id e sembrare
    /// completo.
    /// </summary>
    [Fact]
    public void Senza_nota_il_blocco_dice_che_manca_la_meta_che_conta()
    {
        var read = new WireMessageRead(
            true, "",
            new[] { new WireMessageRow("sayi", 975, 2, 8, "Fionda in legno") },
            Array.Empty<GameTextRow>(),
            Array.Empty<string>());

        string text = WireMessageInspect.Describe(read);

        Assert.Contains("975", text, StringComparison.Ordinal);
        Assert.Contains("T-16", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Il blocco dice che un accostamento non è una prova — ed è la riga che
    /// impedisce di leggerlo come un lookup.
    /// </summary>
    [Fact]
    public void Il_blocco_dichiara_che_accostare_non_e_provare()
    {
        var read = new WireMessageRead(
            true, "",
            new[] { new WireMessageRow("sayi", 975, 2, 8, "Fionda in legno") },
            new[] { new GameTextRow("10666", "Hai raccolto [%s]:") },
            new[] { "raccolto" });

        string text = WireMessageInspect.Describe(read);

        Assert.Contains("10666", text, StringComparison.Ordinal);
        Assert.Contains("non è una prova", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Sulla registrazione reale: quattro <c>sayi</c>, e i due argomenti che il
    /// pacchetto dichiara oggetti sono gli unici con un nome.
    /// </summary>
    /// <remarks>
    /// <para>
    /// I numeri vengono dalla cattura, non da un'attesa. <c>messaggi.noscap</c>
    /// contiene esattamente quattro <c>sayi</c>: 697 con argomento 50 (due volte),
    /// 654 con argomento 13, e 975 con argomento 8. Il campo 5 vale <c>2</c> sui
    /// due che portano un vnum di oggetto e <c>4</c> sugli altri due.
    /// </para>
    /// <para>
    /// <b>Il campo 5 è il punto.</b> Senza guardarlo, l'argomento 50 di 697
    /// verrebbe cercato nel catalogo degli oggetti e ne uscirebbe un nome
    /// plausibile e falso, accanto agli altri e indistinguibile da loro.
    /// </para>
    /// </remarks>
    [WireCaptureFact(Messages)]
    public void Sulla_registrazione_reale_i_quattro_sayi_si_leggono()
    {
        string path = WireCaptureFactAttribute.Resolve(Messages)!;

        WireMessageRead read = WireMessageInspect.Read(path, "Hai raccolto la Fionda", "IT");

        Assert.True(read.Ok, read.Reason);
        Assert.Equal(4, read.Messages.Count);
        Assert.All(read.Messages, m => Assert.Equal("sayi", m.Opcode));

        Assert.Equal(new[] { 697, 654, 697, 975 }, read.Messages.Select(m => m.MessageId));
        Assert.Equal(new[] { 50, 13, 50, 8 }, read.Messages.Select(m => m.Argument));

        // Nessun nome e' mai vuoto: dove il catalogo non risponde, c'e' scritto il
        // perche'. E dove il campo 5 non dichiara un oggetto, il perche' e' quello.
        Assert.All(read.Messages, m => Assert.False(string.IsNullOrWhiteSpace(m.ArgumentName)));
        Assert.All(
            read.Messages.Where(m => m.ArgumentType != 2),
            m => Assert.Contains("non è dichiarato un oggetto", m.ArgumentName, StringComparison.Ordinal));
    }
}
