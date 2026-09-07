using NosAi.ControlPanel;
using NosAi.Runtime.Autonomy;
using Xunit;

namespace NosAi.ControlPanel.Tests;

/// <summary>
/// Il lettore dello snapshot non butta via i campi che lo snapshot pubblica.
/// </summary>
/// <remarks>
/// <c>vnum</c> era nel JSON da sempre e <c>GameplayWireReader.TryEntity</c> lo
/// scartava, quindi la vista Attorno scriveva <c>vnum_not_on_observation</c> per
/// entità il cui numero era lì. <c>kind</c> è arrivato il 2026-09-08 con la
/// specie letta sul filo.
/// </remarks>
public sealed class GameplayWireEntityTests
{
    private static SnapshotView Parse(string entitiesJson) => AttachedSnapshot.Parse($$"""
        {
          "contractVersion": "gate1.snapshot.v1",
          "client": {
            "gameplayBaseline": {
              "source": "DERIVED",
              "hasObservedValue": true,
              "value": {
                "entities": {
                  "source": "LIVE",
                  "hasObservedValue": true,
                  "observedAtUtc": "2026-09-08T12:00:00.0000000Z",
                  "value": {{entitiesJson}}
                }
              }
            }
          }
        }
        """);

    [Fact]
    public void IlVnumELaSpecieArrivanoFinoAllaVista()
    {
        SnapshotView snapshot = Parse(
            """[{"entityId":101,"x":12,"y":8,"hpRatio":0.75,"vnum":36,"kind":"Monster"}]""");

        SelectableEntity entity = Assert.Single(snapshot.Entities.Value);
        Assert.Equal(101, entity.EntityId);
        Assert.Equal(36, entity.Vnum);
        Assert.Equal("Monster", entity.Kind);
    }

    /// <summary>
    /// Uno snapshot più vecchio dei due campi si legge lo stesso, e i due campi
    /// restano nulli: assenti, non inventati.
    /// </summary>
    [Fact]
    public void UnoSnapshotSenzaQuelliCampiSiLeggeLoStesso()
    {
        SnapshotView snapshot = Parse("""[{"entityId":102,"x":3,"y":4}]""");

        SelectableEntity entity = Assert.Single(snapshot.Entities.Value);
        Assert.Equal(102, entity.EntityId);
        Assert.Null(entity.Vnum);
        Assert.Null(entity.Kind);
    }

    /// <summary>Un vnum che non è un numero non diventa un vnum.</summary>
    [Fact]
    public void UnVnumNonNumericoRestaAssente()
    {
        SnapshotView snapshot = Parse(
            """[{"entityId":103,"x":1,"y":2,"vnum":"trentasei","kind":42}]""");

        SelectableEntity entity = Assert.Single(snapshot.Entities.Value);
        Assert.Null(entity.Vnum);
        Assert.Null(entity.Kind);
    }

    /// <summary>Il giro completo: dal JSON alla riga che l'operatore legge.</summary>
    [Fact]
    public void DalJsonAllaRigaMostrata()
    {
        SnapshotView snapshot = Parse(
            """[{"entityId":104,"x":5,"y":6,"hpRatio":1.0,"vnum":1488,"kind":"Bystander"}]""");

        SurroundingsView view = SurroundingsInspect.Inspect(
            snapshot.Entities, new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal("Bystander", view.Rows[0].Species);
        Assert.Equal("1488", view.Rows[0].Vnum);
        Assert.Contains("specie=Bystander", view.Fields[0].Value, StringComparison.Ordinal);
    }
}
