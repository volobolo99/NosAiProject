# Agenti

## Come leggere questa tabella

Questa tabella descrive i ruoli di sviluppo all'interno del progetto NosAi, indicando il modello assegnato a ciascuno e le responsabilità associate.

## Ruoli e modelli

| Ruolo | Modello | Responsabilità |
|-------|---------|----------------|
| Orchestrator/CTO | Claude | Architettura, suddivisione del lavoro, routing, approvazione finale |
| MCP Chief | Claude | Salute e configurazione dell’MCP, proposte di ottimizzazione, binding, promozioni evidence-gated e rollback |
| MCP Auditor | Claude (ruolo di governance in `DEFAULT_ROLES`, non un `employee.*`) | Veto, verifica delle evidenze, regressioni e invarianti; non modificabile dal Chief |
| Research Lab Coordinator | Qwen3 Coder 30B | Ipotesi, esperimenti, benchmark e curatela online→offline senza autorità di rilascio |
| Product & Architecture Lead | Claude decide; il modello locale (qwen2.5-coder:7b) scrive materialmente i documenti di prodotto su incarico | Requisiti di prodotto, priorità, criteri di accettazione e confini architetturali fra livelli cognitivi |
| Perception Agent | Gemini 2.5 Flash Lite per l'analisi visiva, Qwen3 Coder 30B per il codice del dominio | Lettura dello schermo e classificazione delle osservazioni |
| World Model Agent | Qwen3 Coder 30B | Fusione dei sensori e stato del mondo |
| Planning Agent | Qwen3 Coder 30B | Pianificazione delle azioni e navigazione |
| Decision Agent | Qwen3 Coder 30B | Selezione dell'azione e ranking dei candidati |
| Action Agent | DeepSeek V4 Flash | Attuazione dei comandi e verifica dell'esito |
| Memory Agent | DeepSeek V4 Flash | Persistenza, storico degli esiti e conoscenza accumulata |
| Coding Agent | DeepSeek V4 Flash per il codice semplice, Qwen3 Coder 30B per il codice complesso | Implementazione dei moduli a partire dallo scheletro |
| Testing Agent | DeepSeek V4 Flash | Test ripetitivi, esecuzione delle prove e analisi dei log |
| Security Agent | Claude | Confine di sicurezza, custodia delle chiavi, revisione dei cambiamenti sensibili |
| Reviewer Agent | Claude per il codice critico, DeepSeek V4 Flash per la revisione ordinaria | Controllo del risultato prima dell'integrazione |
| Documentation Agent | qwen2.5-coder:7b locale | Documentazione, changelog, roadmap, scheletri, riassunti |
| Model Scout | gpt-oss-20b (fallback nex-n2.5-mini, qwen2.5-coder:7b) | Sorveglianza quotidiana del catalogo modelli e dei prezzi; propone, non promuove mai da solo un binding a pagamento |

## Regole comuni

- Ogni agente riceve un messaggio strutturato conforme a `schemas/agent_message.schema.json` e non una conversazione completa.
- Nessun agente può dichiarare completato un task senza che i controlli automatici siano passati.
- Le promozioni di binding richiedono evidenze firmate, fresche e indipendenti; un booleano nel payload non è una prova.
- Nessun agente può introdurre segnaposto, stub o implementazioni finte.
- Nessun comando di fase autorizza la cancellazione di file di progetto esistenti.
- Agenti diversi lavorano in parallelo solo su insiemi di file disgiunti.
- Le regole operative complete stanno in `.claude/CLAUDE.md`.
