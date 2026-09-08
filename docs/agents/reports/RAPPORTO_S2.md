# RAPPORTO S2 — il pannello ignora nove campi su quattordici che riceve

## File creati e modificati

- M `src/NosAi.ControlPanel/GameplayWireReader.cs` — legge tutti e 14 i campi del baseline (ne leggeva 5)
- M `src/NosAi.ControlPanel/CombatInspect.cs` — riga "Ultimo bersaglio" (il *quale* accanto al *se*, ADR-0018)
- M `src/NosAi.ControlPanel/MainWindow.xaml` — card "Riquadri slot equipaggiamento (schermo)"
- M `src/NosAi.ControlPanel/MainWindow.xaml.cs` — wiring `ApplyInventoryPanelRoi()`
- A `src/NosAi.ControlPanel/InventoryPanelInspect.cs` — vista ROI pannello, sul modello di `TargetInspect`
- A `tests/NosAi.ControlPanel.Tests/GameplayWireReaderTests.cs`
- A `tests/NosAi.ControlPanel.Tests/InventoryPanelInspectTests.cs` (contiene anche `CombatSelectedTargetTests`)

## Build e test

- `dotnet build src/NosAi.ControlPanel -c Debug` → 0 errori, 0 warning
- `dotnet build tests/NosAi.ControlPanel.Tests -c Debug` → 0 errori, 0 warning
- `dotnet test tests/NosAi.ControlPanel.Tests -c Debug` → **Superati: 156. Non superati: 0.**

## Le misure

| Cosa | Risultato |
|---|---|
| Campi pubblicati da `GameplayObservation.ToWire()` | 14 |
| Campi letti da `GameplayWireReader` prima | 5 |
| Campi letti ora | 14 (aggiunti: `inventory`, `selectedTarget`, `groundItems`, `lastPickup`, `skillsReady`, `inCombat`) |
| `data/perception/inventory-panel-roi.calibration` | 18 slot, 1024x768, 2026-09-06 — ora visibile |

## Cosa è restato UNKNOWN e perché

- Ogni campo assente dal JSON resta `Unknown` col motivo del produttore (`not_published_by_provider`; per mappa/cella i motivi già esistenti). Lista vuota = assenza guardata, non UNKNOWN.
- `lastPickup.byPlayer` resta `null` quando il filo non lo porta: null non è false.
- La provenienza per-membro delle liste (`inventory`/`groundItems`/`skillsReady`) non è sul filo: ogni membro è timbrato con la provenienza della lista (la più debole). Non inventata, documentata nel codice.

## Dove mi sono fermato

- Lettura e formattazione dei 6 campi + `selectedTarget`: fatte e testate. Il wiring nelle pagine live (Attorno/Equipaggiamento) richiede che `SnapshotView` (e, per il percorso collegato, `AttachedSnapshot`) trasportino i campi: **non sono nella mia lista file S2**. Non li ho toccati.
- Nel pannello live la riga "Ultimo bersaglio" resta `UNKNOWN · selected_target_not_on_panel_snapshot` finché `SnapshotView` non espone `SelectedTarget`. Motivo onesto: usare `not_published_by_provider` sarebbe falso, il runtime lo pubblica.
- Il wiring della ROI (`InventoryPanelInspect`) è completo: legge il file direttamente, non passa da `SnapshotView`.

## Spec vs codice

- La spec dice "leggerli e mostrarli". Il codice (ownership S2) non include `SnapshotView.cs`/`AttachedSnapshot.cs`, gli unici punti dove quei campi arrivano a `MainWindow`. Ho seguito il codice: lettura e formattazione complete, wiring live bloccato e riportato.

## Altre sessioni nel working tree (non toccati, non committati)

`src/NosAi.Runtime/` (S1/S3): `GameTrafficObserver.cs`, `NosTaleWorldProtocolDecoder.cs`, `Program.cs`, `GameData/MonsterCatalogue.cs`, `Observability/MonsterReportCommand.cs`; test `tests/NosAi.Runtime.Tests/…` e `zz_item_measure.txt`. Lasciati dov'erano.
