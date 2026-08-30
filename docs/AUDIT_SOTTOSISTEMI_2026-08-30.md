# NosAi — Audit dei sottosistemi: cosa è collegato e cosa no

**Data:** 2026-08-30
**Metodo:** analisi statica dei riferimenti fra namespace su `src/NosAi.Runtime/`,
più esecuzione di ogni suite raggiungibile.

## Perché questo audit

`STATO_IMPLEMENTAZIONE.md` elenca molti sottosistemi come **Present** o
**Integrated**. La domanda che nessuno aveva posto è più semplice e più dura:
*quel codice viene mai raggiunto da qualcosa?*

La risposta, per una parte consistente, è no.

## Il difetto strutturale

`src/NosAi.Runtime/NosAi.Runtime.csproj` fissa:

```xml
<StartupObject>NosAi.Runtime.Program</StartupObject>
```

È necessario — senza, il build è ambiguo, perché molti file portano un proprio
`Main`. L'effetto collaterale è che **ogni altro `Main` dell'assembly diventa
codice morto**.

Sette sottosistemi avevano scritto la propria suite di certificazione e il proprio
punto d'ingresso per lanciarla. Nessuno dei due era raggiungibile, e nessun flag di
`Program.cs` le invocava. Quelle suite **non erano mai state eseguite nemmeno una
volta**.

Non è teoria. Lo stesso difetto valeva per Gate 3 e per il Master Host, e appena
sono state rese eseguibili hanno rivelato difetti reali:

- Gate 3 dichiarava azioni eseguite senza eseguirle, e verificava la simulazione
  contro sé stessa;
- la suite di Gate 4 era **rossa** e nessuno poteva accorgersene.

## Suite rese raggiungibili

| Flag | Sottosistema | Righe |
|---|---|---|
| `--storage-test` | `NosAi.Storage.Infrastructure` | 720 |
| `--navigation-test` | `NosAi.Navigation.Pathfinding` | 706 |
| `--raids-test` | `NosAi.Raids.Dodekatheon` | 326 |
| `--gateway-test` | `NosAi.Network.Gateway` | 296 |
| `--localai-test` | `NosAi.AI.LocalInference` | 124 |
| `--miniland-test` | `NosAi.Miniland.Production` | 114 |
| `--hardware-test` | `NosAi.Hardware.Autoscale` | — |

`Program.cs` ora tiene tutte le suite in **una tabella** invece che in una scala di
`if`: aggiungere un runner senza collegarlo era il modo in cui si arrivava qui, e
un elenco unico rende l'omissione evidente. `--list-suites` le stampa.

Sono anche coperte da `NosAi.Runtime.Tests/SubsystemSuiteTests.cs`, così una
regressione fa fallire la build invece di aspettare che qualcuno ricordi un
comando non documentato.

## Moduli che niente referenzia

Riferimenti esterni al proprio namespace, esclusi i test:

| Modulo | Namespace | Righe | Riferimenti esterni |
|---|---|---:|---:|
| Storage | `NosAi.Storage.Infrastructure` | 720 | 0 |
| Raids | `NosAi.Raids.Dodekatheon` | 326 | 0 |
| Network | `NosAi.Network.Gateway` | 296 | 0 |
| AI | `NosAi.AI.LocalInference` | 124 | 0 |
| Miniland | `NosAi.Miniland.Production` | 114 | 0 |
| Capabilities | `NosAi.Runtime.Capabilities` | 86 | 0 |
| Events | `NosAi.Events.InstantBattle` | 73 | 0 |
| Security | `NosAi.Runtime.Security` | 40 | 0 |
| Telemetry | `NosAi.Runtime.Telemetry` | 35 | 0 |
| PlayAi | `NosAi.Runtime.PlayAi` | 15 | 0 |

Circa **1.800 righe** che nessun altro modulo usa. Renderle eseguibili non le rende
integrate: una suite verde su un modulo che niente richiama dimostra che il modulo
funziona da solo, non che serva a qualcosa.

La distinzione va tenuta ferma quando si aggiorna `STATO_IMPLEMENTAZIONE.md`:
**Present** e **Integrated** non sono la stessa cosa, e oggi il documento non le
separa.

## Duplicazione da risolvere: due implementazioni SQLite

| | |
|---|---|
| `Storage/Infrastructure/StorageInfrastructure.cs` | `CentralizedSqlitePolicy`, `CentralDatabaseEngine`, `DatabaseSchemaMigrationManager`, `AutomatedBackupSnapshotService` — **orfano** |
| `Gate2/Gate2Sqlite.cs` e affini | persistenza WAL di Gate 2, attiva e testata |

Il commit di Gate 2 dichiara di aver allineato «la policy centralizzata». Esistono
quindi due implementazioni della stessa policy, una delle quali non è raggiungibile
da nulla.

**Non è stata risolta qui.** Riguarda il lavoro di due autori in parallelo e va
deciso insieme quale sopravvive: unificare unilateralmente rischia di cancellare
lavoro altrui o di rompere Gate 2. È il primo punto da coordinare.

## Debito già registrato altrove

Tipi duplicati su un confine di sicurezza: `TrustTier` è definito in `Contracts`,
`Gate3`, `Gate6` e `Host`; `SafetyGate` in `Gate3`, `Gate6` e `Safety`;
`TrustBoundary`, `RuntimeMode` e `RecoveryController` in `Gate3` e `Gate6`.
Compilano perché stanno in namespace diversi, ma nulla impedisce loro di divergere.
Vedi `docs/GATE3_PIPELINE.md`.

## Cosa questo audit non dimostra

- Che i sottosistemi siano corretti: dimostra solo che ora le loro suite girano.
- Che siano utili: un modulo con zero riferimenti esterni resta non integrato,
  qualunque cosa dica la sua suite.
- Nulla sull'ambiente reale. Tutte le suite sono locali.
