namespace NosAi.ControlPanel;

public sealed record SuiteAction(string Title, string Flag, string Description);

public static class SuiteCatalog
{
    public static IReadOnlyList<SuiteAction> All { get; } =
    [
        new("Gate 1", "--gate1-test", "Canale PC-telefono, classificazione, heartbeat."),
        new("Gate 2", "--gate2-test", "World model e persistenza di sessione."),
        new("Gate 3", "--gate3-test", "Pianificazione su dati osservati."),
        new("Gate 4", "--gate4-test", "Catena SP1–SP8."),
        new("Gate 5", "--gate5-test", "Control center e provenienza."),
        new("Gate 6", "--gate6-test", "Certificazione di rilascio."),
        new("Host", "--host-test", "Master host."),
        new("Storage", "--storage-test", "Infrastruttura di persistenza."),
        new("Navigazione", "--navigation-test", "Pathfinding."),
        new("Gateway", "--gateway-test", "Gateway del control panel."),
        new("Raid", "--raids-test", "Dodekatheon."),
        new("Miniland", "--miniland-test", "Produzione Miniland."),
        new("Local AI", "--localai-test", "Inferenza locale."),
        new("Hardware", "--hardware-test", "Autoscale hardware."),
        new("Economia", "--economy-test", "Inventario."),
        new("Percezione", "--perception-test", "Pipeline di percezione."),
        new("Sicurezza", "--security-test", "Sessione effimera / Noise."),
        new("Probe DXGI", "--dxgi-probe", "Cattura desktop reale. Resta UNKNOWN se non disponibile.")
    ];
}
