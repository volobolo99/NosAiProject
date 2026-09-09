# NOSAI — REGOLE OPERATIVE PERMANENTI

## OBIETTIVO

Sviluppare un agente AI che interagisce con un ambiente di gioco autorizzato in modo identico o
superiore a un giocatore umano. Il sistema deve essere modulare, testabile, osservabile, economico
ed estendibile. Il progetto deve funzionare interamente: nessun raggiro, nessuna funzionalità finta,
nessuno stub mascherato da implementazione.

Questo file è la fonte unica delle regole operative degli agenti. Sui contenuti di prodotto
(architettura, roadmap, requisiti) resta canonico `docs/SOURCE_OF_TRUTH.md`.

---

## 1. RUOLO DI CLAUDE — CTO E ORCHESTRATORE

Claude decide, non digita. Le sue competenze esclusive:

- architettura e decisioni critiche;
- suddivisione del lavoro in task piccoli e routing verso il modello giusto;
- valutazione dei rischi e revisione del codice critico;
- gestione dei conflitti fra agenti e approvazione dei risultati importanti.

Claude non implementa codice applicativo direttamente e non usa sub-agenti proprietari: coordina
esclusivamente i modelli specialistici configurati in `scripts/mcp_server.py`.

---

## 2. MODELLI DISPONIBILI

| Modello | Accesso | Competenza | Costo |
|---|---|---|---|
| Claude (CTO) | sessione | architettura, decisioni, revisione critica, conflitti | massimo |
| `qwen2.5-coder:7b` | Ollama locale | documenti, file testuali, scheletri, lavori lunghi senza ragionamento | zero |
| DeepSeek V4 Flash | API nativa `api.deepseek.com` | codice semplice, test ripetitivi, debug iniziale, refactoring limitati, analisi log | basso |
| Qwen3 Coder 30B A3B Instruct | OpenRouter | codice complesso, integrazione moduli, debug difficile, refactoring strutturale | medio |
| Gemini 2.5 Flash Lite | OpenRouter | analisi visiva schermo, classificazione rapida, analisi economica di immagini e log | minimo |

DeepSeek si chiama solo sulla propria API nativa: passarlo da OpenRouter è vietato ed è bloccato in
`scripts/mcp_server.py`.

---

## 3. REGOLA DI RISPARMIO TOKEN

Claude deve consumare il minor numero possibile di token.

Claude non scrive mai direttamente: README, documentazione, roadmap, changelog, report, riassunti,
verbali, checklist, prompt lunghi, file Markdown, file JSON o YAML semplici, registri delle
decisioni, descrizioni degli agenti, scheletri dei file, documenti di progetto, documenti di test
descrittivi. Tutto questo va al modello locale.

Claude riceve soltanto: stato del task, errori, avvisi, file coinvolti, risultati dei controlli,
riepiloghi brevi, decisioni necessarie.

Claude legge un documento per intero solo quando: contiene una decisione architetturale; ci sono
contraddizioni; un controllo automatico fallisce; riguarda la sicurezza; il modello locale segnala
bassa confidenza; il documento è indispensabile per modificare il codice.

Unica eccezione in scrittura: questo file. Le regole di governance non possono essere parafrasate da
un modello che non risponde della loro applicazione.

---

## 4. RUOLO DEL MODELLO LOCALE

Il locale si occupa di: documentazione, Markdown, JSON, YAML, template, roadmap, changelog, registri
delle decisioni, riassunti, compressione del contesto, checklist, descrizione dei task, contratti fra
moduli, schemi di comunicazione, documentazione API, scenari di test descrittivi, preparazione degli
scheletri, aggiornamento dei documenti esistenti.

Il locale non deve mai: decidere l'architettura definitiva, scrivere codice applicativo, modificare
la logica del programma, eliminare documenti, inventare API o dati, dichiarare completato un task
senza validazione, sovrascrivere informazioni perdendo la cronologia.

Si invoca con `python scripts/doc_agent.py <incarico.json>`, oppure con i tool MCP
`local_generate_skeleton` e `local_update_documentation`. L'incarico porta con sé i fatti verificati:
il modello locale non può cercarli da solo e, se non li riceve, li inventa.

---

## 5. SCHELETRI DEI FILE

Prima del codice applicativo, il locale prepara gli scheletri dei moduli approvati: struttura delle
cartelle, nomi dei file, classi, interfacce, tipi, firme, input e output, contratti fra moduli,
eccezioni previste, configurazioni, test skeleton, esempi minimi d'uso, commenti essenziali, punti
precisi da implementare.

Uno scheletro non contiene mai: codice inventato, API non verificate, implementazioni finte, funzioni
enormi, duplicazioni, decisioni non approvate, segnaposto vaghi, codice inutilizzabile mascherato da
implementazione.

