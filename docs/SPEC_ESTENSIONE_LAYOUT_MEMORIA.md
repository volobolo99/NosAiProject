# Estensione di NosTaleClientLayout — nomi, statistiche, cooldown

**Stato:** specifica di lavoro, pronta per l'implementazione.
**Ambito:** `src/NosAi.Runtime/LiveIntegration/`
**Si appoggia a:** [ADR-0014](adr/ADR-0014-operator-chooses-the-data-path.md),
[ADR-0012](adr/ADR-0012-gameplay-observation-source.md),
[ADR-0021](adr/ADR-0021-the-clients-memory-establishes-the-target.md)
**Ingresso:** [MAPPA_MEMORIA_CLIENT_CANDIDATI.md](MAPPA_MEMORIA_CLIENT_CANDIDATI.md)

## 1. Che cosa manca oggi

`NosTaleClientLayout` risolve player manager e scene manager per signature, e legge
posizione del personaggio, id, bersaglio, map id e le quattro liste entità. Ogni entità
torna come `MapEntityReading(EntityId, X, Y)`.

Mancano tre cose che il WorldState richiede e che nessuna altra sorgente fornisce bene:

| Campo | Perché la memoria e non altro |
|---|---|
| **Nome dell'entità** | Il filo li nomina solo quando li menziona: una cattura che parte a metà sessione ha 25 `in` contro 7685 `mv`. Lo schermo li legge come pixel. Il client li ha tutti, subito. |
| **HP / MP assoluti** | ADR-0012 li indicava come primo provider di gameplay e non esistono ancora. Il filo porta percentuali, non valori. |
| **Cooldown abilità** | Nessuna fonte alternativa. Senza, il Ranking propone azioni non eseguibili e lo scarto lo scopre il Verify. |

## 2. Il vincolo che governa tutto il lavoro

Ogni campo nuovo attraversa tre stati, e **il salto lo autorizza una seconda fonte
indipendente, mai la plausibilità del numero**. È la regola che ha già governato il map
id (portale + riavvio) e il puntatore al bersaglio (oracolo su sei selezioni, due
azzeramenti e un riavvio).

```text
CANDIDATO  →  il campo è leggibile e la forma è plausibile
              classificazione: UNKNOWN, con la ragione che lo dice
              ↓  una seconda fonte concorda, ripetutamente
STABILITO  →  esiste un predicato di validità che gira a ogni lettura
              classificazione: LIVE finché il predicato regge
              ↓  il predicato cede
UNKNOWN    →  con la ragione del controllo fallito. Mai l'ultimo valore buono.
```

Non introdurre un quarto stato, non collassare i tre in due, e non classificare `LIVE`
un campo la cui seconda fonte non ha ancora concordato in una sessione reale.

## 3. Regole di implementazione

- **Nessun RVA cablato.** Gli offset del documento di ingresso servono per orientare una
  ricerca, non per essere scritti in una costante. Un indirizzo trovato scandendo si
  esprime come distanza da una base che `TryResolveBases` risolve a ogni aggancio.
- **Nessuna cache.** La catena si segue a ogni chiamata, come già fa `TryReadPlayer`.
  Il manager viene sostituito al cambio mappa; un indirizzo ricordato è la lettura di
  ciò che occupa quella memoria dopo.
- **Ogni rifiuto ha un nome.** Stessa forma delle ragioni esistenti
  (`player_manager_null`, `entity_list_length_implausible:{n}`), in `snake_case`,
  con il valore visto quando aiuta a decidere.
- **Limiti prima delle allocazioni.** Una lunghezza o un conteggio letti da una catena
  sbagliata sono quattro byte qualunque. Vanno confrontati con un limite prima di
  dimensionare un ciclo, come già fa `MaxEntitiesPerList`.
- **Zero allocazioni sul percorso caldo**, `Span<T>` per il parsing, buffer riusati.
- **Codice e identificatori in inglese, commenti che spiegano il perché.** I commenti che
  ridicono il codice non servono; quelli che dicono da dove viene un numero, sì.

## 4. Fase 1 — Nome delle entità

**Implementata il 3 settembre 2026.** Lettura e comando esistono; classificazione
`UNKNOWN` (`entity_name_not_established`). Concordanza su sessione reale ancora
da registrare — livello raggiungibile: `Integrated`.

### Lettura

Estendere `MapEntityReading` con il nome. Catene candidate dall'oggetto entità:
mostro `+0x1BC → +0x04`, item a terra `+0xC4 → +0x38`, entrambe `char*` ANSI.
Non assumere che le due siano simmetriche né che valgano per NPC e giocatori: ogni
`MapEntityKind` va trattato come un caso a sé finché non è dimostrato il contrario.

Lettura della stringa: lunghezza massima 64 byte, terminazione al primo `\0`, rifiuto
se compare un byte fuori dall'insieme stampabile atteso. Una stringa che non passa
questi controlli non è un nome corto: è una catena sbagliata.

### Seconda fonte

Il pacchetto `in` sul filo nomina l'entità che compare, con il suo id. Per ogni entità
il cui id compare sia nella lista in memoria sia in un `in` osservato nella sessione, il
nome letto dalla memoria deve coincidere. Il campo diventa `STABILITO` quando la
concordanza regge su un numero significativo di entità e sopravvive a un cambio mappa.

