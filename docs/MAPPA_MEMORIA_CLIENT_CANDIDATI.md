# Mappa di memoria del client — offset candidati da fonte terza

**Stato:** ipotesi non verificate. Nessun numero di questo documento è `LIVE`.
**Data:** 2 settembre 2026
**Si appoggia a:** [ADR-0014](adr/ADR-0014-operator-chooses-the-data-path.md),
[ADR-0012](adr/ADR-0012-gameplay-observation-source.md),
`src/NosAi.Runtime/LiveIntegration/NosTaleClientLayout.cs`

## 1. Che cosa è questo documento e che cosa non è

È un elenco di **candidati**: offset e catene di puntatori estratti da un bot NosTale
di terze parti (DLL C/C++ iniettata, con la relativa tabella Cheat Engine), riferiti a
una build del client **ignota e quasi certamente diversa** da quella corrente.

Non è una mappa da cablare nel codice. ADR-0012 ha stabilito il motivo, e ADR-0014 non
lo ha revocato: *un offset sbagliato non fallisce, restituisce un numero plausibile*.
Un numero copiato da un bot altrui è esattamente la forma di errore che questo progetto
rifiuta ovunque — funziona finché non funziona, e quando non funziona non lo dice.

Il valore di questo elenco è duplice e limitato:

1. **Corroborazione.** Dove una fonte indipendente concorda con quello che
   `NosTaleClientLayout` ha già stabilito con i propri oracoli, la concordanza vale
   come seconda fonte nel senso che ADR-0014 chiede.
2. **Ipotesi di partenza.** Dove descrive campi che il layout attuale non legge —
   nomi delle entità, HP/MP, cooldown — indica *dove guardare*, non *cosa credere*.
   Il percorso da ipotesi a lettura è descritto in
   [SPEC_ESTENSIONE_LAYOUT_MEMORIA.md](SPEC_ESTENSIONE_LAYOUT_MEMORIA.md).

## 2. Convenzione della fonte

La fonte risolve le catene così, e ogni offset qui sotto va letto con questa regola:

```text
addr = moduleBase + rva
addr = *addr
per i in 0 .. n-2:   addr = *(addr + offsets[i])
addr = addr + offsets[n-1]          // indirizzo finale, NON dereferenziato
```

Nota: con un solo offset il ciclo non gira e l'offset viene sommato dopo la prima
dereferenza. Con lista vuota la fonte va in undefined behavior; non replicarlo.

## 3. Classe A — concordanze con quanto già stabilito

Queste **non sono novità**: sono conferme. Il layout del repo le ha ricavate per conto
proprio, con oracoli comportamentali e verifica al riavvio. Che una fonte indipendente,
scritta contro un'altra build, descriva gli stessi campi negli stessi punti è la cosa
più vicina a una prova che si possa avere senza il sorgente del client.

| Campo | Fonte terza | `NosTaleClientLayout` | Esito |
|---|---|---|---|
| Entity id | `+0x08` sull'oggetto entità | `EntityIdOffset = 0x08` | concorda |
| Posizione entità | `+0x0C` X int16, `+0x0E` Y int16 | `PositionOffset = 0x0C`, letto come un `uint32` con X nella metà bassa e Y nell'alta | concorda: sono la stessa cosa descritta in due modi |
| Impacchettamento coordinate | le call del client vogliono `Y * 65536 + X` | il layout decodifica `x = packed & 0xFFFF`, `y = packed >> 16` | concorda |
| Array delle liste | offset intermedio `0x04` nelle catene lista | `ListArrayOffset = 0x04` | concorda |

La terza riga merita una nota: la fonte usa quell'impacchettamento in **scrittura**
(è il parametro che passa alle funzioni di movimento del client), il repo lo trova in
**lettura**. Due usi opposti dello stesso formato sono un indizio più forte di due
letture concordi, perché un errore di lettura ripetuto identico è possibile, un errore
che sopravvive anche all'andata è molto meno probabile.

## 4. Classe B — campi nuovi, plausibili, non verificati

Nessuno di questi esiste oggi in `NosTaleClientLayout`. Sono le ipotesi utili.

### 4.1 Nome delle entità

| Entità | Catena dall'oggetto entità |
|---|---|
| Mostro | `+0x1BC` → `+0x04` = `char*` ANSI |
| Item a terra | `+0xC4` → `+0x38` = `char*` ANSI |

Le due catene sono diverse fra mostri e item: non assumere simmetria. La tabella Cheat
Engine etichetta indipendentemente `+0xC4 → DoNazwy` sulla struttura "Monsters" e
`+0x38 → Nazwa Przedmiotu` sull'item, il che suggerisce che esistano più percorsi al
nome e che quello sopra sia solo uno dei due.

### 4.2 Statistiche del personaggio