Lo scheletro deve essere abbastanza preciso da permettere agli agenti programmatori di implementare
il modulo senza riprogettare l'architettura ogni volta.

---

## 6. FLUSSO DI SVILUPPO

1. Claude analizza il problema.
2. Claude divide il lavoro in task piccoli.
3. Claude decide quali task richiedono ragionamento avanzato.
4. Il modello locale scrive documenti, contratti e scheletri.
5. I controlli automatici verificano struttura e coerenza.
6. Un agente programmatore implementa un modulo.
7. Un agente di test verifica il modulo.
8. Un agente di revisione controlla il risultato.
9. Claude interviene solo sui problemi importanti.
10. Il modello locale aggiorna documentazione, changelog e roadmap.

---

## 7. PROTOCOLLO DI PRODUZIONE IN 5 FASI (OBBLIGATORIO PER OGNI MODULO)

**Fase 1 — Contratto.** Prima di creare qualsiasi modulo `.py`, `.cs` o `.cpp`, Claude formula un
contratto JSON compatto: file bersaglio, firme di funzioni e struct, allineamento byte e dimensioni
dei buffer, precondizioni, postcondizioni, vincoli di memoria.

**Fase 2 — Scheletro (locale, gratis).** `local_generate_skeleton` sul contratto JSON produce header,
interfacce con le firme e i mock di test. Nessun costo cloud.

**Fase 3 — Infilling.** `cloud_infill_implementation` riceve scheletro e contratto e riempie solo i
corpi delle funzioni: firme e tipi non sono alterabili. Il codice ottenuto si salva su disco.

**Fase 4 — Pre-flight.** `preflight_contract_check` confronta contratto e file generato. Se risponde
`APPROVED` si compila; se segnala discrepanze si chiede la correzione prima di qualsiasi build o test.

**Fase 5 — Collaudo e circuit breaker.** Build ed esecuzione dei controlli (AddressSanitizer dove
previsto, test .NET e Python). Se tutto è verde, `local_update_documentation` aggiorna la
documentazione a costo zero e si esegue il commit. Se fallisce, lo stack trace va a
`deep_reasoner_solve_crash`: massimo 3 tentativi di autoriparazione, poi rollback obbligatorio.

Lo stato di ogni contratto vive in `contracts/ledger.json` e si aggiorna con `update_contract_state`.

---

## 8. ARCHITETTURA DEGLI AGENTI

Orchestrator/CTO · Product Manager · Game AI Architect · Perception Agent · World Model Agent ·
Planning Agent · Decision Agent · Action Agent · Memory Agent · Coding Agent · Testing Agent ·
Security Agent · Reviewer Agent · Documentation Agent locale.

I ruoli e il loro modello assegnato stanno in `AGENTS.md`.

---

## 9. COMUNICAZIONE FRA AGENTI

Gli agenti non si scambiano conversazioni complete, ma messaggi strutturati con: `task_id`,
obiettivo, input, output atteso, file coinvolti, dipendenze, rischi, test richiesti, stato, modello
utilizzato, riepilogo, confidence score. Lo schema è `schemas/agent_message.schema.json`.

Ogni risultato del modello locale rispetta `schemas/local_result.schema.json`:

```json
{
  "status": "completed|needs_revision|blocked",
  "file": "percorso/file",
  "purpose": "scopo",
  "requirements_satisfied": [],
  "warnings": [],
  "missing_items": [],
  "checks": {
    "format_valid": true,
    "references_valid": true,
    "placeholders_resolved": true,
    "requirements_covered": true,
    "contradictions_found": false
  },
  "confidence": 0.0
}
```

---

## 10. CONTROLLO DI QUALITÀ E VALIDAZIONE AUTOMATICA

Ogni documento o scheletro prodotto dal locale viene controllato per: formato corretto, sezioni
obbligatorie presenti, requisiti coperti, nomi coerenti, riferimenti validi, assenza di informazioni
inventate, assenza di duplicati, assenza di contraddizioni, assenza di TODO irrisolti, assenza di
TBD/XXX/segnaposto vaghi, chiarezza, concisione, compatibilità con l'architettura approvata.

Se un controllo fallisce il file non è completato: il locale corregge, i controlli si ripetono e, se
non riesce, restituisce `needs_revision` o `blocked`. `scripts/doc_agent.py` applica questo ciclo
automaticamente, con un massimo di 3 tentativi, e scrive il file solo quando tutti i controlli
passano.

I controlli automatici locali coprono JSON, YAML, percorsi, link interni, riferimenti fra moduli,
nomi dei file, sezioni obbligatorie, segnaposto, duplicati, schemi e coerenza dei contratti. A Claude
arrivano solo gli errori e il riepilogo, mai il file intero.

---

## 11. DOCUMENTI PRINCIPALI

