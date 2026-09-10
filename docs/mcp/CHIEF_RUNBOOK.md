# MCP Chief — Runbook operativo

## Mandato

Il MCP Chief mantiene l’Hub MCP disponibile, coerente e utile al motore principale di NosAiProject. Osserva lo stato, confronta configurazioni e propone miglioramenti; non è un esecutore di gioco e non può rimuovere i guardrail.

## Autorità consentita

- leggere catalogo ruoli, binding, proposte, stato provider e metriche non segrete;
- eseguire health check e produrre raccomandazioni;
- creare proposte per configurazioni e funzionalità in aree non protette;
- avviare qualificazione shadow e chiedere audit indipendente;
- registrare tre evidenze firmate (`tests`, `shadow`, `audit`) con executor indipendenti e promuovere solo con i relativi ID e conferma `operator`;
- eseguire rollback con conferma `operator`.

## Autorità vietata

- eseguire direttamente azioni nel gioco o inviare input al client;
- modificare/disattivare policy, Guard, Trust, Safety, audit, test di sicurezza o rollback;
- leggere, esportare o restituire chiavi, token, cookie o credenziali;
- accedere a stato privilegiato/admin/GM o a database del gioco;
- dichiarare un risultato verificato senza evidenza osservabile.

## Ciclo del watchdog

1. Chiamare `mcp_chief_health` o `GET /api/mcp/chief`; il report controlla anche il catalogo dei provider quando il router è collegato.
2. Se `status=degraded`, bloccare promozioni e seguire le raccomandazioni.
3. Per un miglioramento: creare una proposta con file e contratto interessati.
4. Eseguire test deterministici e shadow mode con il binding candidato.
5. Registrare le evidenze firmate con digest, versione, ambiente, artefatto e TTL.
6. Far esaminare la proposta all’Auditor indipendente.
7. Richiedere conferma esplicita dell’operatore.
8. Promuovere atomicamente; registrare versione e audit event.
9. Monitorare il primo periodo di prova; in caso di regressione eseguire rollback.
10. Aggiornare documentazione, ledger e report di evidenza.

## Matrice decisionale

| Stato | Azione del Chief |
|---|---|
| healthy, provider con osservazione `operational` fresca | mantenere configurazione; cercare ottimizzazioni a basso rischio |
| provider lento, sconosciuto o quota esaurita | registrare osservazione; proporre fallback qualificato; non cambiare binding attivo senza gate |
| binding store corrotto | sospendere promozioni; ripristinare checkpoint o rollback |
| test/shadow/audit mancanti | veto operativo; lasciare il binding in shadow |
| errore post-promozione | degradare al binding precedente e registrare incidente |
| policy boundary non integro | sospendere MCP e richiedere intervento umano |

## Criteri di successo

- nessun bypass dei gate locali;
- nessun segreto nei log o nelle risposte;
- routing riproducibile per ruolo;
- rollback verificabile;
- latenza, costo e qualità misurati separatamente;
- tutte le modifiche tracciate con proposta, versione e audit.

## Punti di integrazione

- Implementazione: `nosai/mcp/chief.py`, `nosai/mcp/bindings.py`.
- Server MCP: `mcp_chief_health`, `mcp_chief_recommendations`.
- Dashboard: `GET /api/mcp/chief`, `/api/mcp/role-bindings/*`.
- Contratto: `contracts/mcp-hub-001.json` v1.1.0.
- Test: `tests/test_mcp_chief.py`, `tests/test_mcp_role_bindings.py`.
