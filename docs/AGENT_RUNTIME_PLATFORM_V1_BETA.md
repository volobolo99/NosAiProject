# NosAi — Piattaforma runtime degli agenti

**Estensione architetturale:** v1.0 Beta  
**Versione:** 1.0 Beta (bloccata)  
**Creatore:** Volodymyr Ryzhuk

## Scopo

Questa estensione aggiunge un livello runtime indipendente dal modello e orientato all'esecuzione locale sopra la pipeline deterministica di NosAi, senza concedere privilegi di esecuzione ai fornitori decisionali stocastici. Il runtime è limitato, osservabile, recuperabile e progettato per chiudere in sicurezza in caso di errore.

## Piano di controllo runtime

SessionManager, Scheduler, Memoria, Policy, Trust, Risorse, ProviderRouter, Strumenti, Watchdog e Valutazione costituiscono un piano di controllo trasversale. Governano l'esecuzione senza sostituire la pipeline di dominio canonica.

## Contratto a ciclo chiuso

```text
Osservazione
  ↓
WorldState canonico (vN)
  ↓
Simulazione → Ranking tattico → Orchestrator
  ↓
Planner → Contesto decisionale Guard → Trust → Safety
  ↓
Executor / Game Adapter
  ↓
Risultato azione
  ↓
Verificatore + nuova osservazione
  ↓
WorldState canonico (vN+1)
  ├── verificato → checkpoint → decisione successiva
  └── fallito → retry/recupero limitato → nuova pianificazione
```

Ogni azione viene autorizzata indipendentemente. Il livello Trust del chiamante costituisce un limite massimo. Requisiti Trust sconosciuti o malformati devono chiudere il percorso in sicurezza.

Recovery e Watchdog sono controller runtime adattivi: non possono aumentare il livello Trust né concedersi autonomamente autorità di esecuzione.

## Piano eventi e tracce

Il runtime deve emettere eventi tipizzati senza trasformare il bus eventi in un percorso di esecuzione. Gli eventi contengono `event_id`, `session_id`, `run_id`, `task_id`, `parent_event_id`, timestamp, sorgente, tipo, versione dello schema e payload.

Le famiglie principali comprendono percezione, aggiornamenti WorldState, simulazione, ranking, decisioni, piani, valutazioni Guard/Safety, esecuzione, verifica, recupero, ripianificazione, memoria, instradamento provider, modifiche del profilo hardware e ciclo di vita della sessione.

Il piano supporta audit, telemetria, valutazione e replay orientato alla simulazione.

## Provenienza WorldState

Il WorldState canonico è immutabile per ogni osservazione accettata. Ogni versione identifica il proprio predecessore e la provenienza dell'osservazione. La simulazione deve riferirsi alla versione esatta dello stato di ingresso. La verifica confronta risultato previsto e risultato reale dopo la nuova osservazione.

Catena misurabile:

`WorldState vN → previsione → azione → WorldState vN+1 → errore di previsione`.

## Semantica decisionale e ranking

I fornitori decisionali restituiscono solo dati. La simulazione prevede. Il ranking tattico assegna punteggi ma non autorizza. L'Orchestrator coordina. Il Planner crea piani limitati. Guard valuta il rischio contestuale. Trust fornisce limiti deterministici di autorizzazione. Safety è il controllo finale a chiusura sicura.

Il ranking deve esporre punteggio, confidenza, rischio, ricompensa attesa, confidenza della previsione e qualità dell'evidenza per permettere audit e confronto nel tempo.

## Semantica della memoria

La memoria distingue esperienza grezza, osservazione, episodio, ipotesi e conoscenza verificata. Prima di promuovere un'esperienza a strategia riutilizzabile sono necessarie evidenza di verifica e provenienza. Gli esiti non verificati non possono diventare silenziosamente conoscenza.

## Instradamento provider e hardware

Provider Router è locale come prima scelta e controllato dalla policy. Gli ingressi comprendono privacy/località, complessità del compito, latenza, VRAM/RAM, utilizzo GPU, temperatura, energia e prestazioni recenti dei provider. La profilazione hardware rimane deterministica a livello di contratto; rilevamenti e benchmark reali sono soggetti a gate.

## Recupero e Watchdog

Eccezioni dell'Executor e fallimenti della verifica non costituiscono mai un successo. Il runtime può effettuare retry entro un budget e successivamente ripianificare usando un contesto strutturato del fallimento. Il Watchdog indipendente limita runtime, azioni, fallimenti consecutivi e altri budget configurati. Un Watchdog attivato non può essere ripristinato dall'output di un modello.

## Sessione, PC e telefono

Il primo avvio previsto è locale/LAN e autenticato. I messaggi tipizzati utilizzano protezione da sequenza e replay. Ciclo previsto: `HELLO → CAPABILITIES → AUTH → HEARTBEAT/STATUS → COMMAND/EVENT → ACK/ERROR → DISCONNECT`.

Play AI PC, Play Guard PC e Guard AI telefono rimangono ruoli separati collegati tramite contratti espliciti; sessioni non valide o disconnesse devono chiudere il percorso in sicurezza.

## Invarianti di sicurezza

- Nessun LLM esegue direttamente.
- Nessun ranking o Orchestrator esegue direttamente.
- Nessuna percezione esegue direttamente.
- Nessun recupero può aumentare l'autorizzazione.
- Nessun Watchdog può aumentare l'autorizzazione.
- Nessuna escalation cloud quando la policy impone il funzionamento locale.
- Nessun esito non verificato viene trattato come successo.
- Nessuna integrazione live del gioco prima dei gate di rilascio e sicurezza.

## Confine produttivo corrente

Implementato: runtime autonomo limitato, ponte osservazione/ripianificazione a ciclo chiuso, confine deterministico Trust/Guard/Safety, fondazioni provider/risorse, fondazioni sessione/checkpoint e primitive di valutazione.

Soggetto a gate: bus eventi produttivo, archivio WorldState persistente/versionato, PredictionEvaluator, persistenza della conoscenza basata sull'evidenza, trasporto LAN autenticato, Guard/Play Guard produttivi, rilevamento hardware, provider locali/cloud, percezione produttiva e adapter live del gioco.

**Nessun incremento di versione:** il progetto rimane **NosAi 1.0 Beta**.