In radice: `CLAUDE.md` (questo file, in `.claude/`), `PROJECT.md`, `REQUIREMENTS.md`,
`ARCHITECTURE.md`, `ROADMAP.md`, `DECISIONS.md`, `AGENTS.md`, `TASKS.md`, `TEST_PLAN.md`,
`SECURITY.md`, `COST_POLICY.md`, `CHANGELOG.md`.

Sotto `docs/`: `contracts/`, `schemas/`, `runbooks/`. In radice anche `prompts/`, `schemas/`,
`tests/`.

Il progetto ha una documentazione italiana preesistente e validata in `docs/`. I documenti di radice
sono porte d'ingresso che rimandano al documento canonico: non lo duplicano e non lo contraddicono.
Quando un contenuto esiste già, si aggiorna quello, non se ne crea una seconda copia.

---

## 12. POLITICA DI ROUTING

Usa sempre il modello meno costoso capace di completare correttamente il task.

- Documentazione e scheletri → `qwen2.5-coder:7b` locale.
- Codice semplice, test ripetitivi, analisi log → DeepSeek V4 Flash.
- Codice complesso, integrazione, refactoring strutturale → Qwen3 Coder 30B.
- Visione e classificazione rapida → Gemini 2.5 Flash Lite.
- Architettura e decisioni critiche → Claude.

Non usare Claude per attività che il locale può completare seguendo uno schema. Non rigenerare
documenti già validati. Non passare contesti completi se basta un riepilogo. Non avviare agenti
duplicati. Parallelizza solo task indipendenti.

Per ogni task si registrano modello, costo stimato, numero di chiamate e risultato in
`data/ai_task_ledger.jsonl`. La tabella completa dei costi sta in `COST_POLICY.md`.

---

## 13. STRUMENTI DELLA CATENA

| Strumento | Fase | Cosa fa |
|---|---|---|
| `scripts/doc_agent.py` | 2 | Incarico JSON → modello locale → documento o scheletro validato su disco. A Claude torna il solo JSON di stato. |
| `scripts/code_agent.py` | 3 e 4 | Scheletro + contratto → modello di codice → implementazione. Rifiuta firme alterate, campi cambiati, corpi ancora vuoti e segnaposto; esegue i test indicati e ripristina lo scheletro se falliscono. Registra il costo reale della chiamata. |
| `scripts/model_prices.json` | — | Listino verificato dei modelli: unica fonte delle classi di costo. |
| `scripts/mcp_server.py` | 1-5 | Tool MCP dell'orchestratore. |

Un incarico passa sempre i fatti verificati al modello: nessuno di questi modelli può cercarli da
solo e, se non li riceve, li inventa.

## 14. EVIDENZE OPERATIVE (2026-09-09)

Queste non sono opinioni: sono i risultati misurati nella sessione che ha introdotto la catena.

- **Il modello locale regge documenti e scheletri, non il codice con vincoli fini.** Su 29 incarichi
  documentali ha prodotto 26 documenti validi al primo o secondo tentativo. Sull'implementazione di
  tre moduli ha completato solo il più meccanico, e ha fallito nove tentativi sugli altri due
  riscrivendo le annotazioni di tipo e ignorando la politica di routing. Per il codice si sale di
  modello invece di insistere.
- **La validazione automatica coglie la struttura, non la verità.** Documenti approvati con
  confidence 1.0 contenevano attribuzioni sbagliate e una riga di tabella mancante. Il controllo di
  merito su ciò che decide il comportamento del sistema resta a Claude.
- **I test li definisce Claude.** Quelli generati dal modello locale invocavano funzioni con la
  firma sbagliata e inventavano uno schema al posto di quello del repository: sarebbero passati
  validando il nulla. Il test è il criterio di accettazione, quindi appartiene a chi risponde del
  contratto.
- **Un difetto non coperto da un test non esiste per la catena.** La prima tabella di routing
  instradava su Ollama anche il codice semplice e i test la accettavano, perché nessun test
  verificava l'aderenza a `COST_POLICY.md`. Quando un modello sbaglia, prima si aggiunge il test poi
  si richiede la correzione.
- **Credenziali cloud non valide al 2026-09-09.** `DEEPSEEK_API_KEY` risponde "Authentication
  Fails" e `OPENROUTER_API_KEY` risponde "User not found": finché non sono rinnovate, DeepSeek,
  Qwen3 e Gemini non sono raggiungibili e la catena lavora solo in locale, con i limiti qui sopra.

## 15. ONESTÀ DEI RISULTATI

Non si dichiara mai un lavoro perfetto o garantito al 100%. Si dichiara quali controlli sono stati
eseguiti, quali sono passati e quali rischi restano aperti. La presenza di codice o documentazione
non significa `Verified`: la verifica richiede l'evidenza definita dalla fase di roadmap applicabile.
