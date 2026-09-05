# AP-01 / A2 — DeepSeek — Sensor Fusion

**Fase:** AP-01 (Unified World Model), ruolo originariamente assegnato a "Cursor A2" in `docs/agents/AGENT_COMMAND_REGISTRY.md` — riassegnato a te perché è un lavoro di adapter/integrazione indipendente, a basso rischio di sovrapposizione con il lavoro in corso di Claude sullo stesso repo.

## Prima di scrivere codice — leggi in quest'ordine

1. `CLAUDE.md` (root del repo) — protocollo generale del progetto: niente TODO/stub/pseudocodice, file completi, mai indebolire i test, niente mock sul percorso critico, `UNKNOWN` non è mai zero/false/vuoto.
2. `docs/ROADMAP_ESECUTIVA.md` — architettura canonica: pipeline `Observe → Sensor Fusion → World Model → ...`. Il tuo lavoro è esattamente lo stadio "Sensor Fusion".
3. `docs/NOSAI_ARCHITECTURE_BASELINE.md` — sezione sulla Perception/Sensor Fusion e la classificazione di provenienza (`LIVE/DERIVED/CACHED/SIMULATED/UNKNOWN`).
4. `src/NosAi.Runtime/Contracts/DataClassification.cs` — il tipo `ClassifiedValue<T>` e l'enum `DataSourceKind` usati in tutto il runtime per non fabbricare mai un valore osservato: ogni fatto ha `Source`, `HasValue`, `ObservedAtUtc`, e se sconosciuto porta un `FailureReason` esplicito.
5. `src/NosAi.Runtime/Gate1/Gate1ObservationChannel.cs` — un esempio reale di canale di osservazione già esistente (canale Gate1, PC↔telefono).
6. `src/NosAi.Core/Navigation/NavigationObservation.cs` e `NavigationEvidenceEvaluator.cs` — un esempio reale, già in produzione, di come due osservazioni (`before`/`after`) vengono confrontate e fuse in un'unica evidenza con provenienza esplicita e motivo di rifiuto quando l'osservazione è mancante/stale. Usa lo stesso stile di rigore (vedi in particolare come `NavigationEvidenceEvaluator.Evaluate` gestisce staleness e provenienza `"Unknown"`).
7. **`docs/agents/phases/AP-01/A1_CLAUDE_world_model_contracts.md`** e il codice che produce: i contratti versionati del World Model (Player, Map, Tile/Polygon, Portal, Mob, NPC, Drop, Quest, InventoryItem, EquipmentItem, Skill, Buff, Debuff, Cooldown, Resource, Action, Goal). **Questo lavoro potrebbe non essere ancora presente quando inizi.** Cerca con `grep -rln "class.*WorldModel\|record.*WorldModel" src/NosAi.Core/` o percorsi tipo `src/NosAi.Core/WorldModel/`. Se non trovi contratti versionati coerenti con quella lista, **fermati e riporta il blocco esatto** invece di inventare tu una tua versione del World Model: fondere osservazioni dentro un contratto che non esiste ancora produrrebbe un'architettura duplicata/incompatibile che qualcun altro dovrebbe poi disfare.

## Cosa NON toccare

- Nessun file sotto `src/NosAi.Core/Hardware/`, `src/NosAi.Core/Scheduling/`, `src/NosAi.Runtime/Hardware/` — lavoro Claude in corso/completato sulla fase AP-00, non correlato.
- Nessun file di Gate1-6 esistente in `src/NosAi.Runtime/Gate1/`, `Gate2/`, `Gate3/`, ecc. — leggili solo come riferimento, non modificarli.
- Nessun file di altri agenti attivi. Se un file che pensavi di creare esiste già con contenuto diverso dal previsto, fermati e chiedi invece di sovrascrivere.

## Cosa costruire

Il layer di fusione che converte le osservazioni ESISTENTI (rete/Network, memoria/Memory, schermo/Screen, locale/Local — quelle già prodotte dal codice Gate1-3 e dai provider in `src/NosAi.Runtime/LiveIntegration/`) nel World Model versionato di AP-01 (prodotto da A1, vedi sopra).

Requisiti (dal comando ufficiale, vincolanti):
- **Precedenza deterministica**: quando più fonti osservano lo stesso fatto (es. la mappa vista sia dal wire di rete sia dalla memoria del client), la regola di scelta della fonte vincente deve essere esplicita, documentata e testata — mai un ordine implicito/casuale.
- **Disagreement tracking**: quando due fonti si contraddicono, questo va registrato esplicitamente (non silenziosamente risolto verso una delle due come se l'altra non fosse mai esistita).
- **Provenance/confidence/freshness**: ogni fatto fuso porta la classificazione di provenienza, un timestamp di osservazione e — se il World Model di A1 lo prevede — un livello di confidenza.
- **UNKNOWN preservato**: se nessuna fonte osserva un fatto, il risultato fuso è `Unknown` con motivo, mai un default (zero, stringa vuota, mappa origine, ecc. — vedi lo stile già usato in tutto il repo, es. `GameplayObservation.Unobserved` in `src/NosAi.Runtime/LiveIntegration/GameplayProvider.cs`).
- **Mai inventare valori**: se una fonte è assente o l'osservazione è scaduta (stale), il layer di fusione deve dirlo, non deve mai sostituirla con un valore plausibile.
- **Latenza/allocazioni limitate**: questo componente sta nel percorso di osservazione che gira ad alta frequenza (fino a ogni frame/tick) — niente allocazioni non necessarie o operazioni bloccanti nel percorso caldo.

Posizione consigliata: una nuova cartella coerente con dove A1 mette i contratti del World Model (es. `src/NosAi.Core/WorldModel/Fusion/` se A1 usa `src/NosAi.Core/WorldModel/`, altrimenti adatta al percorso reale che trovi). Resta SOLO in una cartella nuova, tua.

## Test

Test reali (non mock) sotto `tests/NosAi.Core.Tests/` nella sottocartella corrispondente. **Importante**: questo progetto NON ha un `using Xunit;` implicito nei file di test — aggiungilo esplicitamente in ogni file, altrimenti non compila. Copri: precedenza deterministica fra fonti in conflitto; disagreement registrato; nessuna fonte disponibile → Unknown con motivo; una fonte stale viene esclusa dalla fusione con motivo esplicito; determinismo (stesso input due volte → stesso output).

## Validazione prima di dichiarare finito

Il .NET 8 SDK, se non è già installato nel tuo ambiente, si installa con:
```
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh && bash /tmp/dotnet-install.sh --channel 8.0 --install-dir /root/.dotnet
export PATH="/root/.dotnet:$PATH"
```
Poi:
```
dotnet build src/NosAi.Core/NosAi.Core.csproj -c Release
dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release
```
Zero warning (il progetto ha `TreatWarningsAsErrors=true`), zero errori, tutti i test verdi — inclusi quelli già esistenti, non solo i tuoi.

## Consegna

Non dichiarare mai `Verified` senza evidenza reale su hardware/client reale (qui puoi al massimo dichiarare `Present`/`Integrated` con evidenza dei test). Alla fine riporta: file creati (percorso completo), riassunto delle scelte di design (in particolare la regola di precedenza fra fonti), comando e risultato esatto di build/test, limiti dichiarati, e cosa serve all'agente di integrazione successivo (A6) per collegare questo layer ai provider reali di osservazione.
