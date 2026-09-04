# NosAiProject

Implementazione sorgente di **NosAi**, agente software per un giocatore autonomo nel contesto privato/test dichiarato.

**Versione:** 1.0 Beta  
**Data architettura:** 2026-09-05

## Obiettivo

NosAi deve operare come un giocatore autonomo: percepire il client, ricostruire il mondo, riconoscere e imparare le mappe, navigare, combattere, comprendere ed eseguire missioni, gestire inventario/equipaggiamento/progressione, imparare dalle esperienze e recuperare dagli errori.

Il target è autonomia operativa completa nel perimetro dichiarato. Non è ammessa onniscienza: informazione insufficiente o conflittuale = `UNKNOWN`, replan o safe-stop.

## Boundary non privilegiato

NosAi può usare CPU/GPU/NPU/RAM/storage del PC, normali API Windows, rete visibile al client, memoria locale legittimamente leggibile, cattura schermo/pixel, OCR/CV, audio disponibile al PC, telemetria locale e meccanismi software di controllo compatibili con il client.

Mouse e tastiera sono **permessi ma non obbligatori**. Non sono ammessi server DB, GM/mod/admin tools, console, API privilegiate, debug/hidden state, credenziali amministrative o hardware esterno di automazione.

## Architettura

`Observe → Sensor Fusion → World Model → Simulation/Prediction → Ranking/Utility → Strategic Orchestrator → HTN/GOAP → Guard → Trust → Safety → Execute → Verify → Re-observe`

Nessun LLM, planner, modello ML o euristica ha autorità diretta di esecuzione.

## Documentazione canonica

- `docs/ROADMAP_ESECUTIVA.md` — roadmap canonica.
- `docs/NOSAI_AUTONOMOUS_PLAYER_SPEC.md` — specifica del giocatore autonomo e boundary.
- `docs/SOURCE_OF_TRUTH.md` — gerarchia documentale.
- `docs/NOSAI_ARCHITECTURE_BASELINE.md` — baseline architetturale.
- `docs/UNPRIVILEGED_DEMO_SPEC.md` — specifica di riproducibilità senza accessi privilegiati.
- `docs/adr/` — decisioni architetturali.
- `third_party/` — vault di codice, ricerca, licenze e provenance.

## Supporto allo sviluppo

- `CLAUDE.md` — istruzioni per Claude Code.
- `.cursor/rules/` — regole Cursor.
- `docs/BUILD_TEST_RELEASE.md` — build/test/release.
- `docs/TESTING.md` — strategia di test.
- `docs/GIT_WORKFLOW.md` — workflow Git.
- `docs/CONTROLLO_PERSONAGGIO_ARCHITETTURA.md` — controllo personaggio.
- `docs/CONTROLLO_PERSONAGGIO_ATTUAZIONE.md` — attuazione e verifica.
- `docs/PROGRESSION_ENGINE_SPEC.md` — progressione ed equipaggiamento.
- `docs/PROTOCOLLO_NOSTALE.md` — riferimento al protocollo osservabile.
- `docs/RECOVERY_WATCHDOG.md` — recovery/watchdog.
- `docs/research/` — ricerca tecnica datata.

## Principi

1. Autonomia senza dati privilegiati.
2. World Model come rappresentazione semantica corrente.
3. Sensor fusion con provenance, confidence e freshness.
4. Pianificazione gerarchica: strategica → HTN/GOAP → reattiva.
5. Separazione assoluta tra decisione ed esecuzione.
6. Safety e authorization come autorità finale.
7. Verifica dopo ogni azione significativa.
8. Memoria persistente e failure learning senza contaminare la truth layer.
9. Testabilità, replay ed evidenza end-to-end.
10. Codice third-party sempre con licenza e provenance preservate.
