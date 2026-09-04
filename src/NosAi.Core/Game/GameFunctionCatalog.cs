namespace NosAi.Core.Game;

public enum GameFunctionKind
{
    Movement,
    Combat,
    Character,
    Interaction,
    Inventory
}

public sealed record GameFunctionDefinition(
    string Id,
    GameFunctionKind Kind,
    string Description,
    bool RequiresTarget,
    bool RequiresFreshObservation,
    double MinimumConfidence);

public static class GameFunctionCatalog
{
    public static IReadOnlyList<GameFunctionDefinition> All { get; } =
    [
        new("movement.move", GameFunctionKind.Movement, "Move the character toward an observed position.", false, true, 0.85),
        new("movement.stop", GameFunctionKind.Movement, "Stop character movement.", false, true, 0.95),
        new("combat.basic_attack", GameFunctionKind.Combat, "Perform the normal client-side attack against an observed target.", true, true, 0.90),
        new("combat.skill", GameFunctionKind.Combat, "Use an available client-side skill against an observed target.", true, true, 0.90),
        new("interaction.interact", GameFunctionKind.Interaction, "Interact with an observed client-visible entity.", true, true, 0.90),
        new("interaction.pickup", GameFunctionKind.Interaction, "Pick up an observed item through the client UI/action path.", true, true, 0.92),
        new("inventory.use_item", GameFunctionKind.Inventory, "Use an observed usable inventory item.", true, true, 0.95)
    ];

    public static bool TryGet(string id, out GameFunctionDefinition? definition)
    {
        definition = All.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));
        return definition is not null;
    }
}
