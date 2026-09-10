# Sequenza di programmazione e inserimento MCP
Questa tabella collega la roadmap canonica alle guide; non riapre task DONE.
Per ogni fase verificare coda, dipendenze e prove sul commit corrente.

| Fase canonica | Area | Gate minimo | Uso della guida MCP |
|---|---|---|---|
| AP-00 | Hardware, AutoSet, budget, bootstrap | profilo riproducibile, budget e fallback | LAB-01/02: fondazioni isolate, dopo baseline CI |
| AP-01 | World Model e fusione | provenienza, freshness, UNKNOWN, proprietà stato C-204 | definire snapshot MCP; niente seconda verità |
| AP-02 | Perception e cattura | sensori reali e replay con code limitate | LAB-03/04: supervisore e worker dopo stato/budget |
| AP-03 | Ricostruzione mappe | persistenza incrementale e revisione mappa | ricerca genera candidati, non mappe vere |
| AP-04 | Navigazione ed esplorazione | stuck detection, ostacoli e replanning | valutare percorsi su replay |
| AP-05 | Combattimento e simulazione | danni/cooldown reali, ranking e verifica | LAB-05: esperimenti solo dopo baseline simulatore |
| AP-06 | Quest e obiettivi | quest graph e verifiche multi-step | laboratorio confronta strategie con obiettivi osservabili |
| AP-07 | Inventario, build, equipaggiamento | pre/postcondizioni e costo risorse | esperimenti di build; contratti dedicati per pet/partner |
| AP-08 | Autonomia e pianificazione | unica catena Guard/Trust/Safety e recovery | servizi MCP asincroni non bloccano il loop locale |
| AP-09 | Memoria, apprendimento e replay | persistenza, revoca e consolidamento offline | completare LAB-05; LAB-06 release; LAB-07 pannello |
| AP-10 | Certificazione completa | Windows end-to-end e regressioni offline/online | certificare LAB-07 prima di dichiarare MCP operativo |

## Quando aprire la guida MCP
**Prima apertura: durante AP-00, dopo baseline build/test**, se si lavora alle
fondazioni MCP. Eseguire il discovery di LAB-01 e poi LAB-02 come ramo di supporto
isolato. Non serve aspettare AP-09 per riparare le evidenze o i binding.
Per ogni LAB seguire mcp/AI_PROGRAMMING_GUIDE.md: non duplicarne gli incarichi qui.

**Integrazione runtime: dopo contratti World Model e risoluzione C-204**.
Definire snapshot/proposte, versioni, ownership e fallback prima di collegare
server MCP e core. LAB-03/04 usano budget AP-00 e stato coerente.
Il processo live deve continuare offline senza dipendenza dal laboratorio.

**Esperimenti: AP-05 e AP-09**. LAB-05 richiede simulatore/replay affidabili,
baseline e metriche reali. Un simulatore finto non qualifica modelli.
**Rilasci e pannello: AP-09, prima della certificazione AP-10**.
LAB-06 dipende da LAB-05; LAB-07 segue LAB-06. Nessun rilascio autonomo prima
di evidenze indipendenti e servizio di autorizzazione.
**Certificazione AP-10**: offline, online autorizzato, perdita rete, restart,
rollback, pressione risorse e confronto con motore locale senza MCP.

## Lavori trasversali
- Sicurezza/protocollo: accompagnano ogni fase, non un controllo solo finale.
- Pannello/Host: stato reale fin da AP-00; controlli MCP nella fase indicata.
- Storage/replay: fondazioni con World Model; consolidamento in AP-09.
- GuardAi/Android: consultare baseline e ADR, definire contratto PC/telefono e
  isolamento del guasto prima di nuove dipendenze. Nessun obbligo del telefono
  per il funzionamento offline del PC.
- Pet/partner: parte delle decisioni combat/build; prima verificare contratti e
  moduli già presenti tramite indice, non crearne copie.
- Provider/tooling di sviluppo: server storico distinto dal MCP Hub operativo.
- CI e documentazione: prove per ogni integrazione, rilascio finale con evidenze.

## Confini da verificare a ogni collegamento
Produttore -> contratto versionato -> consumatore -> risultato verificato.
Annotare serializzazione, unità, clock, code, timeout, cancellazione, errore,
provenienza e autorità. Snapshot di gioco osservato distinto da simulazione.
Non collegare un modulo direttamente all'adapter saltando il percorso locale.
