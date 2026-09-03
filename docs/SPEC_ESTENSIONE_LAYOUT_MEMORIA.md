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

**Implementata il 3 settembre 2026 e provata su client reale lo stesso giorno.**
La concordanza con il filo c'è ed è registrata sotto; l'RVA `0x004F4BA8` non è nel
codice. Livello raggiunto: **`Integrated`**, non `Verified`, e il motivo non è la
concordanza ma l'ancora che manca — vedi «Quel che resta aperto». Classificazione
in codice: `UNKNOWN` (`player_vitals_not_established`).

### Che cosa non ha funzionato

La procedura scritta qui in origine cercava il blocco della fonte terza nelle
finestre attorno alle basi già risolte e sceglieva i candidati per **forma**:
quattro `uint32` con `0 <= valore <= massimo`, i massimi non nulli, la distanza fra
le coppie costante fra due letture. Su un client reale non ha prodotto niente di
utile, per una ragione che vale la pena scrivere perché leggendo il filtro non si
vede: **la forma non discrimina.** Una parola a zero soddisfa `0 <= valore <=
massimo` contro qualunque massimo, sempre e a costo zero, e un heap grande è pieno
di quadruple che passano. Il filtro ha restituito rumore, e nella lista non c'era
vita.

Il blocco cercato non c'era comunque: su questa build `MaxMP` e `MP` non stanno a
`-0xF4` e `-0xF0` da HP, e in 128 byte attorno a HP il valore che il filo dava per
`maxMp` non compare affatto — § 7.3 del documento di ingresso. Le costanti
`0x00/0x04/0xF0/0xF4` di `PlayerVitalsBlock` descrivono quel blocco.

Una scansione differenziale sull'intero processo, invece, HP lo trova: tre passaggi
di `--memory-scan` / `--memory-narrow` mentre il valore si muoveva hanno portato a
`0x1F7AEC7C`. Ma è una procedura che l'operatore guida a mano un passaggio per
volta, dice *dove* è HP e non *che cosa* lo tiene, e consegna un indirizzo assoluto
— cioè proprio la cosa che la § 3 vieta di conservare.

### Che cosa ha funzionato — invertire la domanda

Il filo non porta solo percentuali: `stat` porta `hp maxHp mp maxMp` come quattro
interi assoluti, confermati contro l'HUD (`docs/PROTOCOLLO_NOSTALE.md`). Con quei
numeri in mano la domanda smette di essere «che cosa somiglia a della vita», che non
ha una risposta finita, e diventa «dove stanno questi due numeri, uno accanto
all'altro», che ne ha una.

Procedura, nell'ordine — è quella che esegue `--calibrate-vitals`:

1. Prendere dal `stat` più recente i quattro interi. Senza `stat` la sonda rifiuta:
   non c'è niente da cercare, e cercare comunque vorrebbe dire tornare alla forma.
2. Scandire la memoria privata del processo e tenere solo gli indirizzi che portano
   `maxHp` con `hp` nella parola successiva (`[a] == maxHp`, `[a+4] == hp`). Idem
   per la coppia MP. Un giro solo non prova niente: su un heap grande qualche coppia
   non correlata terrà quei due numeri per caso.
3. Aspettare che il filo riporti un corrente **diverso** e rifare lo stesso
   controllo sui soli sopravvissuti. Chi teneva la coppia per coincidenza non ha
   ragione di seguirla quando cambia; chi è vita la segue. Se il valore non si è
   mosso, il secondo giro rifà la prima domanda e non conferma niente: va detto,
   non contato.
4. Un solo sopravvissuto è il campo. Zero e più di uno sono due esiti nominati e
   distinti, non un fallimento generico.
5. Esprimere il risultato come distanza da una base risolta, non come indirizzo.
   **Questo passo non è fatto** — vedi sotto.
6. Ripetere dopo un riavvio del client. Un offset che non sopravvive al riavvio è un
   indirizzo che ha funzionato una volta.

Nessuno di questi passi chiede a una persona quale candidato sembri giusto. È la
differenza fra la scansione differenziale del paragrafo precedente e questa: la
stessa evidenza, ma il giudizio sta in un predicato invece che nell'occhio
dell'operatore.

### Che cosa la sessione del 3 settembre ha stabilito

Client reale, personaggio `3443217`, `stat` come seconda fonte. I dump e i numeri
stanno in [MAPPA_MEMORIA_CLIENT_CANDIDATI.md § 7](MAPPA_MEMORIA_CLIENT_CANDIDATI.md);
qui l'esito:

