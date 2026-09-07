using System.Globalization;
using NosAi.Core.WorldModel.Certification;

namespace NosAi.Runtime.Testing;

/// <summary>
/// Runs every certification suite and prints AP-10's per-stage scorecard:
/// <c>--certification-report</c>.
/// </summary>
/// <remarks>
/// <para>
/// The suites already answered pass/fail per gate, and
/// <c>GateCertificationRunner</c> already collected the individual checks. What
/// nothing did was turn that into the thing a certification is for: which of the
/// fourteen stages is weak, what backs each claim, and what would raise it.
/// </para>
/// <para>
/// It never returns a passing exit code on the strength of green suites alone.
/// The report's own ceiling is <see cref="VerificationLevel.Integrated"/>
/// (<see cref="CertificationReportBuilder"/> explains why), so
/// <c>IsFullyCertified</c> is structurally unreachable from here -- and the exit
/// code says whether every suite that was run passed, which is a smaller and
/// truer claim than "certified".
/// </para>
/// </remarks>
public static class CertificationReportCommand
{
    /// <summary>The operator flag.</summary>
    public const string Flag = "--certification-report";

    /// <summary>Runs every suite, prints the report, and returns 0 when all passed.</summary>
    public static async Task<int> RunAsync(CancellationToken token = default)
    {
        var passed = new Dictionary<string, bool>(StringComparer.Ordinal);

        foreach (CertificationSuite suite in CertificationSuites.All)
        {
            bool ok;
            try
            {
                ok = await suite.Run().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A suite that throws has not passed, and saying which exception
                // beats a bare false: the next reader needs to know whether the
                // stage is weak or the harness is.
                Console.WriteLine($"[ERRORE] suite {suite.Key}: {ex.GetType().Name}: {ex.Message}");
                ok = false;
            }

            passed[suite.Key] = ok;
            token.ThrowIfCancellationRequested();
        }

        CertificationReport report = CertificationReportBuilder.Build(passed, DateTime.UtcNow);
        Print(report);

        return passed.Values.All(v => v) ? 0 : 1;
    }

    /// <summary>One block per stage: level, evidence, and every blocker by name.</summary>
    /// <param name="report">A report from <see cref="CertificationReportBuilder.Build"/>.</param>
    public static void Print(CertificationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        Console.WriteLine("=== Certificazione per stadio (AP-10) ===");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"compilato: {report.ObservedAtUtc:O}, stadi: {report.Stages.Count}"));

        foreach (CertificationStageResult stage in report.Stages)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {stage.Stage,-22} {stage.Level,-11} evidenza: {stage.Evidence}"));
            foreach (string blocker in stage.Blockers)
                Console.WriteLine($"      - {blocker}");
        }

        Console.WriteLine(report.OverallLevel is { } overall
            ? $"livello complessivo: {overall} (il piu' debole degli stadi)"
            : "livello complessivo: nessuno stadio riportato");

        // Said out loud, because the one thing a certification must never do is
        // let a green run read as a certified product.
        Console.WriteLine(
            "certificato del tutto: no -- e non e' raggiungibile da qui: Verified richiede "
            + "la validazione sul client reale, che nessuna suite di questo processo puo' fornire.");
    }
}
