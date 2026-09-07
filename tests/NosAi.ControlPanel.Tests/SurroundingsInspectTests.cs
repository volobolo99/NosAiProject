using System.IO;
using NosAi.ControlPanel;
using NosAi.LiveIntegration;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using Xunit;

namespace NosAi.ControlPanel.Tests;

/// <summary>
/// Surroundings: a populated list, an observed empty map, and no observation at
/// all are three drawings. Age is part of the drawing.
/// </summary>
public sealed class SurroundingsInspectTests
{
    private static readonly DateTime Now = new(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void APopulatedListShowsIdPositionLifeAndAge()
    {
        var entities = ClassifiedValue<IReadOnlyList<SelectableEntity>>.Live(
        [
            new SelectableEntity(101, new MapPoint(12, 8), 0.75, Now),
            new SelectableEntity(102, new MapPoint(3, 4), null, Now.AddSeconds(-30))
        ]);

        SurroundingsView view = SurroundingsInspect.Inspect(entities, Now);

        Assert.Equal(SurroundingsKind.Populated, view.Kind);
        Assert.Equal(2, view.Rows.Count);
        Assert.Equal(101, view.Rows[0].EntityId);
        Assert.Equal("12,8", view.Rows[0].Position);
        Assert.Equal("75%", view.Rows[0].Life);
        Assert.Equal("0s", view.Rows[0].Age);
        Assert.Equal(102, view.Rows[1].EntityId);
        Assert.Contains(SurroundingsInspect.HpNotStated, view.Rows[1].Life, StringComparison.Ordinal);
        Assert.Equal("30s", view.Rows[1].Age);
        Assert.NotEqual(view.Rows[0].Age, view.Rows[1].Age);
        Assert.Contains(SurroundingsInspect.VnumNotOnObservation, view.Rows[0].Vnum, StringComparison.Ordinal);
        Assert.Contains(SurroundingsInspect.VnumNotOnObservation, view.Rows[0].Name, StringComparison.Ordinal);
        Assert.DoesNotContain(view.Fields, f => f.Value == "0");
    }

    [Fact]
    public void AnObservedEmptyListIsNoEntitiesAroundNotUnknown()
    {
        var entities = ClassifiedValue<IReadOnlyList<SelectableEntity>>.Live(
            Array.Empty<SelectableEntity>());

        SurroundingsView view = SurroundingsInspect.Inspect(entities, Now);

        Assert.Equal(SurroundingsKind.NoEntitiesAround, view.Kind);
        Assert.Equal(SurroundingsInspect.NoEntitiesAroundLabel, view.Summary);
        Assert.DoesNotContain("UNKNOWN", view.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(SurroundingsInspect.NoObservationLabel, view.Summary, StringComparison.Ordinal);
        Assert.Empty(view.Rows);
        Assert.Equal("DERIVED", view.Fields[0].Source);
    }

    [Fact]
    public void AnUnpublishedListIsNoObservationNotAnEmptyMap()
    {
        SurroundingsView view = SurroundingsInspect.Inspect(
            ClassifiedValue<IReadOnlyList<SelectableEntity>>.Unknown(GameplayObservation.NotPublishedReason),
            Now);

        Assert.Equal(SurroundingsKind.NoObservation, view.Kind);
        Assert.Contains(SurroundingsInspect.NoObservationLabel, view.Summary, StringComparison.Ordinal);
        Assert.Contains(GameplayObservation.NotPublishedReason, view.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(SurroundingsInspect.NoEntitiesAroundLabel, view.Summary, StringComparison.Ordinal);
        Assert.Empty(view.Rows);
        Assert.Equal("UNKNOWN", view.Fields[0].Source);
    }

    [Fact]
    public void ANullListIsNoObservation()
    {
        SurroundingsView view = SurroundingsInspect.Inspect(null, Now);
        Assert.Equal(SurroundingsKind.NoObservation, view.Kind);
        Assert.Contains(SurroundingsInspect.NoObservationLabel, view.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(SurroundingsInspect.NoEntitiesAroundLabel, view.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void TheViewHasNoWritePathIntoTheRuntime()
    {
        string source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "NosAi.ControlPanel", "SurroundingsInspect.cs"));
        AssertNoWrite(source);
    }

    internal static void AssertNoWrite(string source)
    {
        string[] forbidden =
        [
            "TryBeginActuation", "GatedInputBackend", "Win32InputBackend", "SendInput",
            "mouse_event", "keybd_event", "PostMessage", "WriteProcessMemory",
            "RequestHalt", "ImmediateHalt", "/api/command", "File.Write",
            "WriteAllBytes", "WriteAllText", "ArmInput", "BeginSession",
            "IsActuating = true"
        ];
        foreach (string token in forbidden)
            Assert.DoesNotContain(token, source, StringComparison.Ordinal);
    }

    internal static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        Assert.True(directory is not null, "Repository root not found.");
        return directory!.FullName;
    }
}

/// <summary>
/// Specie e vnum: quello che il pannello aveva e non mostrava.
/// </summary>
/// <remarks>
/// <para>
/// <b>Da cosa nasce.</b> Il 2026-09-08 il decoder ha cominciato a leggere anche
/// le entità di tipo 2 e il movimento del tipo 1, e l'avvistamento porta ora la
/// specie. Senza queste righe l'operatore vedrebbe un NPC, un pet e un mostro
/// disegnati identici.
/// </para>
/// <para>
/// <b>E un difetto trovato lì accanto</b>: lo snapshot HTTP pubblica il vnum da
/// sempre, ma <c>GameplayWireReader.TryEntity</c> lo scartava, quindi il pannello
/// scriveva <c>vnum_not_on_observation</c> anche per entità il cui numero era nel
/// JSON.
/// </para>
/// </remarks>
public sealed class SurroundingsSpeciesTests
{
    private static readonly DateTime Now = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private static SurroundingsView Inspect(params SelectableEntity[] entities) =>
        SurroundingsInspect.Inspect(
            ClassifiedValue<IReadOnlyList<SelectableEntity>>.Live(entities), Now);

    [Fact]
    public void LaSpecieDichiarataSiVede()
    {
        SurroundingsView view = Inspect(
            new SelectableEntity(101, new MapPoint(12, 8), 0.75, Now, Vnum: 36,
                Vitals: null, Kind: "Monster"),
            new SelectableEntity(102, new MapPoint(13, 9), 1.0, Now, Vnum: 1488,
                Vitals: null, Kind: "Bystander"),
            new SelectableEntity(103, new MapPoint(14, 10), null, Now,
                Vnum: null, Vitals: null, Kind: "Player"));

        Assert.Equal("Monster", view.Rows[0].Species);
        Assert.Equal("Bystander", view.Rows[1].Species);
        Assert.Equal("Player", view.Rows[2].Species);
    }

    /// <summary>Specie non dichiarata non diventa «mostro» per omissione.</summary>
    [Fact]
    public void SenzaSpecieCompareIlMotivo()
    {
        SurroundingsView view = Inspect(
            new SelectableEntity(101, new MapPoint(12, 8), 0.75, Now));

        Assert.Contains(
            SurroundingsInspect.SpeciesNotOnObservation, view.Rows[0].Species,
            StringComparison.Ordinal);
    }

    /// <summary>Il vnum osservato si mostra invece di essere buttato via.</summary>
    [Fact]
    public void IlVnumOsservatoSiVede()
    {
        SurroundingsView view = Inspect(
            new SelectableEntity(101, new MapPoint(12, 8), 0.75, Now, Vnum: 36));

        Assert.Equal("36", view.Rows[0].Vnum);

        // Il nome resta ignoto, ma per un motivo diverso: il vnum c'e', manca il
        // catalogo. Due ignoranze diverse non si scrivono uguali.
        Assert.Contains(
            SurroundingsInspect.NameNeedsCatalogue, view.Rows[0].Name,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            SurroundingsInspect.VnumNotOnObservation, view.Rows[0].Name,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SenzaVnumIlMotivoResta()
    {
        SurroundingsView view = Inspect(
            new SelectableEntity(101, new MapPoint(12, 8), 0.75, Now));

        Assert.Contains(
            SurroundingsInspect.VnumNotOnObservation, view.Rows[0].Vnum,
            StringComparison.Ordinal);
        Assert.Contains(
            SurroundingsInspect.VnumNotOnObservation, view.Rows[0].Name,
            StringComparison.Ordinal);
    }

    /// <summary>La riga che l'operatore legge porta la specie per prima.</summary>
    [Fact]
    public void LaRigaMostrataPortaLaSpecie()
    {
        SurroundingsView view = Inspect(
            new SelectableEntity(101, new MapPoint(12, 8), 0.75, Now, Vnum: 36,
                Vitals: null, Kind: "Bystander"));

        Assert.Contains("specie=Bystander", view.Fields[0].Value, StringComparison.Ordinal);
        Assert.Contains("vnum=36", view.Fields[0].Value, StringComparison.Ordinal);
    }
}