- **`MaxHP` sta nei quattro byte immediatamente prima di `HP`.** È l'unica parte
  della descrizione della fonte terza che ha retto, ed è la forma che la procedura
  sopra cerca.
- **Il blocco unico `{MaxMP, MP, MaxHP, HP}` non esiste su questa build.**
- **Le due coppie distano `0x78`**, che è anche il passo con cui la struttura si
  ripete: HP e MP sono lo stesso campo di due record consecutivi, non due campi
  della stessa struttura. Che cosa siano quei record non è noto.
- **Del vecchio indirizzo hanno resistito al riavvio i 16 bit bassi**, non gli altri.
  Due campioni: è un fatto misurato, non ancora una regola.

### Quel che resta aperto — l'ancora

L'indirizzo trovato è heap, e il riavvio ne ha ucciso uno: la domanda se un'ancora
serva è chiusa, la risposta è sì. Finché la coppia non si esprime come distanza da
una base che `TryResolveBases` risolve a ogni aggancio, quello che esiste è una
calibrazione da rifare a ogni sessione, non una lettura — e la § 3 lo chiede a ogni
campo.

Sui criteri della § 8 questo si legge senza ambiguità: il punto 4 è soddisfatto,
una sessione reale ha registrato la concordanza; il punto 1 no, perché la lettura
non si regge su una base risolta. Livello: **`Integrated`**. A `Verified` manca
l'ancora, non la prova.

### Seconda fonte

Il filo porta HP e MP del personaggio come **valori assoluti**, sul pacchetto `stat`
(`hp maxHp mp maxMp`, confermati contro l'HUD in `docs/PROTOCOLLO_NOSTALE.md`). È la
seconda fonte usata dalla procedura sopra, ed è ciò che rende possibile invertire la
domanda: il controllo incrociato è un'uguaglianza fra interi, non il confronto fra un
rapporto letto dalla memoria e una percentuale che il client ha arrotondato.

Il filo non vede la memoria del client e la memoria non vede il filo: è esattamente
la forma di prova che ADR-0014 chiede.

Una conseguenza operativa: `stat` è mandato quando il numero cambia, non a intervalli
regolari (ADR-0012). Il secondo giro quindi non si programma, si aspetta — e senza un
`stat` nuovo con un corrente diverso la sonda dichiara che non può confermare, invece
di riconfermare la prima domanda.

### Predicato di validità permanente

Girano a ogni lettura, non solo in fase di scoperta:

- `0 <= hp <= maxHp` e `0 <= mp <= maxMp`, con i massimi non nulli;
- continuità: una variazione di HP superiore al massimo in un intervallo troppo breve è
  un puntatore che si è spostato, non un colpo;
- coerenza con l'ultimo `stat`, quando il filo ha parlato di recente: i massimi devono
  coincidere esattamente — cambiano di rado, quindi un massimo diverso da quello del
  filo è un puntatore che si è spostato — e il corrente deve stare entro la variazione
  plausibile nell'intervallo fra le due letture, perché le due sorgenti non sono
  campionate nello stesso istante.

Se uno cede: `UNKNOWN` con la ragione del controllo fallito.

## 6. Fase 3 — Cooldown delle abilità

**Implementata il 3 settembre 2026.** Oracolo e comando esistono; classificazione
`UNKNOWN` finché non converge. Concordanza su sessione reale ancora da registrare —
livello raggiungibile: `Integrated`. Nessuna delle due catene discordanti è nel
codice, e il passo `0x48` è **misurato e riportato**, mai assunto: `ObservedStride`
restituisce `null` quando i salti non concordano invece di farne una media.

Il vincolo richiede entrambe le direzioni, ed è la ragione per cui regge: una parola
permanentemente zero soddisfa « zero quando è pronta » a ogni ripristino, gratis, per
sempre, quindi è la *risalita* a escluderla. I ripristini richiesti sono due perché
il primo registra soltanto ciò che la parola ha fatto, mentre il secondo è quello che
la coincidenza non ripete.

L'operatore preme il tasto e il filo dice quando l'abilità torna: `su` riporta il
colpo e il bersaglio, non lo slot, quindi nessuna sorgente sul filo dice *quale*
abilità è stata usata. Quello che l'operatore non fornisce mai è il momento del
ripristino, che è `sr` — e senza filo la sonda **rifiuta** invece di controllare le
discese sullo stesso cronometro che le ha prodotte.

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
