# RAPPORTO S4 — il catalogo degli oggetti contro il filo

## File creati e modificati

- A `src/NosAi.Runtime/GameData/ItemCatalogue.cs` — `CataloguedItem`/`ItemCatalogueLookup`/`ItemCatalogue`, sul modello di `SkillCatalogue`
- A `tests/NosAi.Runtime.Tests/GameData/ItemCatalogueTests.cs` — 5 test (3 `NosTaleClientFact`, 1 `RecordedCaptureFact`, 1 `Fact`)
- A `tests/NosAi.Runtime.Tests/ItemCatalogueRefusalTests.cs` — 3 test `Fact` sui rifiuti, sul modello di `SkillCatalogueRefusalTests`

`ItemReferenceDecoder.cs` è nella lista S4 ma non l'ho toccato: era già completo e i suoi 12 test passano. Nessun comando nuovo (`Program.cs` è di S3, la spec S4 lo vieta).

## Build e test

- `dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Debug` → **0 errori, 0 warning**
- `dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj --filter "FullyQualifiedName~ItemCatalogue"` → **Superati: 8. Non superati: 0.**
- `dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj --filter "FullyQualifiedName~RefusalReasonRegisterTests"` → **Superati: 2. Non superati: 0.** (le tre costanti `…Reason` nuove sono coperte)

## Le misure

`Item.dat` decodificato (`data/NosAi.Runtime` → `NSgtdData.NOS`): **7727 record** con vnum, range `[1..138212]`, 83780 righe, 34550 code binarie, 1218 lunghezze non affidabili. Il conteggio combacia con i 7727 di `items.json` già documentati in `docs/research/`.

Il filo (otto catture, `--wire-inspect --timeline`) nomina **14 vnum di oggetto distinti**. Riepilogo per fonte:

| Fonte | Campo | vnum osservati |
|---|---|---|
| `drop` | 1 | 2006, 8 |
| `icon` | 4 | 2006, 8 |
| `ivn` | gruppo `slot.vnum.…` | 2006, 2612, 13, 8, 309, 518, 284 |
| `sayi` | 6 (quando 5=2) | 2006, 2612, 13, 8 |
| `eq` | gruppo puntato | 221, 262, 157, 224, 279 |
| `equip` | gruppo `slot.vnum.…` | 262, 221, 715, 157, 309, 224, 279, 284, 518, 902 |

| vnum | nel catalogo | nome | Slot |
|---|---|---|---|
| 8 | **sì** | Fionda in legno | SecondaryWeapon (5) |
| 13 | **sì** | Uniforme da allenamento | Armor (1) |
| 2612 | **sì** | Amuleto rafforzamento armatura | Amulet (11) |
| 2006 | **no** | — | — |
| 157, 221, 224, 262, 279, 284, 309, 518, 715, 902 | **no** | — | — |

**Il riscontro che regge** (`data/messaggi.noscap`, l'evento intero): `drop 8 …` → `sayi … 2 8 …` → `ivn 0 0.8.…` nominano tutti il vnum 8, e 8 risolve in «Fionda in legno». Tre fonti indipendenti, stesso oggetto. Lo stesso vale per 13 e 2612 (drop/sayi/ivn concordi, nome che risolve).

**Il riscontro che non regge**: 11 vnum su 14 **non esistono** nell'`Item.dat` installato. In particolare `2006`, che il filo nomina 6+ volte in 3 catture come `drop`/`ivn`/`sayi`/`icon` concordi, non è nel catalogo. Nessuno dei vnum di equipaggiamento (`eq`/`equip`) risolve. Non è un difetto di decodifica: il conteggio 7727 è esatto e 8/13/2612 risolvono — quei vnum non stanno proprio nella tabella.

## Cosa ho lasciato `Unknown` e perché

- **Tutti i campi numerici** di `CataloguedItem` (`Price`, `InventoryType`, `ItemType`, `ItemSubType`, `Slot`) → `item_fields_not_carried_by_wire`. Il filo porta il vnum e nient'altro: nessun prezzo, tipo o slot. Nessun campo è riempito «per somiglianza» col campo adiacente.
- **11 vnum su 14** → `item_not_in_catalogue`. La tabella non li contiene, e il catalogo lo dice per nome invece di inventare un oggetto.
- `Slot` ha la sola conferma esterna dell'enum (tre fonti, vedi `ItemReference`), **non** una conferma dal filo: resta provvisorio, con la nota nel codice.

## Dove mi sono fermato

Non mi sono fermato: tutto il perimetro S4 è completo (catalogo + 2 file di test). Non ho aggiunto consumatori né il comando `--item-report` (fuori scope S4, e `Program.cs` è di S3).

**Domanda aperta lasciata al rapporto, non al codice**: perché `2006` e i vnum di equipaggiamento non stanno nell'`Item.dat` installato. Ipotesi non verificate: il server privato usa un `Item.dat` diverso o vnum personalizzati. Fuori scope S4 — il catalogo onestamente risponde `item_not_in_catalogue`.

## Spec vs codice

- La spec assume «i vnum che le otto catture nominano davvero esistono nel catalogo». La misura dice **no per 11 su 14**. Ha ragione il codice: la premessa regge solo per 8, 13, 2612.

## Test rimandati ancora aperti

T-03, T-05, T-06, T-07, T-08, T-09, T-12, T-13, T-14, T-16, T-17.