Catena della fonte: RVA `0x004F4BA8`, offset `{0xE4, 0x100, 0x4C8, 0x8B8}`.

| Campo | Offset dal blocco |
|---|---|
| MaxMP | `+0x00` (uint32) |
| MP | `+0x04` (uint32) |
| MaxHP | `+0xF0` (uint32) |
| HP | `+0xF4` (uint32) |

È il campo più interessante che manca al runtime: ADR-0012 indicava HP/MP come primo
provider di gameplay, e finora non c'è. **Non usare l'RVA.** La strada corretta è
cercare il blocco a partire dalle basi già risolte — vedi la spec di estensione.

### 4.3 Cooldown delle abilità

| Campo | Catena | Nota |
|---|---|---|
| Abilità 1-4 | RVA `0x004F4DD0`, `{0x158, 0x4, 0x4, 0x0, 0x24}` | passo `(n-1) * 0x48`, valore `0` = pronta |
| Abilità 5+ | RVA `0x004F4CDC`, `{0x20, 0x4, 0x88, 0xE28, 0x24}` | stesso passo |
| Numero abilità | RVA `0x004F4C70`, `{0x3EC, 0x768}` | |

Due tabelle distinte per due intervalli di abilità è una struttura strana abbastanza da
essere probabilmente vera: nessuno la inventerebbe. La tabella Cheat Engine riporta per
`Skill1` una catena leggermente diversa (`{0x158,0x4,0x4,0x0,0x8,0x14}`) da quella nel
codice (`{0x158,0x4,0x4,0x0,0x24}`): **le due fonti non concordano con se stesse**, il
che declassa ulteriormente questo gruppo.

### 4.4 Item a terra — campi oltre la posizione

| Campo | Offset |
|---|---|
| Id item | `+0x08` |
| Quantità | `+0x20` (uint32) |
| Flag consumabile | `+0x0C` |

Attenzione: `+0x08` come "id item" confligge con `EntityIdOffset = 0x08` già stabilito.
O è lo stesso campo con due nomi, o una delle due letture è sbagliata. Va risolto prima
di usare uno dei due su un item.

### 4.5 Altri

| Campo | Catena | Nota |
|---|---|---|
| Portata d'attacco | RVA `0x004F4904`, `{0x68}`, BYTE | la fonte la corregge a mano (`-3`, e `+1` per il corpo a corpo): sospetto che non sia una portata pura |
| Partner | RVA `0x004F4908`, `{0x4, 0x0}` | |
| Pet | RVA `0x004F4908`, `{0x4, 0x4}` | |
| Entità selezionata | RVA `0x004F4DC0`, `{0x1D0,0x20,0x4,0xC,0x9A0}` | il repo ha già `TargetPointerOffset = 0x44` sul manager, trovato per oracolo: **percorso diverso, non usare questo** |

## 5. Classe C — inutilizzabile

Tutti gli RVA di base (`0x004F4904`, `0x003566D8`, `0x003582C0`, `0x00360E7C`, …) e le
catene di navigazione verso le liste entità (`{0xEA4,0x4,0x5E4,0x0}` per i mostri,
`{0xEB0,0x4,0x5C4,0x0}` per gli item). Motivi, in ordine di gravità:

1. Appartengono a un'altra build. Un RVA è un fatto su un binario, non sul gioco.
2. Il repo raggiunge le stesse liste da un'altra parte — scene manager per signature,
   liste a `+0x0C/+0x10/+0x14/+0x18` — e quel percorso è già verificato. Sostituirlo
   con questo sarebbe una regressione, non un'aggiunta.
3. La fonte itera `monsterCount - 1`. Non è chiaro se sia un fuori-di-uno voluto o un
   bug; in entrambi i casi non è un conteggio di cui fidarsi.

## 6. Errori noti nella fonte

Registrati perché qualcuno, prima o poi, riaprirà il codice originale.

- Precedenza sbagliata: `skill - 1 * 0x48` invece di `(skill - 1) * 0x48`. Presente in
  un ramo e corretto in un altro dello stesso file.
- Condizione di prossimità con `&&` dove serve `||`: la raccolta si ferma appena il
  personaggio è allineato su un asse.
- Nessuna validazione dei puntatori prima della dereferenza. La fonte gira **dentro** il
  processo del client, quindi una catena rotta lo fa crashare. Noi leggiamo da fuori:
  lo stesso errore da noi produce `UNKNOWN`, che è il motivo per cui leggiamo da fuori.

## 7. Che cosa fare con questo documento

Niente, direttamente. È materiale in ingresso per
[SPEC_ESTENSIONE_LAYOUT_MEMORIA.md](SPEC_ESTENSIONE_LAYOUT_MEMORIA.md), che descrive
come un candidato diventa una lettura. Un offset di qui che comparisse in `src/` senza
essere passato da quella procedura è un difetto, non una scorciatoia.