Fino ad allora: il nome viaggia come candidato accanto all'id, e nulla a valle può
decidere su di esso.

### Verifica operatore

Un comando nel menu esistente che stampa la lista entità con id, posizione e nome
candidato, e affianca il nome che il filo ha dato per lo stesso id quando disponibile.
La discordanza deve essere visibile a occhio, non nascosta in un log.

## 5. Fase 2 — HP e MP

**Implementata il 3 settembre 2026.** Ricerca sulle basi risolte e comando
esistono; classificazione `UNKNOWN` (`player_vitals_not_established`). Concordanza
su sessione reale ancora da registrare — livello raggiungibile: `Integrated`.
L'RVA `0x004F4BA8` non è nel codice.

### Lettura

Il blocco statistiche della fonte terza sta a `{MaxMP, MP, MaxHP, HP}` con offset
`+0x00, +0x04, +0xF0, +0xF4`, tutti `uint32`. **L'RVA di partenza non si usa.**

Procedura corretta, nell'ordine:

1. Cercare il blocco a partire dalle basi già risolte (player manager, player object)
   usando l'oracolo: HP e MP sono gli unici due valori che *cambiano quando il
   personaggio subisce danno e non altrimenti*, e i rispettivi massimi sono gli unici
   che restano fermi mentre quelli cambiano e saltano al passaggio di livello.
2. Filtrare i candidati con la relazione strutturale: quattro `uint32` con
   `0 <= valore <= massimo`, i due massimi diversi da zero, e la distanza fra le coppie
   costante fra due letture.
3. Esprimere il risultato come distanza da una base risolta, non come indirizzo.
4. Ripetere dopo un riavvio del client. Un offset che non sopravvive al riavvio è un
   indirizzo che ha funzionato una volta.

### Seconda fonte

Il filo porta HP e MP come **percentuali** (`cond`, `st`). La memoria porta valori
assoluti e i massimi. Il controllo incrociato è diretto: `hp / maxHp` letto dalla
memoria deve corrispondere alla percentuale del filo entro la tolleranza di
arrotondamento del client. Due rappresentazioni diverse dello stesso fatto da due
sorgenti indipendenti: è esattamente la forma di prova che ADR-0014 chiede.

### Predicato di validità permanente

Girano a ogni lettura, non solo in fase di scoperta:

- `0 <= hp <= maxHp` e `0 <= mp <= maxMp`, con i massimi non nulli;
- continuità: una variazione di HP superiore al massimo in un intervallo troppo breve è
  un puntatore che si è spostato, non un colpo;
- coerenza con la percentuale del filo, quando il filo ha parlato di recente.

Se uno cede: `UNKNOWN` con la ragione del controllo fallito.

## 6. Fase 3 — Cooldown delle abilità

La fase più debole e va dichiarata tale: le due fonti disponibili **non concordano fra
loro** sulla catena (`{…,0x0,0x24}` nel codice contro `{…,0x0,0x8,0x14}` nella tabella).
Non partire dai numeri.

Partire dall'oracolo comportamentale, sullo stampo di `TargetIdFinder`: una parola è
candidata solo se scende a zero esattamente quando l'abilità torna disponibile e risale
esattamente quando viene usata. Il passo `0x48` fra abilità consecutive e l'esistenza di
due tabelle separate per gli intervalli 1-4 e 5+ sono ipotesi da confermare con
l'oracolo, non premesse.

Seconda fonte: il filo annuncia il ripristino dell'abilità. La concordanza fra
l'istante in cui la parola va a zero e l'istante del pacchetto è la prova.

Se l'oracolo non converge, **il campo resta `UNKNOWN` e la fase si chiude comunque**.
Un cooldown ignoto è un'informazione onesta; un cooldown sbagliato fa proporre al
Ranking azioni che il Verify scoprirà fallite una per una.

## 7. Fuori ambito

- Qualunque scrittura nella memoria del client.
- Qualunque chiamata alle funzioni del client. Richiederebbe codice nativo x86 in-process
  per la convenzione di chiamata Delphi: vedi [SPEC_SHIM_NATIVO_X86.md](SPEC_SHIM_NATIVO_X86.md),
  che non è approvata per l'implementazione.
- Sostituire i percorsi già stabiliti (scene manager, target pointer) con quelli della
  fonte terza.
- Evasione dei sistemi di rilevamento, esclusa da ADR-0014 e non riaperta qui.

## 8. Criteri di accettazione

Una fase è chiusa quando, per il suo campo:

1. la lettura esiste, senza RVA cablati e senza cache;
2. il predicato di validità gira a ogni lettura e ha una ragione nominata per ogni ramo
   di rifiuto;
3. esiste un comando nel menu che mostra all'operatore lettura e seconda fonte affiancate;
4. una sessione reale registra la concordanza, **oppure** il campo è dichiarato
   `UNKNOWN` con la ragione e la fase si chiude lo stesso;
5. la classificazione in codice riflette 4: nessun `LIVE` senza la concordanza al punto 4.

Livello di verifica raggiungibile senza il punto 4: `Integrated`. Con: `Verified`.
