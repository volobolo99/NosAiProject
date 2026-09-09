# NosAiProject — MASTER ROADMAP

Generata da `contracts/ledger.json`. Vocabolario dei tag: `docs/PROTOCOL_TOKENS.md`.
Non si modifica a mano: la rigenera `update_contract_state` a ogni cambio di stato.

**Completamento globale: 68%** — 17 contratti conclusi su 25.

### Gate 0: Ambiente, test harness e bridge nativo (50%)

- [x] **C-001** Build e suite .NET riproducibili
- [x] **C-002** Suite Python e CI
- [ ] **C-003** Harness AddressSanitizer
  - **Blocker**: `scripts/run_asan_pipeline.py` non esiste; la FASE 5 lo invoca
- [ ] **C-004** Gatekeeper dei test Python
  - **Blocker**: `scripts/gatekeeper.py` non esiste; la FASE 5 lo invoca
- [ ] **C-005** Bridge ctypes verso il modulo nativo
  - **Blocker**: nessun modulo nativo da caricare
- [x] **C-006** Instradamento dei modelli: DeepSeek nativo, mai da OpenRouter

### Gate 1: Intercettazione pacchetti, hook e parsing opcode (70%)

- [x] **C-101** Sorgente di pacchetti astratta
- [x] **C-102** Cattura live via WinDivert
- [x] **C-103** Motore di cattura del traffico di gioco
- [x] **C-104** Parsing e registro degli opcode
- [ ] **C-105** Hook di memoria o DLL nel client
  - **Blocker**: nessun hook realizzato; cambia il confine tecnico del prodotto
- [x] **C-106** Replay deterministico di una cattura

### Gate 2: Dispatcher, correlazione entità e sincronizzazione Python (60%)

- [x] **C-201** Dispatcher thread-safe degli eventi
- [x] **C-202** Correlazione degli identificativi di entità
- [x] **C-203** Fusione delle osservazioni nel World Model
- [ ] **C-204** Sincronizzazione fra runtime C# e `nosai/` Python
  - **Blocker**: nessun canale dichiarato fra i due stack

### Gate 3: Decisione autonoma e recupero (55%)

- [x] **C-301** Pianificazione gerarchica HTN
- [x] **C-302** Pianificazione GOAP
- [x] **C-303** Orchestratore strategico
- [ ] **C-304** Macchina a stati finiti esplicita
  - **Blocker**: zero sorgenti con FSM; duplicherebbe l'autorità di Planner e Orchestrator
- [x] **C-305** Recupero dopo disconnessione

### Gate 4: Hardening, fuzzing e rilascio (25%)

- [x] **C-401** Cifratura di sessione e autenticazione
- [ ] **C-402** Fuzzing sui pacchetti corrotti
  - **Blocker**: zero occorrenze di fuzz nel repository
- [x] **C-403** Superficie di sicurezza del runtime
- [ ] **C-404** Procedura di rilascio verificata
  - **Blocker**: manca l'esecuzione end-to-end che la chiuda

## Domande aperte

- La FASE 5 presuppone codice nativo e ASan che il progetto non ha: o si apre un ramo nativo, o la FASE 5 va riscritta su `dotnet test` e `pytest`.
- Quale stack possiede lo stato di gioco: il runtime C# o `nosai/` Python.
- Una FSM esplicita sostituisce Planner e Orchestrator o li affianca.
